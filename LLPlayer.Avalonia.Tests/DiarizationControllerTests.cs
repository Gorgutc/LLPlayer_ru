using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Diarization;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class DiarizationControllerTests
{
    [AvaloniaFact]
    public async Task Analysis_uses_exact_audio_index_and_compensates_both_delays()
    {
        using var media = await AvStreamTestMedia.CreateAsync();
        FakeEngine engine = new();
        await using Session session = new(media, engine);
        await session.ReadyAsync();
        Player player = session.Player;
        FlyleafPlaybackController controller = session.Controller;
        await controller.SelectAudioTrackAsync(controller.GetAudioTracks().Single(track => track.Label.Contains("Stereo 44100")));
        int selectedIndex = player.decoder.AudioStream.StreamIndex;
        Assert.Equal(3, selectedIndex);
        player.Config.Audio.Delay = Ms(75).Ticks;
        player.Config.Subtitles[0].Delay = Ms(300).Ticks;
        SubtitleData owner = SetTimedCue(player, 0, "Primary");
        int updates = 0;
        controller.CuesChanged += slot => { if (slot == 0) updates++; };

        Task<int> command = controller.AnalyzeSpeakersAsync(0);
        try
        {
            DiarizationAudioSource input = await Within(engine.Started.Task);
            Assert.Equal(Path.GetFullPath(media.Media), input.Path);
            Assert.Equal(selectedIndex, input.StreamIndex);
            // Raw cue 1000..1100ms, heard at 1225..1325ms after compensating both delays.
            engine.Finish(new(Ms(1000), Ms(1100), "SPEAKER_00"), new(Ms(1225), Ms(1325), "SPEAKER_01"));
            Assert.Equal(1, await Within(command));
            Assert.Equal("Speaker 2", owner.SpeakerId);
            CueInfo cue = Assert.Single(controller.GetCues(0));
            Assert.Equal("Speaker 2", cue.SpeakerId);
            Assert.Equal("Primary", cue.Text);
            Assert.Equal(Ms(1000), cue.Start);
            Assert.Null(Assert.Single(controller.GetCues(1)).SpeakerId);
            await WaitForAsync(() => updates > 0);

            player.Config.Subtitles[0].Delay = Ms(325).Ticks;
            await WaitForAsync(() => owner.SpeakerId == null);
            Assert.Null(Assert.Single(controller.GetCues(0)).SpeakerId);
        }
        finally { engine.Finish(); }
    }

    [AvaloniaTheory]
    [InlineData("media")]
    [InlineData("cues")]
    [InlineData("external audio")]
    public async Task Obsolete_result_cannot_replace_current_cue_labels_even_when_engine_ignores_cancellation(string replacement)
    {
        using var media = await AvStreamTestMedia.CreateAsync();
        FakeEngine engine = new();
        await using Session session = new(media, engine);
        await session.ReadyAsync();
        Player player = session.Player;
        FlyleafPlaybackController controller = session.Controller;
        if (replacement == "external audio")
            await session.SelectExternalAudioAsync(media.ExternalAudio);
        SubtitleData original = SetTimedCue(player, 0, "Original");

        Task<int> command = controller.AnalyzeSpeakersAsync(0);
        try
        {
            DiarizationAudioSource input = await Within(engine.Started.Task);
            SubtitleData current;
            switch (replacement)
            {
                case "media":
                    var opened = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    controller.Open(media.Media, opened.SetResult);
                    Assert.Null(await Within(opened.Task));
                    await session.ReadyAsync();
                    current = SetTimedCue(player, 0, "New media");
                    break;
                case "cues":
                    current = SetTimedCue(player, 0, "Replacement");
                    break;
                case "external audio":
                    string secondAudio = Path.Combine(media.DirectoryPath, "second.m4a");
                    File.Copy(media.ExternalAudio, secondAudio);
                    Assert.Equal(Path.GetFullPath(media.ExternalAudio), input.Path);
                    await session.SelectExternalAudioAsync(secondAudio);
                    Assert.Equal(input.StreamIndex, player.decoder.AudioStream.StreamIndex);
                    Assert.Equal(secondAudio, player.decoder.AudioStream.Demuxer.Url);
                    Assert.Same(original, Assert.Single(player.SubtitlesManager[0].SnapshotSubs()));
                    current = original;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(replacement));
            }
            await Within(engine.Cancelled.Task);
            Assert.True(controller.IsDiarizing);
            Assert.Null(current.SpeakerId);
            engine.Finish(new DiarizationTurn(Ms(0), Ms(2000), "SPEAKER_02"));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Within(command));
            Assert.Null(current.SpeakerId);
            Assert.Null(Assert.Single(controller.GetCues(0)).SpeakerId);
        }
        finally { engine.Finish(); }
    }

    static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    static SubtitleData SetTimedCue(Player player, int slot, string text)
    {
        SubtitleData cue = new() { StartTime = Ms(1000), EndTime = Ms(1100), Text = text };
        player.SubtitlesManager[slot].Load([cue]);
        return cue;
    }

    static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

    static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, timeout.Token);
        }
    }

    sealed class FakeEngine : IDiarizationEngine
    {
        public TaskCompletionSource<DiarizationAudioSource> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource<IReadOnlyList<DiarizationTurn>> result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<DiarizationTurn>> RunAsync(DiarizationAudioSource source, CancellationToken token)
        {
            using var registration = token.Register(() => Cancelled.TrySetResult(true));
            Started.SetResult(source);
            return await result.Task;
        }

        public void Finish(params DiarizationTurn[] turns) => result.TrySetResult(turns);
    }

    sealed class Session : IAsyncDisposable
    {
        public Player Player { get; }
        public FlyleafPlaybackController Controller { get; }

        public Session(AvStreamTestMedia media, FakeEngine engine)
        {
            AvaloniaUIDispatcher dispatcher = new();
            CollectionSyncBridge bridge = new(dispatcher);
            EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
            if (!Engine.IsLoaded)
            {
                EngineConfig engineConfig = EngineBootstrap.CreateEngineConfig(
                    Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR")!, new(media.DirectoryPath, media.DirectoryPath));
                // The process-wide logger must not hold a file in a disposable fixture directory.
                engineConfig.LogOutput = "";
                EngineBootstrap.Start(engineConfig);
            }
            Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
            config.Player.AutoPlay = false;
            config.Subtitles.SearchLocal = false;
            config.Subtitles.LanguageAutoDetect = false;
            Player = new(config);
            Controller = new(Player, dispatcher, bridge, new GpuWorkCoordinator(), () => (engine, false));
            Assert.Null(Player.Open(media.Media).Error);
        }

        public async Task ReadyAsync()
        {
            await WaitForAsync(() => !Player.SubtitlesManager[0].IsLoading && Controller.GetCues(0).Count > 0);
            // The second slot must be ready and distinct so projection proves no cross-slot writes.
            if (!Player.Subtitles[1].Enabled)
            {
                var track = Controller.GetSubtitleTracks().Single(track =>
                    track.Source is SubtitlesStream stream && stream.StreamIndex == 5);
                Controller.SelectSubtitleTrack(1, track);
                await Controller.Recognition.MutateAsync(() => { });
            }
            await WaitForAsync(() => !Player.SubtitlesManager[1].IsLoading && Controller.GetCues(1).Count > 0);
        }

        public async Task SelectExternalAudioAsync(string path)
        {
            var external = new ExternalAudioStream { Url = path, Codec = Path.GetFileName(path), Protocol = "file" };
            PlaylistItem.AddExternalStream(external, Player.Playlist.Selected, "Diarization");
            await Controller.SelectAudioTrackAsync(Controller.GetAudioTracks().Single(track => track.Label.Contains(external.Codec)));
        }

        public async ValueTask DisposeAsync()
        {
            try { await Controller.DisposeAsync(); }
            finally { Player.Dispose(); }
        }
    }
}
