using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;
using LLPlayer.Avalonia.Services;
using DecoderStatus = FlyleafLib.MediaFramework.Status;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class VideoSwitchBufferingRaceTests
{
    [AvaloniaFact]
    public async Task Stale_audio_restart_entry_cannot_bypass_an_active_buffer_before_external_audio_playback()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player;
        FrameSurface surface = new();
        player.Renderer.Surface = surface;
        player.Renderer.SetControlSize(640, 360);
        Assert.Null(player.Open(media.Media).Error);
        await session.Controller.SelectAudioTrackAsync(session.Controller.GetAudioTracks()
            .Single(track => track.Label.Contains("Mono 48000", StringComparison.Ordinal)));
        await SeekAndWait(player, 2500);
        using PcmCounter pcm = new(player);
        IOpen originalPlugin = player.decoder.OpenedPlugin;
        using BufferCompletionBarrier barrier = new(originalPlugin, TestContext.Current.CancellationToken);
        try
        {
            player.Play();
            await WaitUntil(() => pcm.Nonzero48000 > 0, () => Describe(player, pcm));
            Assert.True(ReadAudioConsumerRunning(player));
            ReplaceOpenedPlugin(player, barrier);
            player.SeekAccurate(2500);
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.False(ReadBufferRequest(player), "The active pass consumed its pending request before buffering.");
            Assert.False(ReadAudioConsumerRunning(player));
            long beforeRestart = pcm.Nonzero48000;
            MethodInfo? restart = typeof(Player).GetMethod("StartScreamerVASDAudio", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(restart);

            // Model the stale decision cached by an external-audio caller before this buffer stopped its
            // consumer. This tests the actual entry guard, not the full mid-Open interleaving of that caller.
            restart.Invoke(player, null);

            Assert.False(ReadAudioConsumerRunning(player), "A stale audio restart must not bypass the active buffer pass.");
            Assert.Equal(beforeRestart, pcm.Nonzero48000);
            barrier.Release();
            await WaitUntil(() => pcm.Nonzero48000 >= beforeRestart + 2, () => Describe(player, pcm));
            ExternalAudioStream external = new()
            {
                Url = media.ExternalAudio, SampleRate = 32000, ChannelLayout = "mono", Language = Language.English,
                Codec = "buffer-guard-external-audio", Protocol = "file"
            };
            PlaylistItem.AddExternalStream(external, player.Playlist.Selected, "BufferCompletionRace");
            await session.Controller.SelectAudioTrackAsync(session.Controller.GetAudioTracks()
                .Single(track => track.Label.Contains("buffer-guard-external-audio", StringComparison.Ordinal)));
            Assert.Same(external, player.decoder.AudioStream.ExternalStream);
            Assert.Equal(32000, player.decoder.AudioStream.SampleRate);
            await WaitUntil(() => pcm.Nonzero32000 > 0, () => Describe(player, pcm) + $"; nonzero32000={pcm.Nonzero32000}");
            Assert.True(player.IsPlaying);
        }
        finally
        {
            barrier.Release();
            ReplaceOpenedPlugin(player, originalPlugin);
            player.Pause();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Embedded_video_switch_during_old_buffer_completion_restarts_retained_audio(bool overlapBufferCompletion)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player;
        FrameSurface surface = new();
        player.Renderer.Surface = surface;
        player.Renderer.SetControlSize(640, 360);
        Assert.Null(player.Open(media.Media).Error);
        await session.Controller.SelectAudioTrackAsync(session.Controller.GetAudioTracks()
            .Single(track => track.Label.Contains("Mono 48000", StringComparison.Ordinal)));
        Assert.Null(player.Open(player.VideoDemuxer.VideoStreams.Single(stream => stream.Width == 320), defaultAudio: false).Error);
        await LoadExternalSubtitles(player, session.Controller, media);
        await SeekAndWait(player, 2500);
        Assert.Equal(320u, player.decoder.VideoStream.Width);
        Assert.Equal(48000, player.decoder.AudioStream.SampleRate);
        long mediaGeneration = player.MediaGeneration;
        var choice = session.Controller.GetVideoTracks().Single(track => track.Label.Contains("Blue 640", StringComparison.Ordinal));
        Assert.False(choice.IsSelected);
        IOpen originalPlugin = player.decoder.OpenedPlugin;
        using BufferCompletionBarrier barrier = new(originalPlugin, TestContext.Current.CancellationToken);
        TaskCompletionSource<BufferSnapshot> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Buffered(object? sender, BufferingCompletedArgs args) => completed.TrySetResult(new(
            args.Error, player.decoder.AudioDecoder.Status, player.decoder.AudioDecoder.IsRunning, player.decoder.AudioDecoder.Frames.Count,
            ReadBufferRequest(player), player.CurTime, ReadPendingSeeks(player), barrier.Callbacks, barrier.BufferCallbacks));
        player.BufferingCompleted += Buffered;
        using PcmCounter pcm = new(player);
        Task? selection = null;
        try
        {
            if (overlapBufferCompletion) ReplaceOpenedPlugin(player, barrier);
            player.Play();
            if (overlapBufferCompletion)
            {
                // This is the existing plugin callback at the very end of BufferVASD. Its caller has not yet
                // consumed requiresBuffering. No production hook or scheduler delay manufactures the ordering.
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
                Assert.False(completed.Task.IsCompleted);
            }
            else
            {
                BufferSnapshot initial = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
                Assert.Null(initial.Error);
                Assert.True(initial.AudioRunning || initial.AudioFrames > 0,
                    $"Initial buffering completed without retained audio ready: {initial}");
                await WaitUntil(() => pcm.Nonzero48000 > 0, () => Describe(player, pcm));
            }

            selection = session.Controller.SelectVideoTrackAsync(choice);
            await selection.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.True(player.IsPlaying);
            Assert.Equal(mediaGeneration, player.MediaGeneration);
            Assert.Equal(640u, player.decoder.VideoStream.Width);
            Assert.Equal(48000, player.decoder.AudioStream.SampleRate);
            long beforeRelease = pcm.Nonzero48000;
            if (overlapBufferCompletion)
            {
                Assert.True(ReadBufferRequest(player), "The completed embedded-video switch must publish its new buffer request.");
                // Pause leaves an active decoder Paused; when it already reached Ended, Flush resets it to
                // Stopped instead. Both are inactive and need the newly published buffering request.
                Assert.False(player.decoder.AudioDecoder.IsRunning);
                Assert.Empty(player.decoder.AudioDecoder.Frames);
                TestContext.Current.TestOutputHelper?.WriteLine($"Before releasing old buffer: pendingSeeks={ReadPendingSeeks(player)}; pluginCallbacks={barrier.Callbacks}; bufferCallbacks={barrier.BufferCallbacks}; {Describe(player, pcm)}");
                barrier.Release();
                BufferSnapshot firstCompletion = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
                TestContext.Current.TestOutputHelper?.WriteLine($"First public buffer completion: {firstCompletion}");
                Assert.Null(firstCompletion.Error);
                // A success notification from the obsolete buffer must not conceal the newer request while
                // retained audio is inactive and empty. A decoder that already finished is valid only while
                // decoded audio remains queued. Capture on the playback thread, not later on the UI thread.
                Assert.True(firstCompletion.AudioRunning || firstCompletion.AudioFrames > 0,
                    $"Buffering completed without retained audio ready: {firstCompletion}; {Describe(player, pcm)}");
            }
            await WaitUntil(() => pcm.Nonzero48000 >= beforeRelease + 2 && surface.IsBlue,
                () => Describe(player, pcm));
            Assert.Equal(48000, player.Audio.SampleRate);
            Assert.True(player.IsPlaying);
        }
        finally
        {
            barrier.Release();
            try
            {
                if (selection != null)
                    await selection.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            }
            finally
            {
                ReplaceOpenedPlugin(player, originalPlugin);
                player.BufferingCompleted -= Buffered;
                player.Pause();
            }
        }
    }

    static string Describe(Player player, PcmCounter pcm) =>
        $"status={player.Status}; time={TimeSpan.FromTicks(player.CurTime)}; duration={TimeSpan.FromTicks(player.Duration)}; " +
        $"audio={player.decoder.AudioDecoder.Status}; audioRunning={player.decoder.AudioDecoder.IsRunning}; audioFrames={player.decoder.AudioDecoder.Frames.Count}; " +
        $"video={player.decoder.VideoDecoder.Status}; bufferRequested={ReadBufferRequest(player)}; " +
        $"rate={player.Audio.SampleRate}; nonzero48000={pcm.Nonzero48000}";

    static bool ReadBufferRequest(Player player)
    {
        FieldInfo? field = typeof(Player).GetField("requiresBuffering", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<bool>(field.GetValue(player));
    }

    static bool ReadAudioConsumerRunning(Player player)
    {
        FieldInfo? field = typeof(Player).GetField("isScreamerVASDAudio", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<bool>(field.GetValue(player));
    }

    static int ReadPendingSeeks(Player player)
    {
        FieldInfo? field = typeof(Player).GetField("seeks", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        object? pending = field.GetValue(player);
        Assert.NotNull(pending);
        PropertyInfo? count = pending.GetType().GetProperty("Count");
        Assert.NotNull(count);
        return Assert.IsType<int>(count.GetValue(pending));
    }

    static void ReplaceOpenedPlugin(Player player, IOpen plugin)
    {
        MethodInfo? setter = typeof(PluginHandler).GetProperty(nameof(PluginHandler.OpenedPlugin))?.GetSetMethod(nonPublic: true);
        Assert.NotNull(setter);
        setter.Invoke(player.decoder, [plugin]);
    }

    static async Task LoadExternalSubtitles(Player player, FlyleafPlaybackController controller, AvStreamTestMedia media)
    {
        for (int slot = 0; slot < 2; slot++)
        {
            string path = slot == 0 ? media.Primary : media.Secondary;
            ExternalSubtitlesStream source = new() { Url = path, Title = Path.GetFileName(path), Language = Language.English, Downloaded = true };
            PlaylistItem.AddExternalStream(source, player.Playlist.Selected, "BufferCompletionRace");
            controller.SelectSubtitleTrack(slot, controller.GetSubtitleTracks().Single(track => ReferenceEquals(track.Source, source)));
            await controller.Recognition.MutateAsync(() => { });
            int current = slot;
            await WaitUntil(() => !player.SubtitlesManager[current].IsLoading
                && player.SubtitlesManager[current].SnapshotSubs().Count == 1, () => $"Subtitle slot {current} did not load.");
        }
    }

    static async Task SeekAndWait(Player player, int milliseconds)
    {
        TaskCompletionSource<int> complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, int requested) => complete.TrySetResult(requested);
        player.SeekCompleted += Completed;
        try
        {
            player.SeekAccurate(milliseconds);
            Assert.Equal(milliseconds, await complete.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
        }
        finally { player.SeekCompleted -= Completed; }
    }

    static async Task WaitUntil(Func<bool> predicate, Func<string> diagnostics)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate())
        {
            Dispatcher.UIThread.RunJobs();
            if (DateTime.UtcNow >= deadline) throw new TimeoutException(diagnostics());
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    sealed record BufferSnapshot(string? Error, DecoderStatus AudioStatus, bool AudioRunning, int AudioFrames, bool BufferRequested, long Position,
        int PendingSeeks, int PluginCallbacks, int BufferCallbacks);

    sealed class BufferCompletionBarrier(IOpen inner, CancellationToken cancellationToken) : IOpen
    {
        readonly ManualResetEventSlim release = new();
        int entered;
        int callbacks;
        int bufferCallbacks;
        public int Callbacks => Volatile.Read(ref callbacks);
        public int BufferCallbacks => Volatile.Read(ref bufferCallbacks);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name => inner.Name;
        public Version Version => inner.Version;
        public PluginHandler Handler => inner.Handler;
        public int Priority => inner.Priority;
        public bool CanOpen() => inner.CanOpen();
        public OpenResults Open() => inner.Open();
        public OpenResults OpenItem() => inner.OpenItem();
        public void OnLoaded() => inner.OnLoaded();
        public void OnInitializing() => inner.OnInitializing();
        public void OnInitialized() => inner.OnInitialized();
        public void OnInitializingSwitch() => inner.OnInitializingSwitch();
        public void OnInitializedSwitch() => inner.OnInitializedSwitch();
        public void OnBuffering() => inner.OnBuffering();
        public void OnOpenExternalAudio() => inner.OnOpenExternalAudio();
        public void OnOpenExternalVideo() => inner.OnOpenExternalVideo();
        public void OnOpenExternalSubtitles() => inner.OnOpenExternalSubtitles();
        public void OnBufferingCompleted()
        {
            Interlocked.Increment(ref callbacks);
            inner.OnBufferingCompleted();
            // Paused seeking invokes the same plugin callback from its task finally after SeekCompleted.
            // Block only BufferVASD's end-of-pass callback, not that unrelated late completion.
            if (!new StackTrace(fNeedFileInfo: false).GetFrames().Any(frame =>
                    frame.GetMethod() is { Name: "BufferVASD", DeclaringType: var type } && type == typeof(Player)))
                return;
            Interlocked.Increment(ref bufferCallbacks);
            if (Interlocked.Exchange(ref entered, 1) != 0) return;
            Entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15), cancellationToken))
                throw new TimeoutException("Buffer completion investigation barrier was not released.");
        }
        public void Release() => release.Set();
        // The player owns the real plugin. Only the test-owned wait handle belongs to this wrapper.
        public void Dispose() => release.Dispose();
    }

    sealed class PcmCounter : IDisposable
    {
        readonly Player player;
        long nonzero48000;
        long nonzero32000;
        public long Nonzero48000 => Interlocked.Read(ref nonzero48000);
        public long Nonzero32000 => Interlocked.Read(ref nonzero32000);
        public PcmCounter(Player player) { this.player = player; player.Audio.SamplesAdded += Added; }
        void Added(object? sender, AudioFrame frame)
        {
            int rate = player.Audio.SampleRate;
            if (rate is not (32000 or 48000) || frame.dataLen <= 0) return;
            byte[] bytes = new byte[Math.Min(frame.dataLen, 4096)];
            Marshal.Copy(frame.dataPtr, bytes, 0, bytes.Length);
            if (bytes.Any(value => value != 0))
            {
                if (rate == 48000) Interlocked.Increment(ref nonzero48000);
                else Interlocked.Increment(ref nonzero32000);
            }
        }
        public void Dispose() => player.Audio.SamplesAdded -= Added;
    }

    sealed class FrameSurface : IVideoSurface
    {
        int blue;
        public bool IsBlue => Volatile.Read(ref blue) != 0;
        public void PresentFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride)
        {
            int offset = height / 2 * stride + width / 2 * 4;
            Volatile.Write(ref blue, width == 640 && bgra[offset] > 180 && bgra[offset + 1] < 30 && bgra[offset + 2] < 30 ? 1 : 0);
        }
        public void ClearFrame() => Volatile.Write(ref blue, 0);
    }

    sealed class NativeSession : IAsyncDisposable
    {
        readonly IAudioBackend previousBackend;
        public Player Player { get; }
        public FlyleafPlaybackController Controller { get; }
        public NativeSession(TempDir state)
        {
            AvaloniaUIDispatcher dispatcher = new();
            CollectionSyncBridge bridge = new(dispatcher);
            EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
            if (!Engine.IsLoaded)
                EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR")!,
                    new AppPaths(Path.Combine(state.Path, "config"), Path.Combine(state.Path, "state"))));
            previousBackend = AudioEngine.Backend;
            AudioEngine.Backend = NullAudioBackend.Instance;
            Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
            config.Player.AutoPlay = false;
            config.Subtitles.SearchLocal = false;
            config.Subtitles.LanguageAutoDetect = false;
            Player = new(config);
            Controller = new(Player, dispatcher, bridge);
        }
        public async ValueTask DisposeAsync()
        {
            try { await Controller.DisposeAsync(); Player.Dispose(); }
            finally { AudioEngine.Backend = previousBackend; }
        }
    }
}
