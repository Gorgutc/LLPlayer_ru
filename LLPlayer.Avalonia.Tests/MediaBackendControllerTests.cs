using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaDevice;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class MediaBackendControllerTests
{
    [AvaloniaFact]
    public async Task Software_readback_retains_the_saved_hardware_choice_and_presentation()
    {
        using TempDir state = new();
        await using Session session = new(state);
        PlaybackSettings current = session.Controller.GetPlaybackSettings();
        Assert.False(current.VideoHardwareAccelerationEnabled);
        Assert.Equal(PortableVideoDecodeMode.VAAPI, current.VideoHardwareDecodeMode);
        Assert.Equal("/dev/dri/renderD129", current.VideoHardwareDevice);
        Assert.Equal(PortableVideoPresentationMode.OpenGL, current.VideoPresentationMode);
        Assert.True(session.Player.Config.Video.VideoAcceleration);
        Assert.Equal(PortableVideoDecodeMode.Software, session.Player.Config.Video.PortableDecodeMode);
    }

    [AvaloniaFact]
    public async Task Queued_toggles_read_fresh_state_and_failed_save_changes_nothing()
    {
        using TempDir state = new();
        await using Session session = new(state);
        Func<Task> toggle = Required<Func<Task>>(session.Controller, "ToggleVideoAccelerationAsync");
        using ManualResetEventSlim entered = new(), release = new();
        Task blocker = session.Controller.Recognition.MutateAsync(() => { entered.Set(); release.Wait(); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task first, second;
        try { first = toggle(); second = toggle(); }
        finally { release.Set(); }
        await Task.WhenAll(blocker, first, second);
        PlaybackSettings saved = session.Store.Load();
        Assert.False(saved.VideoHardwareAccelerationEnabled);
        Assert.Equal(PortableVideoDecodeMode.VAAPI, saved.VideoHardwareDecodeMode);
        Assert.Equal("/dev/dri/renderD129", saved.VideoHardwareDevice);
        Assert.Equal(PortableVideoDecodeMode.Software, session.Player.Config.Video.PortableDecodeMode);
        Assert.True(session.Player.Config.Video.VideoAcceleration);

        // A directory at the destination forces the real store's atomic write to fail.
        string invalidDestination = System.IO.Path.Combine(state.Path, "directory-as-settings");
        Directory.CreateDirectory(invalidDestination);
        session.Controller.ConfigurePlaybackSettings(new(invalidDestination), saved);
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(toggle);
        Assert.True(failure is IOException or UnauthorizedAccessException, failure.ToString());
        Assert.False(session.Controller.GetPlaybackSettings().VideoHardwareAccelerationEnabled);
        Assert.Equal(PortableVideoDecodeMode.Software, session.Player.Config.Video.PortableDecodeMode);
        Assert.Equal("/dev/dri/renderD129", session.Controller.GetPlaybackSettings().VideoHardwareDevice);
        Assert.True(session.Player.Config.Video.VideoAcceleration);
    }

    [AvaloniaFact]
    public async Task Default_and_rebound_commands_use_the_same_hardware_owner()
    {
        using TempDir state = new();
        // No production default chord is added: this pre-existing binding models a loaded engine config.
        await using Session session = new(state, hardwareBinding: true);
        Assert.Equal(session.OriginalBindings, session.Controller.GetEngineKeyBindings());
        KeyBinding original = Assert.Single(session.Player.Config.Player.KeyBindings.Keys,
            binding => binding.Action == KeyBindingAction.ToggleVideoAcceleration);
        original.ActionInternal();
        await session.Controller.Recognition.MutateAsync(() => { });
        Assert.True(session.Store.Load().VideoHardwareAccelerationEnabled);
        Assert.True(session.Player.Config.Video.VideoAcceleration);
        Assert.Equal(PortableVideoDecodeMode.VAAPI, session.Player.Config.Video.PortableDecodeMode);

        session.Controller.ApplyEngineKeyBindings(session.OriginalBindings);
        KeyBinding rebound = Assert.Single(session.Player.Config.Player.KeyBindings.Keys,
            binding => binding.Action == KeyBindingAction.ToggleVideoAcceleration);
        rebound.ActionInternal();
        await session.Controller.Recognition.MutateAsync(() => { });
        Assert.False(session.Store.Load().VideoHardwareAccelerationEnabled);
        Assert.True(session.Player.Config.Video.VideoAcceleration);
        Assert.Equal(PortableVideoDecodeMode.Software, session.Player.Config.Video.PortableDecodeMode);
        Assert.Equal(session.OriginalBindings, session.Controller.GetEngineKeyBindings());

        var resolve = Required<Func<KeyBindingAction, Action?>>(session.Controller, "ResolveEngineAction");
        Assert.NotNull(resolve(KeyBindingAction.ToggleVideoAcceleration));
        resolve(KeyBindingAction.ToggleVideoAcceleration)!();
        await session.Controller.Recognition.MutateAsync(() => { });
        Assert.True(session.Store.Load().VideoHardwareAccelerationEnabled);
    }

    [AvaloniaFact]
    public async Task Hardware_toggle_publishes_a_pending_request_without_reopening_media()
    {
        using TempDir state = new();
        await using Session session = new(state);
        int opens = 0;
        session.Player.Opening += (_, _) => opens++;
        session.Player.Config.Video.VideoAcceleration = false;
        long generation = session.Player.MediaGeneration;
        await session.Controller.ToggleVideoAccelerationAsync();
        PortableVideoDecodeStatus requested = session.Controller.GetVideoDecodeStatus();
        Assert.Equal(PortableVideoDecodeMode.VAAPI, requested.RequestedMode);
        Assert.Equal("/dev/dri/renderD129", requested.RequestedDevice);
        Assert.True(requested.IsPending);
        Assert.False(requested.HardwareActive);
        Assert.False(requested.IsInitialized);
        Assert.True(session.Player.Config.Video.VideoAcceleration);
        Assert.Equal(generation, session.Player.MediaGeneration);
        Assert.Equal(0, opens);
    }

    [AvaloniaFact]
    public async Task Saved_presentation_is_posted_to_the_ui_without_reopening_media()
    {
        using TempDir state = new();
        await using Session session = new(state);
        List<PortableVideoPresentationMode> applied = [];
        int opens = 0;
        session.Player.Opening += (_, _) => opens++;
        session.Controller.ConfigureVideoPresentation(
            () => new(PortableVideoPresentationMode.Software, PortableVideoPresentationMode.Software, false, false, null),
            mode => { Assert.True(Dispatcher.UIThread.CheckAccess()); applied.Add(mode); });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([PortableVideoPresentationMode.OpenGL], applied);
        PlaybackSettings baseline = session.Controller.GetPlaybackSettings(), edited = baseline.Clone();
        edited.VideoPresentationMode = PortableVideoPresentationMode.Software;
        await session.Controller.SavePlaybackSettingsAsync(edited, baseline, session.Controller.GetPlaybackRuntimeSettings(), false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PortableVideoPresentationMode.Software, applied[^1]);
        Assert.Equal(PortableVideoPresentationMode.Software, session.Store.Load().VideoPresentationMode);
        Assert.Equal(0, opens);
    }

    [AvaloniaFact]
    public async Task Capture_open_rejects_stale_or_forged_full_device_and_format_tokens_without_opening()
    {
        using TempDir state = new();
        await using Session session = new(state);
        var open = Required<Func<CaptureDeviceInfo, long, CaptureVideoFormatInfo?, CancellationToken, Task>>(
            session.Controller, "OpenCaptureDeviceAsync");
        FieldInfo field = typeof(VideoEngine).GetField("captureSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = (CaptureDeviceSnapshot)field.GetValue(Engine.Video)!;
        var current = new CaptureDeviceInfo("native-camera", "Camera", "video4linux2", "/dev/video-test", CaptureDeviceKind.Video);
        int opens = 0;
        FieldInfo epoch = typeof(FlyleafPlaybackController).GetField("mediaOpenEpoch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? previousEpoch = epoch.GetValue(session.Controller);
        session.Player.Opening += (_, _) => opens++;
        field.SetValue(Engine.Video, new CaptureDeviceSnapshot(42, CaptureAvailability.Available, new[] { current }, null));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => open(current, 41, null, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => open(current with { Input = "/dev/another-camera" }, 42, null, default));
            var forged = new CaptureVideoFormatInfo("invented-format", current.Id, 1920, 1080, 30, 1, "nv12", null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => open(current, 42, forged, default));
            Assert.Equal(0, opens);
            Assert.False(session.Player.CanPlay);
            Assert.Equal(previousEpoch, epoch.GetValue(session.Controller));
        }
        finally { field.SetValue(Engine.Video, previous); }
    }

    [AvaloniaFact]
    public async Task Capture_choice_that_becomes_stale_in_the_queue_cannot_advance_the_media_epoch()
    {
        using TempDir state = new();
        await using Session session = new(state);
        FieldInfo field = typeof(VideoEngine).GetField("captureSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo epoch = typeof(FlyleafPlaybackController).GetField("mediaOpenEpoch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? previousEpoch = epoch.GetValue(session.Controller);
        var previous = (CaptureDeviceSnapshot)field.GetValue(Engine.Video)!;
        var device = new CaptureDeviceInfo("native-camera", "Camera", "video4linux2", "/dev/video-test", CaptureDeviceKind.Video);
        field.SetValue(Engine.Video, new CaptureDeviceSnapshot(42, CaptureAvailability.Available, new[] { device }, null));
        using ManualResetEventSlim entered = new(), release = new();
        Task blocker = session.Controller.Recognition.MutateAsync(() => { entered.Set(); release.Wait(); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        int opens = 0;
        session.Player.Opening += (_, _) => opens++;
        try
        {
            Task open = session.Controller.OpenCaptureDeviceAsync(device, 42);
            field.SetValue(Engine.Video, new CaptureDeviceSnapshot(43, CaptureAvailability.Available, new[] { device }, null));
            release.Set();
            await blocker;
            await Assert.ThrowsAsync<InvalidOperationException>(() => open);
            Assert.Equal(previousEpoch, epoch.GetValue(session.Controller));
            Assert.Equal(0, opens);
        }
        finally { release.Set(); field.SetValue(Engine.Video, previous); }
    }

    static T Required<T>(FlyleafPlaybackController controller, string name) where T : Delegate
    {
        MethodInfo? method = typeof(FlyleafPlaybackController).GetMethod(name);
        Assert.NotNull(method);
        return method.CreateDelegate<T>(controller);
    }

    sealed class Session : IAsyncDisposable
    {
        public Player Player { get; }
        public FlyleafPlaybackController Controller { get; }
        public PlaybackSettingsStore Store { get; }
        public IReadOnlyList<EngineKeyBindingInfo> OriginalBindings { get; }
        public Session(TempDir state, bool hardwareBinding = false)
        {
            string? native = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
            Assert.SkipWhen(string.IsNullOrWhiteSpace(native), "Offline controller regression requires native FFmpeg fixture configuration.");
            AvaloniaUIDispatcher dispatcher = new();
            CollectionSyncBridge bridge = new(dispatcher);
            EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
            if (!Engine.IsLoaded) EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(native!, new(state.Path, state.Path)));
            Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
            config.Player.AutoPlay = false;
            config.Subtitles.SearchLocal = false;
            config.Subtitles.LanguageAutoDetect = false;
            Player = new(config);
            if (hardwareBinding)
                Player.Config.Player.KeyBindings.Add(System.Windows.Input.Key.F12, KeyBindingAction.ToggleVideoAcceleration, ctrl: true);
            OriginalBindings = Player.Config.Player.KeyBindings.Keys.Select(binding => new EngineKeyBindingInfo(
                binding.Action, binding.Key, binding.Ctrl, binding.Alt, binding.Shift, binding.IsKeyUp, binding.IsEnabled)).ToArray();
            Controller = new(Player, dispatcher, bridge);
            Store = new(System.IO.Path.Combine(state.Path, "playback.json"));
            Controller.ConfigurePlaybackSettings(Store, new()
            {
                VideoHardwareDecodeMode = PortableVideoDecodeMode.VAAPI,
                VideoHardwareDevice = "/dev/dri/renderD129",
                VideoPresentationMode = PortableVideoPresentationMode.OpenGL
            });
        }
        public async ValueTask DisposeAsync() { await Controller.DisposeAsync(); Dispatcher.UIThread.RunJobs(); Player.Dispose(); }
    }
}
