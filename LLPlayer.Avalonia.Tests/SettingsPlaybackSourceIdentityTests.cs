using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class SettingsPlaybackSourceIdentityTests
{
    [AvaloniaFact]
    public async Task Untouched_system_default_draft_preserves_a_newer_explicit_device_intent()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        Assert.Null(player.Open(media.Media).Error);
        ReadinessDiagnostic("device-after-open", player, controller);
        await WaitForDefaultSubtitleCache(player, controller);
        ReadinessDiagnostic("device-after-default-cache", player, controller);
        PlaybackSettings baseline = controller.GetPlaybackSettings();
        session.ConfigureStore(state, baseline);
        PlaybackRuntimeSettings runtime = controller.GetPlaybackRuntimeSettings();
        Assert.True(string.IsNullOrEmpty(runtime.AudioDevice));
        var draft = new PlaybackSettingsViewModel(baseline, runtime, controller.SavePlaybackSettingsAsync);
        const string newerDevice = "missing-device-selected-after-the-draft-opened";
        await controller.SelectAudioDeviceAsync(newerDevice);
        ReadinessDiagnostic("device-after-selection", player, controller);
        Assert.Equal(newerDevice, controller.AudioSettings.SelectedDeviceId);
        draft.Draft.SeekAccurate = !baseline.SeekAccurate;

        ReadinessDiagnostic("device-before-save", player, controller);
        await draft.SaveCommand.ExecuteAsync(null);
        ReadinessDiagnostic("device-after-save: " + (draft.Error ?? "success"), player, controller);

        Assert.Null(draft.Error);
        Assert.Equal(!baseline.SeekAccurate, player.Config.Player.SeekAccurate);
        Assert.True(session.Store!.HasSavedSettings);
        Assert.Equal(newerDevice, controller.AudioSettings.SelectedDeviceId);

        // An explicit edit back to system default must still replace the newer device intent.
        var reset = new PlaybackSettingsViewModel(controller.GetPlaybackSettings(), controller.GetPlaybackRuntimeSettings(),
            controller.SavePlaybackSettingsAsync);
        reset.SelectedAudioDevice = Assert.Single(reset.AudioDeviceChoices, device => device.Id == "");
        await reset.SaveCommand.ExecuteAsync(null);
        Assert.Null(reset.Error); Assert.Equal("", controller.AudioSettings.SelectedDeviceId);
    }

    [AvaloniaFact]
    public async Task Actual_default_subtitle_cache_loading_is_a_legitimate_busy_reason_until_the_cue_is_ready()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        using ManualResetEventSlim release = new();
        TaskCompletionSource loadingEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int uiThread = Environment.CurrentManagedThreadId, loadingThread = 0;
        void HoldDefaultCache(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(SubManager.IsLoading) || !player.SubtitlesManager[0].IsLoading) return;
            loadingThread = Environment.CurrentManagedThreadId;
            loadingEntered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(15)), "Native default subtitle-cache gate was not released.");
        }
        player.SubtitlesManager[0].PropertyChanged += HoldDefaultCache;
        try
        {
            Assert.Null(player.Open(media.Media).Error);
            await loadingEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotEqual(uiThread, loadingThread);
            ReadinessDiagnostic("held-default-cache", player, controller);
            Assert.False(controller.Recognition.IsDispatching);
            Assert.False(player.IsASRRunning);
            Assert.All(Enumerable.Range(0, 2), slot => Assert.False(player.Subtitles[slot].EnabledASR));
            Assert.True(player.SubtitlesManager[0].IsLoading);
            Assert.True(controller.Recognition.IsBusy);
            PlaybackSettings baseline = controller.GetPlaybackSettings(); session.ConfigureStore(state, baseline);
            PlaybackRuntimeSettings runtime = controller.GetPlaybackRuntimeSettings();
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.SavePlaybackSettingsAsync(baseline.Clone(), baseline, runtime with { Baseline = runtime }, false));
            Assert.Contains("subtitle loading", failure.Message);
            Assert.False(session.Store!.HasSavedSettings); Assert.False(File.Exists(session.Store.Path));
        }
        finally { release.Set(); player.SubtitlesManager[0].PropertyChanged -= HoldDefaultCache; }
        await WaitForDefaultSubtitleCache(player, controller);
        ReadinessDiagnostic("completed-default-cache", player, controller);
        Assert.Equal("Primary original", Assert.Single(controller.GetCues(0)).Text);
        Assert.False(player.SubtitlesManager[0].IsLoading);
    }

    static async Task WaitForDefaultSubtitleCache(Player player, FlyleafPlaybackController controller)
    {
        bool Ready() => !player.SubtitlesManager[0].IsLoading && !player.SubtitlesManager[1].IsLoading
            && controller.GetCues(0).Any(cue => cue.Text == "Primary original");
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void CacheChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SubManager.IsLoading) && Ready()) completed.TrySetResult();
        }
        for (int slot = 0; slot < 2; slot++) player.SubtitlesManager[slot].PropertyChanged += CacheChanged;
        try
        {
            if (Ready()) completed.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.True(Ready());
        }
        finally { for (int slot = 0; slot < 2; slot++) player.SubtitlesManager[slot].PropertyChanged -= CacheChanged; }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Old_numeric_subtitle_delay_draft_does_not_change_a_replacement_track_in_the_same_media(int slot)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        Assert.Null(player.Open(media.Media).Error);
        await LoadSubtitle(player, controller, slot, media.Primary);
        PlaybackSettings baseline = controller.GetPlaybackSettings();
        session.ConfigureStore(state, baseline);
        PlaybackRuntimeSettings runtime = controller.GetPlaybackRuntimeSettings();
        SubtitlesStream previousStream = player.decoder.SubtitlesStreams[slot];
        long generation = player.MediaGeneration;

        await LoadSubtitle(player, controller, slot, media.Secondary);
        Assert.Equal(generation, player.MediaGeneration);
        Assert.NotSame(previousStream, player.decoder.SubtitlesStreams[slot]);
        SetSubtitleDelay(player, slot, TimeSpan.FromMilliseconds(250).Ticks);
        Assert.Equal(TimeSpan.FromMilliseconds(250).Ticks, player.Config.Subtitles[slot].Delay);
        PlaybackRuntimeSettings edited = runtime with { Baseline = runtime, ZoomPercent = 125 };
        edited = slot == 0 ? edited with { PrimarySubtitleDelayTicks = TimeSpan.FromMilliseconds(900).Ticks }
            : edited with { SecondarySubtitleDelayTicks = TimeSpan.FromMilliseconds(900).Ticks };

        await controller.SavePlaybackSettingsAsync(baseline.Clone(), baseline, edited, false);

        Assert.Equal(TimeSpan.FromMilliseconds(250).Ticks, player.Config.Subtitles[slot].Delay);
        Assert.Equal(125, player.Config.Video.Zoom); // The media-scoped viewport edit was admitted.
        Assert.True(session.Store!.HasSavedSettings);
        Assert.Equal("Secondary original", Assert.Single(controller.GetCues(slot)).Text);

        // The guard must still admit a freshly captured delay on that exact current source.
        PlaybackRuntimeSettings fresh = controller.GetPlaybackRuntimeSettings();
        PlaybackRuntimeSettings freshEdit = slot == 0
            ? fresh with { Baseline = fresh, PrimarySubtitleDelayTicks = TimeSpan.FromMilliseconds(650).Ticks }
            : fresh with { Baseline = fresh, SecondarySubtitleDelayTicks = TimeSpan.FromMilliseconds(650).Ticks };
        PlaybackSettings current = controller.GetPlaybackSettings();
        await controller.SavePlaybackSettingsAsync(current.Clone(), current, freshEdit, false);
        Assert.Equal(TimeSpan.FromMilliseconds(650).Ticks, player.Config.Subtitles[slot].Delay);
    }

    [AvaloniaFact]
    public async Task Old_numeric_audio_delay_draft_does_not_change_a_replacement_audio_stream_in_the_same_media()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        Assert.Null(player.Open(media.Media).Error);
        await controller.SelectAudioTrackAsync(controller.GetAudioTracks().Single(track => track.Label.Contains("Mono 48000", StringComparison.Ordinal)));
        Assert.Equal(48000, player.decoder.AudioStream.SampleRate);
        PlaybackSettings baseline = controller.GetPlaybackSettings();
        session.ConfigureStore(state, baseline);
        PlaybackRuntimeSettings runtime = controller.GetPlaybackRuntimeSettings();
        AudioStream previousStream = player.decoder.AudioStream;
        long generation = player.MediaGeneration;

        await controller.SelectAudioTrackAsync(controller.GetAudioTracks().Single(track => track.Label.Contains("Stereo 44100", StringComparison.Ordinal)));
        Assert.Equal(generation, player.MediaGeneration);
        Assert.NotSame(previousStream, player.decoder.AudioStream);
        Assert.Equal(44100, player.decoder.AudioStream.SampleRate);
        player.Config.Audio.Delay = TimeSpan.FromMilliseconds(250).Ticks;
        Assert.Equal(TimeSpan.FromMilliseconds(250).Ticks, player.Config.Audio.Delay);

        await controller.SavePlaybackSettingsAsync(baseline.Clone(), baseline, runtime with
        { Baseline = runtime, AudioDelayTicks = TimeSpan.FromMilliseconds(900).Ticks, ZoomPercent = 125 }, false);

        Assert.Equal(TimeSpan.FromMilliseconds(250).Ticks, player.Config.Audio.Delay);
        Assert.Equal(125, player.Config.Video.Zoom);
        Assert.True(session.Store!.HasSavedSettings);

        PlaybackRuntimeSettings fresh = controller.GetPlaybackRuntimeSettings();
        PlaybackSettings current = controller.GetPlaybackSettings();
        await controller.SavePlaybackSettingsAsync(current.Clone(), current, fresh with
        { Baseline = fresh, AudioDelayTicks = TimeSpan.FromMilliseconds(650).Ticks }, false);
        Assert.Equal(TimeSpan.FromMilliseconds(650).Ticks, player.Config.Audio.Delay);
    }

    static async Task LoadSubtitle(Player player, FlyleafPlaybackController controller, int slot, string path)
    {
        ExternalSubtitlesStream stream = new() { Url = path, Title = Path.GetFileName(path), Language = Language.English, Downloaded = true };
        PlaylistItem.AddExternalStream(stream, player.Playlist.Selected, "PlaybackSettingsSourceIdentity");
        controller.SelectSubtitleTrack(slot, controller.GetSubtitleTracks().Single(track => ReferenceEquals(track.Source, stream)));
        await controller.Recognition.MutateAsync(() => { });
        Stopwatch timer = Stopwatch.StartNew();
        while (player.SubtitlesManager[slot].IsLoading || controller.GetCues(slot).Count != 1)
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "Expected the selected subtitle source to finish loading.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static void SetSubtitleDelay(Player player, int slot, long ticks)
    {
        int previous = SubtitlesSelectedHelper.CurIndex;
        try { SubtitlesSelectedHelper.CurIndex = slot; player.Config.Subtitles[slot].Delay = ticks; }
        finally { SubtitlesSelectedHelper.CurIndex = previous; }
    }

    static void ReadinessDiagnostic(string phase, Player player, FlyleafPlaybackController controller)
    {
        string? path = Environment.GetEnvironmentVariable("LLPLAYER_SETTINGS_IDENTITY_DIAGNOSTICS");
        if (string.IsNullOrEmpty(path)) return;
        string line = $"{phase}: status={player.Status}; pending={controller.Recognition.IsDispatching}; busy={controller.Recognition.IsBusy}; asr={player.IsASRRunning}; "
            + $"slot0Asr={player.Subtitles[0].EnabledASR}; slot1Asr={player.Subtitles[1].EnabledASR}; loading0={player.SubtitlesManager[0].IsLoading}; loading1={player.SubtitlesManager[1].IsLoading}; "
            + $"cues0={controller.GetCues(0).Count}; cues1={controller.GetCues(1).Count}";
        string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.AppendAllText(full, line + Environment.NewLine);
    }

    sealed class NativeSession : IAsyncDisposable
    {
        readonly IAudioBackend previousBackend;
        readonly int previousSubtitleSlot;
        public Player Player { get; }
        public FlyleafPlaybackController Controller { get; }
        public PlaybackSettingsStore? Store { get; private set; }
        public NativeSession(TempDir state)
        {
            AvaloniaUIDispatcher dispatcher = new(); CollectionSyncBridge bridge = new(dispatcher);
            EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
            if (!Engine.IsLoaded)
                EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR")!,
                    new AppPaths(Path.Combine(state.Path, "config"), Path.Combine(state.Path, "state"))));
            previousBackend = AudioEngine.Backend; AudioEngine.Backend = NullAudioBackend.Instance;
            previousSubtitleSlot = SubtitlesSelectedHelper.CurIndex; SubtitlesSelectedHelper.CurIndex = 0;
            Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
            config.Player.AutoPlay = false; config.Subtitles.SearchLocal = false; config.Subtitles.LanguageAutoDetect = false;
            Player = new(config); Controller = new(Player, dispatcher, bridge);
        }
        public void ConfigureStore(TempDir state, PlaybackSettings baseline)
        {
            Store = new(Path.Combine(state.Path, "playback.json"));
            Controller.ConfigurePlaybackSettings(Store, baseline);
        }
        public async ValueTask DisposeAsync()
        {
            try { await Controller.DisposeAsync(); Player.Dispose(); }
            finally { AudioEngine.Backend = previousBackend; SubtitlesSelectedHelper.CurIndex = previousSubtitleSlot; }
        }
    }
}
