using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Avalonia.Headless.XUnit;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class PlaybackChoicesTests
{
    [AvaloniaFact]
    public async Task Snapshots_include_native_metadata_and_chapters_without_exposing_native_sources()
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            dispatcher.RunAll();
            var item = player.Playlist.Selected;
            PlaylistItem.AddExternalStream(new ExternalAudioStream
            {
                Url = Path.Combine(state.Path, "audio.wav"), Language = Language.English,
                Codec = "pcm_s16le", BitRate = 768000, Protocol = "file"
            }, item, "test");
            PlaylistItem.AddExternalStream(new ExternalVideoStream
            {
                Url = Path.Combine(state.Path, "video.mkv"), Width = 320, Height = 180,
                FPS = 25, Codec = "h264", Protocol = "file", HasAudio = true
            }, item, "test");
            player.Chapters.Add(new Demuxer.Chapter { Title = "Second part", StartTime = TimeSpan.FromSeconds(3.5).Ticks });
            IPlaybackController choices = controller;
            var audio = choices.GetAudioTracks();
            var video = choices.GetVideoTracks();
            var chapters = choices.GetChapters();
            Assert.Contains(audio, track => track.IsSelected && track.Label.Contains("Hz", StringComparison.Ordinal));
            Assert.Contains(audio, track => track.Label.Contains("English", StringComparison.Ordinal) && track.Label.Contains("768000", StringComparison.Ordinal) && track.Label.Contains("pcm_s16le", StringComparison.Ordinal));
            Assert.Contains(video, track => track.IsSelected);
            Assert.Contains(video, track => track.Label.Contains("320x180", StringComparison.Ordinal) && track.Label.Contains("h264", StringComparison.Ordinal));
            Assert.Contains(chapters, chapter => chapter.Title == "Second part" && chapter.Start == TimeSpan.FromSeconds(3.5));
            foreach (object token in audio.Select(track => track.Token).Concat(video.Select(track => track.Token)).Concat(chapters.Select(chapter => chapter.Token)))
            {
                Assert.IsNotAssignableFrom<StreamBase>(token);
                Assert.IsNotAssignableFrom<ExternalStream>(token);
                Assert.IsNotType<Demuxer.Chapter>(token);
            }
            int audioCount = audio.Count;
            item.ExternalAudioStreams.Clear();
            player.Chapters.Clear();
            Assert.Equal(audioCount, audio.Count);
            Assert.Contains(chapters, chapter => chapter.Title == "Second part");
            player.Stop();
            Assert.Empty(choices.GetAudioTracks());
            Assert.Empty(choices.GetVideoTracks());
            Assert.Empty(choices.GetChapters());
        }
        finally { await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll(); }
    }

    [AvaloniaFact]
    public async Task Removed_foreign_and_fabricated_tokens_do_not_attempt_native_open_or_emit_errors()
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        var (otherPlayer, otherController, _, _) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            dispatcher.RunAll();
            ExternalAudioStream external = new() { Url = Path.Combine(state.Path, "missing.wav"), Codec = "removed-audio" };
            PlaylistItem.AddExternalStream(external, player.Playlist.Selected, "test");
            IPlaybackController choices = controller;
            PlaybackStreamTrack track = Assert.Single(choices.GetAudioTracks(), track => track.Label.Contains("removed-audio", StringComparison.Ordinal));
            int errors = 0, opens = 0;
            controller.ErrorOccurred += _ => errors++;
            player.OpenExternalAudioStreamCompleted += (_, _) => opens++;
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using ManualResetEventSlim release = new();
            Task barrier = controller.Recognition.MutateAsync(() =>
            {
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task selection = choices.SelectAudioTrackAsync(track);
            player.Playlist.Selected.ExternalAudioStreams.Remove(external);
            release.Set();
            await barrier;
            await selection;
            await ((IPlaybackController)otherController).SelectAudioTrackAsync(track);
            await choices.SelectAudioTrackAsync(new("Fabricated", false, external));
            dispatcher.RunAll();
            Assert.Equal(0, opens);
            Assert.Equal(0, errors);
            Assert.False(external.Enabled);
        }
        finally
        {
            await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll();
            await otherController.DisposeAsync(); otherPlayer.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Playback_choice_changes_coalesce_on_dispatcher_and_stop_after_disposal()
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            dispatcher.RunAll();
            List<string?> notifications = [];
            controller.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
            var choices = (IPlaybackController)controller;
            Assert.NotEmpty(choices.GetVideoTracks());
            for (int i = 0; i < 3; i++) player.Chapters.Add(new() { Title = $"Part {i}", StartTime = i * TimeSpan.TicksPerSecond });
            Assert.DoesNotContain("PlaybackChoices", notifications);
            dispatcher.RunAll();
            Assert.Equal(1, notifications.Count(name => name == "PlaybackChoices"));
            Assert.Equal(3, choices.GetChapters().Count);
            notifications.Clear();
            player.Stop();
            dispatcher.RunAll();
            Assert.Contains("PlaybackChoices", notifications);
            Assert.Empty(choices.GetChapters());
            notifications.Clear();
            player.Chapters.Add(new() { Title = "After stop" });
            await controller.DisposeAsync();
            dispatcher.RunAll();
            Assert.DoesNotContain("PlaybackChoices", notifications);
            Assert.Empty(choices.GetAudioTracks());
        }
        finally { await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll(); }
    }

    [AvaloniaFact]
    public async Task Queued_media_open_hides_previous_choices_before_the_engine_dispatches()
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        using ManualResetEventSlim release = new();
        try
        {
            Assert.Null(player.Open(media).Error);
            dispatcher.RunAll();
            IPlaybackController choices = controller;
            Assert.NotEmpty(choices.GetVideoTracks());
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task barrier = controller.Recognition.MutateAsync(() =>
            {
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            controller.Open(media);
            Assert.Empty(choices.GetAudioTracks());
            Assert.Empty(choices.GetVideoTracks());
            Assert.Empty(choices.GetChapters());
            release.Set();
            await barrier;
            await controller.Recognition.MutateAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(20));
            dispatcher.RunAll();
            Assert.NotEmpty(choices.GetVideoTracks());
        }
        finally { release.Set(); await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_stream_error_is_discarded_after_a_later_successful_switch_on_the_same_input(bool native)
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            dispatcher.RunAll();
            IPlaybackController choices = controller;
            PlaybackStreamTrack original = Assert.Single(choices.GetAudioTracks(), track => track.IsSelected);
            AudioStream originalNative = player.decoder.AudioStream;
            PlaylistItem.AddExternalStream(new ExternalAudioStream
            {
                Url = Path.Combine(state.Path, "missing-delayed-audio.wav"), Codec = "broken-choice"
            }, player.Playlist.Selected, "test");
            PlaybackStreamTrack broken = Assert.Single(choices.GetAudioTracks(), track => track.Label.Contains("broken-choice", StringComparison.Ordinal));
            List<string> errors = [];
            List<string?> nativeErrors = [];
            controller.ErrorOccurred += errors.Add;
            player.OpenExternalAudioStreamCompleted += (_, e) => nativeErrors.Add(e.Error);
            await choices.SelectAudioTrackAsync(broken);
            Assert.Single(nativeErrors);
            Assert.NotNull(nativeErrors[0]);
            Assert.Empty(errors); // The deliberately queued dispatcher has not delivered the old error.
            if (native) Assert.Null(player.Open(originalNative, defaultAudio: false).Error);
            else await choices.SelectAudioTrackAsync(original);
            Assert.Same(originalNative, player.decoder.AudioStream);
            dispatcher.RunAll();
            Assert.Empty(errors);
        }
        finally { await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll(); }
    }
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Stalled_external_stream_open_settles_on_media_replacement_or_shutdown(bool video, bool shutdown)
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        StalledMediaServer server = new();
        try
        {
            Assert.Null(player.Open(media).Error);
            dispatcher.RunAll();
            IPlaybackController choices = controller;
            if (video)
                PlaylistItem.AddExternalStream(new ExternalVideoStream { Url = server.Url, Codec = "stalled-loopback" }, player.Playlist.Selected, "test");
            else
                PlaylistItem.AddExternalStream(new ExternalAudioStream { Url = server.Url, Codec = "stalled-loopback" }, player.Playlist.Selected, "test");
            List<string> errors = [];
            controller.ErrorOccurred += errors.Add;
            PlaybackStreamTrack track = Assert.Single(video ? choices.GetVideoTracks() : choices.GetAudioTracks(),
                candidate => candidate.Label.Contains("stalled-loopback", StringComparison.Ordinal));
            Task selection = video ? choices.SelectVideoTrackAsync(track) : choices.SelectAudioTrackAsync(track);
            await server.Request.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(selection.IsCompleted);
            if (shutdown)
                await controller.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
            else
            {
                controller.Open(media);
                await controller.Recognition.MutateAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(4));
            }
            await selection.WaitAsync(TimeSpan.FromSeconds(4));
            dispatcher.RunAll();
            Assert.Empty(errors);
            if (!shutdown)
            {
                Assert.True(player.CanPlay);
                Assert.Equal(media, controller.Url);
                Assert.False(player.decoder.Interrupt);
                Assert.Single(choices.GetAudioTracks(), candidate => candidate.IsSelected);
            }
        }
        finally
        {
            // Release the local socket even on intentional RED so the native call cannot delay test cleanup.
            await server.DisposeAsync();
            await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Canceling_a_queued_replacement_releases_the_old_stream_interrupt_and_allows_recovery(bool video, bool newerWriter)
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        StalledMediaServer server = new();
        using ManualResetEventSlim releaseCompletion = new();
        TaskCompletionSource completionEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int completions = 0;
        void HoldFirstCompletion(object? sender, object? args)
        {
            if (Interlocked.Increment(ref completions) != 1) return;
            completionEntered.TrySetResult();
            Assert.True(releaseCompletion.Wait(TimeSpan.FromSeconds(15)));
        }
        try
        {
            Assert.Null(player.Open(media).Error);
            dispatcher.RunAll();
            IPlaybackController choices = controller;
            if (video)
            {
                PlaylistItem.AddExternalStream(new ExternalVideoStream { Url = server.Url, Codec = "stalled-loopback" }, player.Playlist.Selected, "test");
                player.OpenExternalVideoStreamCompleted += HoldFirstCompletion;
            }
            else
            {
                PlaylistItem.AddExternalStream(new ExternalAudioStream { Url = server.Url, Codec = "stalled-loopback" }, player.Playlist.Selected, "test");
                player.OpenExternalAudioStreamCompleted += HoldFirstCompletion;
            }
            List<string> errors = [];
            controller.ErrorOccurred += errors.Add;
            PlaybackStreamTrack track = Assert.Single(video ? choices.GetVideoTracks() : choices.GetAudioTracks(),
                candidate => candidate.Label.Contains("stalled-loopback", StringComparison.Ordinal));
            Task selection = video ? choices.SelectVideoTrackAsync(track) : choices.SelectAudioTrackAsync(track);
            await server.Request.Task.WaitAsync(TimeSpan.FromSeconds(10));
            controller.Open(media);
            // The old native operation still holds the atomic engine gate and serialized queue here.
            // Canceling now guarantees that the queued replacement never reaches Initialize.
            await completionEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.True(controller.CancelOpen());
            // A native queued-open worker uses this same setter outside lockActions. Even a second
            // true write owns its signal and must not be cleared by retirement of our older lease.
            if (newerWriter) player.decoder.Interrupt = true;
            releaseCompletion.Set();
            await selection.WaitAsync(TimeSpan.FromSeconds(4));
            await controller.Recognition.MutateAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(4));
            dispatcher.RunAll();
            Assert.Equal(newerWriter, player.decoder.Interrupt);
            Assert.Empty(errors);
            if (newerWriter) return;
            PlaylistItem.AddExternalStream(new ExternalVideoStream { Url = media, Codec = "recovered-local" }, player.Playlist.Selected, "test");
            PlaybackStreamTrack recovered = Assert.Single(choices.GetVideoTracks(), candidate => candidate.Label.Contains("recovered-local", StringComparison.Ordinal));
            await choices.SelectVideoTrackAsync(recovered).WaitAsync(TimeSpan.FromSeconds(10));
            dispatcher.RunAll();
            Assert.Empty(errors);
            Assert.True(player.Video.IsOpened);
            Assert.Equal(media, player.decoder.VideoStream.Demuxer.Url);
            Assert.Single(choices.GetVideoTracks(), candidate => candidate.IsSelected && candidate.Label.Contains("recovered-local", StringComparison.Ordinal));
        }
        finally
        {
            releaseCompletion.Set();
            player.OpenExternalAudioStreamCompleted -= HoldFirstCompletion;
            player.OpenExternalVideoStreamCompleted -= HoldFirstCompletion;
            await server.DisposeAsync();
            await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll();
        }
    }
    sealed class StalledMediaServer : IAsyncDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource shutdown = new();
        readonly Task server;
        bool disposed;
        public string Url { get; }
        public TaskCompletionSource Request { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StalledMediaServer()
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/stalled.mp4";
            server = ServeAsync();
        }
        async Task ServeAsync()
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(shutdown.Token);
            await using NetworkStream stream = client.GetStream();
            byte[] request = new byte[4096];
            Assert.True(await stream.ReadAsync(request, shutdown.Token) > 0);
            Request.TrySetResult();
            await Task.Delay(Timeout.Infinite, shutdown.Token);
        }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            shutdown.Cancel(); listener.Stop();
            try { await server; } catch (OperationCanceledException) { }
            shutdown.Dispose();
        }
    }
    static (Player Player, FlyleafPlaybackController Controller, QueuedDispatcher Dispatcher, string Media) Create(TempDir state)
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(ffmpeg), "Requires configured native FFmpeg shared libraries.");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(media), "Requires configured canonical single-stream media fixture.");
        Assert.True(File.Exists(media));
        QueuedDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
        if (!Engine.IsLoaded) EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg!, new(state.Path, state.Path)));
        Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
        config.Player.AutoPlay = false;
        config.Subtitles.SearchLocal = false;
        config.Subtitles.LanguageAutoDetect = false;
        Player player = new(config);
        return (player, new FlyleafPlaybackController(player, dispatcher, bridge), dispatcher, media!);
    }

    sealed class QueuedDispatcher : IUIDispatcher
    {
        readonly ConcurrentQueue<Action> pending = new();
        public bool CheckAccess() => false;
        public void Post(Action action) => pending.Enqueue(action);
        public void Invoke(Action action) => action();
        public void RunAll() { while (pending.TryDequeue(out Action? action)) action(); }
    }
}
