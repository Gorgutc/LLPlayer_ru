using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FlyleafLib.MediaFramework.MediaDevice;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class SecondAppInputIntegrationTests
{
    [Fact]
    public void Command_line_terminator_preserves_option_shaped_media_as_one_argument()
    {
        AppOptions options = AppOptions.Parse(["--ffmpeg-dir", "native libs", "--", "--camera & a=b%:字幕"]);
        Assert.Equal("native libs", options.FFmpegDir);
        Assert.Equal("--camera & a=b%:字幕", options.MediaPath);
        Assert.Throws<AppOptionsException>(() => AppOptions.Parse(["--", "first", "second"]));
    }

    [AvaloniaFact]
    public void Control_wheel_zooms_at_cursor_without_changing_volume_even_when_volume_wheel_is_disabled()
    {
        using var fixture = new InputWindow(new AppPrefs { MouseWheelToVolumeUpDown = false });
        Point local = new(70, 45);
        fixture.Window.MouseWheel(fixture.Point(local), new Vector(0, 1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        var zoom = Assert.Single(fixture.Player.Zooms);
        Assert.True(zoom.In);
        Assert.Equal(local.X * fixture.Window.RenderScaling, zoom.X, 5);
        Assert.Equal(local.Y * fixture.Window.RenderScaling, zoom.Y, 5);
        Assert.Equal(60, fixture.Player.Volume);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shift_wheel_uses_existing_subtitle_position_or_size_owner(bool control)
    {
        using var fixture = new InputWindow();
        double size = fixture.Vm.SubtitleFontSize;
        double position = fixture.Vm.SubtitleBottomOffset;
        fixture.Window.MouseWheel(fixture.Point(), new Vector(0, 1), RawInputModifiers.Shift
            | (control ? RawInputModifiers.Control : RawInputModifiers.None));
        Dispatcher.UIThread.RunJobs();
        if (control) Assert.True(fixture.Vm.SubtitleFontSize > size);
        else Assert.True(fixture.Vm.SubtitleBottomOffset > position);
        Assert.Empty(fixture.Player.Zooms);
        Assert.Equal(60, fixture.Player.Volume);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Middle, RawInputModifiers.None, KeyBindingAction.SubsCurSeek)]
    [InlineData(MouseButton.Middle, RawInputModifiers.Control, KeyBindingAction.ResetZoom)]
    [InlineData(MouseButton.XButton1, RawInputModifiers.None, KeyBindingAction.SubsPrevSeekFallback)]
    [InlineData(MouseButton.XButton2, RawInputModifiers.None, KeyBindingAction.SubsNextSeekFallback)]
    public void Auxiliary_video_click_invokes_actual_engine_owner(MouseButton button, RawInputModifiers modifiers, KeyBindingAction action)
    {
        using var fixture = new InputWindow();
        Point point = fixture.Point();
        fixture.Window.MouseMove(point, modifiers);
        fixture.Window.MouseDown(point, button, modifiers);
        fixture.Window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([action], fixture.Player.Actions);
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Control_drag_pans_from_existing_offsets_by_relative_viewport_distance()
    {
        using var fixture = new InputWindow();
        fixture.Player.Pan = (.1, -.2);
        Point from = fixture.Point(new Point(60, 50));
        Vector delta = new(80, 30);
        fixture.Window.MouseMove(from, RawInputModifiers.Control);
        fixture.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
        fixture.Window.MouseMove(from + delta, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
        fixture.Window.MouseUp(from + delta, MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(.1 + delta.X / fixture.Window.VideoSurface.Bounds.Width, fixture.Player.Pan.X, 5);
        Assert.Equal(-.2 + delta.Y / fixture.Window.VideoSurface.Bounds.Height, fixture.Player.Pan.Y, 5);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Middle_video_click_does_not_seek_a_disabled_primary_subtitle_slot()
    {
        using var fixture = new InputWindow();
        fixture.Player.PrimarySlotEnabled = false;
        Click(fixture.Window, fixture.Point(), MouseButton.Middle);
        Assert.Empty(fixture.Player.Actions);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Service_menu_commands_reuse_the_live_view_model_and_engine_owners()
    {
        using var fixture = new InputWindow();
        foreach (var (name, command) in new (string, System.Windows.Input.ICommand)[]
        {
            ("CommandPaletteMenu", fixture.Vm.ShowCommandPaletteCommand),
            ("AboutMenu", fixture.Vm.ShowAboutCommand),
            ("DebugMenu", fixture.Vm.TogglePlayerDebugCommand),
            ("AudioCaptureMenu", fixture.Vm.ShowAudioCaptureDevicesCommand),
            ("VideoCaptureMenu", fixture.Vm.ShowVideoCaptureDevicesCommand),
            ("RecordMenu", fixture.Vm.ToggleRecordingCommand),
            ("SnapshotMenu", fixture.Vm.TakeSnapshotCommand),
            ("HardwareAccelerationMenu", fixture.Vm.ToggleVideoAccelerationCommand),
        })
        {
            MenuItem item = Assert.IsType<MenuItem>(fixture.Window.FindControl<MenuItem>(name));
            Assert.Same(command, item.Command);
        }
        fixture.Window.FindControl<MenuItem>("RecordMenu")!.Command!.Execute(null);
        fixture.Window.FindControl<MenuItem>("SnapshotMenu")!.Command!.Execute(null);
        fixture.Window.FindControl<MenuItem>("HardwareAccelerationMenu")!.Command!.Execute(null);
        Assert.Equal([KeyBindingAction.ToggleRecording, KeyBindingAction.TakeSnapshot, KeyBindingAction.ToggleVideoAcceleration], fixture.Player.Actions);
        Assert.Empty(fixture.Player.KeysDown);
    }

    [AvaloniaTheory]
    [InlineData("NewApplicationMenu", ApplicationLaunchKind.New)]
    [InlineData("CloneApplicationMenu", ApplicationLaunchKind.Clone)]
    [InlineData("RestartApplicationMenu", ApplicationLaunchKind.Restart)]
    public async Task Application_menu_routes_to_the_injected_process_owner(string name, ApplicationLaunchKind kind)
    {
        using var fixture = new InputWindow();
        List<ApplicationLaunchKind> calls = [];
        fixture.Vm.LaunchApplication = request => { calls.Add(request); return Task.FromResult(true); };
        MenuItem item = Assert.IsType<MenuItem>(fixture.Window.FindControl<MenuItem>(name));
        var command = Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(item.Command);
        await command.ExecuteAsync(null);
        Assert.Equal([kind], calls);
        Assert.True(fixture.Window.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left, RawInputModifiers.None, false)]
    [InlineData(MouseButton.Left, RawInputModifiers.Control, true)]
    [InlineData(MouseButton.Right, RawInputModifiers.None, true)]
    public void Context_menu_action_executes_once_and_keeps_open_for_right_or_control_left_click(
        MouseButton button, RawInputModifiers modifiers, bool keepOpen)
    {
        using var fixture = new InputWindow();
        Click(fixture.Window, fixture.Point(), MouseButton.Right);
        ContextMenu menu = fixture.Window.FindControl<Panel>("VideoHost")!.ContextMenu!;
        MenuItem subtitles = Assert.Single(menu.Items.OfType<MenuItem>(), row => row.Header?.ToString() == "Subtitles");
        ClickControl(subtitles);
        MenuItem visibility = Assert.Single(subtitles.Items.OfType<MenuItem>(), row => ReferenceEquals(row.Command, fixture.Vm.ToggleSubtitlesCommand));
        Assert.True(fixture.Vm.SubtitlesVisible);
        ClickControl(visibility, button, modifiers);
        Assert.False(fixture.Vm.SubtitlesVisible);
        Assert.Equal(keepOpen, menu.IsOpen);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public async Task Main_window_close_cancels_and_drains_busy_owned_capture_before_host_shutdown()
    {
        using var fixture = new InputWindow();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken runningToken = default;
        bool hostShutdown = false;
        var model = new CaptureDevicesViewModel(CaptureDeviceKind.Video, () => CaptureDeviceSnapshot.Initial,
            async token => { runningToken = token; await release.Task; },
            (_, _, _) => throw new InvalidOperationException("Formats are not queried by this test."),
            (_, _, _, _) => Task.CompletedTask);
        fixture.Window.ShowCaptureDevices(model);
        Task pending = model.RefreshCommand.ExecuteAsync(null);
        fixture.Window.ShutdownRequested = () => { hostShutdown = true; return Task.CompletedTask; };
        try
        {
            fixture.Window.Close();
            Assert.True(runningToken.IsCancellationRequested);
            Assert.False(hostShutdown);
            Assert.True(fixture.Window.IsVisible);
            release.SetResult();
            await pending;
            await fixture.Window.RequestApplicationExitAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(hostShutdown);
            Assert.False(fixture.Window.IsVisible);
        }
        finally { release.TrySetResult(); await pending; }
    }

    [AvaloniaFact]
    public async Task Explicit_exit_drains_owned_video_adapter_refresh_before_host_shutdown_and_closes_settings()
    {
        using var fixture = new InputWindow();
        TaskCompletionSource<IReadOnlyList<PortableVideoAdapterInfo>> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool hostShutdown = false;
        var model = new PlaybackSettingsViewModel(new(), new(), (_, _, _, _) => Task.CompletedTask,
            refreshVideoAdapters: () => release.Task);
        fixture.Window.ShowPlaybackSettings(model);
        PlaybackSettingsWindow settings = Assert.IsType<PlaybackSettingsWindow>(Assert.Single(fixture.Window.OwnedWindows));
        Task pending = model.RefreshVideoAdaptersCommand.ExecuteAsync(null);
        Assert.True(model.Busy);
        fixture.Window.ShutdownRequested = () => { hostShutdown = true; return Task.CompletedTask; };
        Task exit = fixture.Window.RequestApplicationExitAsync();
        try
        {
            Assert.False(hostShutdown);
            Assert.True(fixture.Window.IsVisible);
            Assert.True(settings.IsVisible);
            release.SetResult([]);
            await pending;
            await exit;
            Dispatcher.UIThread.RunJobs();
            Assert.True(hostShutdown);
            Assert.False(settings.IsVisible);
            Assert.False(fixture.Window.IsVisible);
        }
        finally
        {
            release.TrySetResult([]);
            await pending;
            await exit;
            if (settings.IsVisible) settings.Close();
            if (fixture.Window.IsVisible) fixture.Window.Close();
        }
    }

    static void Click(TopLevel window, Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseMove(point, modifiers);
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    static void ClickControl(Control control, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        TopLevel root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Click(root, point, button, modifiers);
    }

    sealed class GesturePlayer : FakePlaybackController, IPlaybackController
    {
        public List<(bool In, double X, double Y)> Zooms { get; } = [];
        public List<KeyBindingAction> Actions { get; } = [];
        public (double X, double Y) Pan { get; set; }
        public Action? ResolveEngineAction(KeyBindingAction action) => () => Actions.Add(action);
        public void ZoomVideoAt(bool zoomIn, double deviceX, double deviceY) => Zooms.Add((zoomIn, deviceX, deviceY));
        public (double X, double Y) GetVideoPan() => Pan;
        public void SetVideoPan(double x, double y) => Pan = (x, y);
    }

    sealed class InputWindow : IDisposable
    {
        public MainWindow Window { get; } = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        public GesturePlayer Player { get; } = new() { IsOpened = true, IsPlaying = true, Volume = 60, PrimarySlotEnabled = true };
        public MainWindowViewModel Vm { get; }
        public InputWindow(AppPrefs? prefs = null)
        {
            prefs ??= new();
            // MainWindowViewModel intentionally restores the persisted preference when it is constructed.
            prefs.Volume = Player.Volume;
            Vm = new(Player, prefs.Normalize(), _ => { }, new FakeTranslator(), Window);
            Window.Attach(Vm, null);
            Window.Show();
            Window.VideoSurface.PresentFrame(new byte[16 * 9 * 4], 16, 9, 16 * 4);
            Dispatcher.UIThread.RunJobs();
            Window.CaptureRenderedFrame()?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
        public Point Point(Point? local = null) => Window.VideoSurface.TranslatePoint(local ?? new Point(120, 100), Window)!.Value;
        public void Dispose() { Window.Close(); Vm.Dispose(); }
    }
}
