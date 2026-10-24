using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>
/// F-12 A-B repeat in the shared Windows/Linux Avalonia transport: the click cycle (Set A → Set B → Clear), the explicit
/// menu actions, the engine-mirrored bindables, the seek-bar markers and the real transport wiring. Expected messages
/// mirror the WPF engine OSD messages; the clear toast is worded "A-B cleared" (the engine OSD says "Cleared").
/// </summary>
public class AbLoopRepeatTests
{
    static long Ticks(double seconds) => TimeSpan.FromSeconds(seconds).Ticks;

    static (string Title, string? Message) LastToast(MainWindowViewModel vm)
        => (vm.Toasts[^1].Title, vm.Toasts[^1].Message);

    // ---- view model: click cycle and explicit actions -----------------------------------------------------------

    [Fact]
    public void Click_cycles_set_A_then_set_B_then_clear()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.Duration = Ticks(60);
        player.CurTime = Ticks(10);

        vm.ToggleAbLoop();
        vm.HasAbLoopA.Should().BeTrue();
        vm.HasAbLoopB.Should().BeFalse();
        vm.AbLoopActive.Should().BeTrue();
        vm.AbLoopEnabled.Should().BeFalse("one point is not a loop yet");
        vm.AbLoopStartSeconds.Should().BeApproximately(10, 1e-9);
        LastToast(vm).Should().Be(("A-B repeat", "A set"));

        player.CurTime = Ticks(25);
        vm.ToggleAbLoop();
        vm.AbLoopEnabled.Should().BeTrue("both points are set with A < B");
        vm.AbLoopEndSeconds.Should().BeApproximately(25, 1e-9);
        LastToast(vm).Should().Be(("A-B repeat", "B set"));

        vm.ToggleAbLoop();
        vm.AbLoopActive.Should().BeFalse();
        vm.AbLoopEnabled.Should().BeFalse();
        vm.HasAbLoopA.Should().BeFalse();
        vm.HasAbLoopB.Should().BeFalse();
        LastToast(vm).Should().Be(("A-B repeat", "A-B cleared"));
    }

    [Fact]
    public void Click_rejects_B_that_does_not_follow_A()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.Duration = Ticks(60);
        player.CurTime = Ticks(30);
        vm.ToggleAbLoop();                                  // A = 30 s

        player.CurTime = Ticks(10);                         // playhead is before A
        vm.ToggleAbLoop();                                  // engine rejects B <= A

        vm.HasAbLoopB.Should().BeFalse();
        vm.AbLoopEnabled.Should().BeFalse();
        LastToast(vm).Should().Be(("A-B repeat", "B must be after A"));
    }

    [Fact]
    public void Explicit_actions_set_both_points_and_clear()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.Duration = Ticks(60);
        player.CurTime = Ticks(5);

        vm.SetAbLoopStart();
        LastToast(vm).Should().Be(("A-B repeat", "A set"));

        player.CurTime = Ticks(20);
        vm.SetAbLoopEnd();
        vm.AbLoopEnabled.Should().BeTrue();
        LastToast(vm).Should().Be(("A-B repeat", "B set"));

        vm.ClearAbLoop();
        vm.AbLoopActive.Should().BeFalse();
        LastToast(vm).Should().Be(("A-B repeat", "A-B cleared"));

        player.AbLoopCalls.Should().Equal("setA", "setB", "clear");
    }

    [Fact]
    public void Explicit_B_without_A_is_kept_but_does_not_loop()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.Duration = Ticks(60);
        player.CurTime = Ticks(20);

        vm.SetAbLoopEnd();

        vm.HasAbLoopB.Should().BeTrue("the engine accepts B on its own, like the WPF menu item");
        vm.AbLoopEnabled.Should().BeFalse();
        LastToast(vm).Should().Be(("A-B repeat", "B set"));

        player.CurTime = Ticks(40);
        vm.SetAbLoopStart();                                // A after B drops the now-invalid B (engine rule)
        vm.HasAbLoopA.Should().BeTrue();
        vm.HasAbLoopB.Should().BeFalse();
        LastToast(vm).Should().Be(("A-B repeat", "A set"));
    }

    [Fact]
    public void Explicit_Set_B_at_an_earlier_position_keeps_the_old_B_and_reports_the_rejection()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.Duration = Ticks(60);
        player.CurTime = Ticks(10);
        vm.SetAbLoopStart();                                // A = 10 s
        player.CurTime = Ticks(20);
        vm.SetAbLoopEnd();                                  // B = 20 s
        vm.AbLoopEnabled.Should().BeTrue();

        player.CurTime = Ticks(5);                          // earlier than A: the engine must reject the new B
        vm.SetAbLoopEnd();

        vm.AbLoopEndSeconds.Should().BeApproximately(20, 1e-9, "the engine keeps the previously accepted B");
        vm.AbLoopEnabled.Should().BeTrue();
        LastToast(vm).Should().Be(("A-B repeat", "B must be after A"),
            "a rejected attempt must never be reported as accepted just because an older B is still set");
    }

    [Fact]
    public void A_playhead_that_advances_past_A_during_the_call_is_not_reported_as_a_rejection()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.Duration = Ticks(60);
        player.CurTime = Ticks(10);
        vm.SetAbLoopStart();                                // A = 10 s: the playhead sits exactly on the boundary
        player.BeforeSetABLoopEnd = () => player.CurTime = Ticks(11); // playback advances before the engine samples it

        vm.SetAbLoopEnd();

        vm.HasAbLoopB.Should().BeTrue();
        vm.AbLoopEndSeconds.Should().BeApproximately(11, 1e-9);
        vm.AbLoopEnabled.Should().BeTrue();
        LastToast(vm).Should().Be(("A-B repeat", "B set"),
            "the engine accepted the point, so the message must not claim a rejection");
    }

    [Fact]
    public void Commands_do_nothing_without_media()
    {
        var (vm, player, _, _, _) = TestVm.Create();

        vm.ToggleAbLoop();
        vm.SetAbLoopStart();
        vm.SetAbLoopEnd();
        vm.ClearAbLoop();

        player.AbLoopCalls.Should().BeEmpty("the view-model guards must stop the commands before they reach the controller");
        vm.Toasts.Should().BeEmpty("the transport is disabled before a media is opened");
        vm.AbLoopActive.Should().BeFalse();
    }

    [Fact]
    public void Engine_point_changes_are_mirrored_into_view_model_notifications()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.Duration = Ticks(60);
        player.CurTime = Ticks(7);

        List<string> names = [];
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null && !names.Contains(e.PropertyName))
                names.Add(e.PropertyName);
        };

        player.SetABLoopStart();                            // engine-side change (e.g. a bound hotkey)

        names.Should().Contain(nameof(MainWindowViewModel.AbLoopActive))
            .And.Contain(nameof(MainWindowViewModel.HasAbLoopA))
            .And.Contain(nameof(MainWindowViewModel.AbLoopStartSeconds));
        vm.AbLoopStartSeconds.Should().BeApproximately(7, 1e-9);
    }

    [Fact]
    public void Opening_another_file_drops_the_points_like_the_engine()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.Duration = Ticks(60);
        player.CurTime = Ticks(12);
        vm.SetAbLoopStart();
        player.CurTime = Ticks(42);
        vm.SetAbLoopEnd();
        vm.AbLoopEnabled.Should().BeTrue();

        player.ResetAbLoopForNewMedia();                    // the engine resets A-B inside ResetMe and re-raises

        vm.AbLoopActive.Should().BeFalse();
        vm.AbLoopEnabled.Should().BeFalse();
        vm.AbLoopStartSeconds.Should().Be(double.NaN);
        vm.AbLoopEndSeconds.Should().Be(double.NaN);
        vm.HasAbLoopA.Should().BeFalse();
        vm.HasAbLoopB.Should().BeFalse();
    }

    // ---- seek-bar marker geometry (pure) ------------------------------------------------------------------------

    [Fact]
    public void Marker_layout_is_empty_without_points_or_a_usable_track()
    {
        AbLoopMarkers.Layout(double.NaN, double.NaN, 60, 200, 6).Should().Be(default(AbLoopMarkerLayout));
        AbLoopMarkers.Layout(10, 20, double.NaN, 200, 6).Should().Be(default(AbLoopMarkerLayout));
        AbLoopMarkers.Layout(10, 20, 0, 200, 6).Should().Be(default(AbLoopMarkerLayout));
        AbLoopMarkers.Layout(10, 20, 60, 12, 6).Should().Be(default(AbLoopMarkerLayout), "a 12 DIP track cannot hold two 6 DIP insets");
    }

    [Fact]
    public void Marker_layout_maps_seconds_onto_the_thumb_centre_travel()
    {
        AbLoopMarkerLayout layout = AbLoopMarkers.Layout(10, 20, 60, 200, 6);

        layout.XA.Should().BeApproximately(6 + 10 / 60.0 * 188, 1e-9);
        layout.XB.Should().BeApproximately(6 + 20 / 60.0 * 188, 1e-9);
        layout.BandLeft.Should().BeApproximately(layout.XA!.Value, 1e-9);
        layout.BandRight.Should().BeApproximately(layout.XB!.Value, 1e-9);
    }

    [Fact]
    public void Marker_layout_clamps_points_into_the_track()
    {
        AbLoopMarkerLayout layout = AbLoopMarkers.Layout(-5, 90, 60, 200, 6);
        layout.XA.Should().Be(6, "a point before the start sits on the first thumb position");
        layout.XB.Should().Be(194, "a point past the end sits on the last thumb position");
    }

    [Fact]
    public void Marker_layout_keeps_both_markers_but_no_band_for_an_inverted_pair()
    {
        AbLoopMarkerLayout layout = AbLoopMarkers.Layout(40, 20, 60, 200, 6);
        layout.XA.Should().NotBeNull();
        layout.XB.Should().NotBeNull();
        layout.BandLeft.Should().BeNull();
        layout.BandRight.Should().BeNull();
    }

    // ---- seek-bar marker rendering ------------------------------------------------------------------------------

    static AbLoopMarkers CreateMarkers(double valueA, double valueB, IBrush? line = null, IBrush? band = null)
        => new()
        {
            Width = 200,
            Height = 20,
            Maximum = 60,
            ValueA = valueA,
            ValueB = valueB,
            Inset = 6,
            LineBrush = line ?? Brushes.Red,
            BandBrush = band ?? Brushes.Blue,
            BandOpacity = 0.5,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

    static Window Show(Control content)
    {
        Window window = new() { Width = 220, Height = 40, Background = Brushes.White, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    static WriteableBitmap Capture(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        return window.CaptureRenderedFrame() ?? throw new InvalidOperationException("headless Skia produced no frame");
    }

    static Color PixelAt(WriteableBitmap frame, int x, int y)
    {
        using ILockedFramebuffer fb = frame.Lock();
        unsafe
        {
            byte* p = (byte*)fb.Address + y * fb.RowBytes + x * 4;
            return Color.FromRgb(p[2], p[1], p[0]);
        }
    }

    [AvaloniaFact]
    public void Markers_draw_one_line_per_point_and_a_band_between_them()
    {
        AbLoopMarkers markers = CreateMarkers(10, 20);
        Window window = Show(markers);
        try
        {
            using WriteableBitmap frame = Capture(window);
            Point origin = markers.TranslatePoint(default, window)!.Value;
            int row = (int)Math.Round(origin.Y + 4);
            AbLoopMarkerLayout layout = AbLoopMarkers.Layout(10, 20, 60, markers.Bounds.Width, markers.Inset);
            int xA = (int)Math.Round(origin.X + layout.XA!.Value);
            int xB = (int)Math.Round(origin.X + layout.XB!.Value);
            int mid = (int)Math.Round(origin.X + (layout.BandLeft!.Value + layout.BandRight!.Value) / 2);

            Color lineA = PixelAt(frame, xA, row);
            lineA.R.Should().BeGreaterThan(200, "the A marker is a solid red line");
            lineA.G.Should().BeLessThan(80);
            lineA.B.Should().BeLessThan(80);

            Color band = PixelAt(frame, mid, row);
            band.B.Should().BeGreaterThan(180, "the band between the points is translucent blue on white");
            band.B.Should().BeGreaterThan(band.R);

            Color empty = PixelAt(frame, (int)Math.Round(origin.X) + 2, row);
            empty.Should().Be(Colors.White);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Markers_draw_nothing_without_points()
    {
        AbLoopMarkers empty = CreateMarkers(double.NaN, double.NaN);
        Window emptyWindow = Show(empty);
        try
        {
            using WriteableBitmap frame = Capture(emptyWindow);
            Point origin = empty.TranslatePoint(default, emptyWindow)!.Value;
            int row = (int)Math.Round(origin.Y + 4);
            AbLoopMarkerLayout wouldBe = AbLoopMarkers.Layout(10, 20, 60, empty.Bounds.Width, empty.Inset);

            PixelAt(frame, (int)Math.Round(origin.X + wouldBe.XA!.Value), row).Should().Be(Colors.White);
            PixelAt(frame, (int)Math.Round(origin.X + wouldBe.XB!.Value), row).Should().Be(Colors.White);
        }
        finally
        {
            emptyWindow.Close();
        }
    }

    // ---- real transport wiring ----------------------------------------------------------------------------------

    static (MainWindow Window, MainWindowViewModel Vm, FakePlaybackController Player) BuildWindow()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        FakePlaybackController player = new();
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        MainWindowViewModel vm = new(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, player);
    }

    [AvaloniaFact]
    public void Transport_exposes_the_AB_button_and_seek_bar_markers_wired_to_the_view_model()
    {
        var (window, vm, player) = BuildWindow();
        try
        {
            Button button = window.FindControl<Button>("AbLoopButton")!;
            button.Should().NotBeNull();
            button.Command.Should().BeSameAs(vm.ToggleAbLoopCommand);
            button.IsEnabled.Should().BeFalse("no media is open");

            AbLoopMarkers markers = window.FindControl<AbLoopMarkers>("AbLoopMarkers")!;
            markers.Should().NotBeNull();
            markers.IsVisible.Should().BeFalse();
            markers.IsHitTestVisible.Should().BeFalse("the marker overlay must never steal seeking from the slider");
            window.FindControl<Slider>("SeekSlider")!.IsHitTestVisible.Should().BeTrue();

            LucideIcon idleIcon = window.FindControl<LucideIcon>("AbLoopIconIdle")!;
            LucideIcon activeIcon = window.FindControl<LucideIcon>("AbLoopIconActive")!;
            idleIcon.IsVisible.Should().BeTrue("the A-B icon is unlit while no point is set");
            activeIcon.IsVisible.Should().BeFalse();

            player.IsOpened = true;
            player.Duration = Ticks(60);
            player.CurTime = Ticks(15);
            Dispatcher.UIThread.RunJobs();
            button.IsEnabled.Should().BeTrue();

            vm.ToggleAbLoop();                              // A = 15 s
            Dispatcher.UIThread.RunJobs();
            markers.IsVisible.Should().BeTrue();
            markers.ValueA.Should().BeApproximately(15, 1e-9);
            markers.Maximum.Should().BeApproximately(60, 1e-9);
            idleIcon.IsVisible.Should().BeFalse();
            activeIcon.IsVisible.Should().BeTrue("the icon is lit while a point is set");

            player.CurTime = Ticks(45);
            vm.ToggleAbLoop();                              // B = 45 s
            Dispatcher.UIThread.RunJobs();
            markers.ValueB.Should().BeApproximately(45, 1e-9);
            vm.AbLoopEnabled.Should().BeTrue();

            vm.ClearAbLoop();
            Dispatcher.UIThread.RunJobs();
            markers.IsVisible.Should().BeFalse();
            idleIcon.IsVisible.Should().BeTrue();
            activeIcon.IsVisible.Should().BeFalse();
            button.IsEnabled.Should().BeTrue("the media is still open");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Right_click_menu_offers_the_three_explicit_actions()
    {
        var (window, vm, player) = BuildWindow();
        try
        {
            Button button = window.FindControl<Button>("AbLoopButton")!;
            MenuFlyout flyout = button.ContextFlyout.Should().BeOfType<MenuFlyout>().Subject;
            flyout.Items.Should().HaveCount(3);

            player.IsOpened = true;
            player.Duration = Ticks(60);
            player.CurTime = Ticks(10);
            vm.SetAbLoopStart();                            // a set point enables Clear in the open menu
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            try
            {
                flyout.Items[0].Should().BeOfType<MenuItem>().Subject.Command.Should().BeSameAs(vm.SetAbLoopStartCommand);
                flyout.Items[1].Should().BeOfType<MenuItem>().Subject.Command.Should().BeSameAs(vm.SetAbLoopEndCommand);
                MenuItem clear = flyout.Items[2].Should().BeOfType<MenuItem>().Subject;
                clear.Command.Should().BeSameAs(vm.ClearAbLoopCommand);
                clear.IsEnabled.Should().BeTrue("Clear is reachable while a point is set");
                vm.ClearAbLoop();
                Dispatcher.UIThread.RunJobs();
                clear.IsEnabled.Should().BeFalse("Clear is disabled once no point is set");
            }
            finally
            {
                flyout.Hide();
            }
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Seek_bar_markers_align_with_the_real_thumb_positions()
    {
        var (window, vm, player) = BuildWindow();
        try
        {
            player.IsOpened = true;
            player.Duration = Ticks(60);
            player.CurTime = Ticks(12);
            vm.SetAbLoopStart();                            // A = 12 s
            player.CurTime = Ticks(42);
            vm.SetAbLoopEnd();                              // B = 42 s

            AbLoopMarkers markers = window.FindControl<AbLoopMarkers>("AbLoopMarkers")!;
            markers.LineBrush = Brushes.Red;                // distinct from the theme-coloured thumb

            player.CurTime = Ticks(12);                     // playhead on A: one marker must sit under the thumb
            Dispatcher.UIThread.RunJobs();
            (int markerA, int thumbA) = MarkerAndThumbColumns(window);

            player.CurTime = Ticks(42);                     // playhead on B
            Dispatcher.UIThread.RunJobs();
            (int markerB, int thumbB) = MarkerAndThumbColumns(window);

            Math.Abs(markerA - thumbA).Should().BeLessThanOrEqualTo(1,
                $"the A marker at column {markerA} must align with the thumb at {thumbA}");
            Math.Abs(markerB - thumbB).Should().BeLessThanOrEqualTo(1,
                $"the B marker at column {markerB} must align with the thumb at {thumbB}");
            (markerB - markerA).Should().BeGreaterThan(50, "the two points are 30 s apart on a ~60 s bar");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    /// <summary>Columns (relative to the window) of the seek thumb centre and of the nearest marker line.</summary>
    static (int Marker, int Thumb) MarkerAndThumbColumns(MainWindow window)
    {
        WriteableBitmap frame = Capture(window);
        Slider slider = window.FindControl<Slider>("SeekSlider")!;
        AbLoopMarkers markers = window.FindControl<AbLoopMarkers>("AbLoopMarkers")!;
        Thumb thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();

        Point sliderOrigin = slider.TranslatePoint(default, window)!.Value;
        Point markersOrigin = markers.TranslatePoint(default, window)!.Value;
        thumb.Bounds.Width.Should().Be(12, "the thin seek slider uses a 12-DIP thumb");
        markers.Inset.Should().Be(6, "the marker overlay must use exactly half of the thumb width as its inset");
        // Both columns are relative to the slider: the seek panel can sit inside padded transport chrome.
        Point thumbCentre = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), slider)!.Value;
        int thumbColumn = (int)Math.Round(thumbCentre.X);
        int row = (int)Math.Round(markersOrigin.Y + 2);
        int width = (int)Math.Round(slider.Bounds.Width);

        int markerColumn = int.MinValue;
        int best = int.MaxValue;
        for (int column = 0; column < width; column++)
        {
            Color c = PixelAt(frame, (int)Math.Round(sliderOrigin.X) + column, row);
            if (c.R < 200 || c.G > 70 || c.B > 70)
                continue;
            int distance = Math.Abs(column - thumbColumn);
            if (distance < best)
            {
                best = distance;
                markerColumn = column;
            }
        }

        markerColumn.Should().BeGreaterThanOrEqualTo(0, "the marker line must be visible on the seek bar");
        return (markerColumn, thumbColumn);
    }
}

/// <summary>
/// Env-gated real-engine integration (FFmpeg + the generated fixture, like <see cref="EndToEndTests"/>): the unit tests
/// above run through <see cref="FakePlaybackController"/>, so this is the only proof that the production
/// <see cref="FlyleafPlaybackController"/> re-raises every engine A-B property and that the view model receives those
/// notifications. Skipped when the fixtures are absent; the Windows Avalonia gate provides them.
/// </summary>
[Collection(FlyleafGlobalsCollection.Name)]
public sealed class AbLoopRealEngineTests
{
    static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout)
                return false;
            await Task.Delay(25);
        }
        return true;
    }

    static long Ticks(double seconds) => TimeSpan.FromSeconds(seconds).Ticks;

    [AvaloniaFact]
    public async Task Real_engine_ab_loop_properties_are_reraised_by_the_adapter_and_mirrored_by_the_view_model()
    {
        string? ffmpegDir = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpegDir), "Real-player A-B integration requires the FFmpeg shared libraries.");
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrEmpty(media) || !File.Exists(media), "Real-player A-B integration requires LLPLAYER_TEST_MEDIA.");

        using TempDir state = new();
        AppPaths paths = new(Path.Combine(state.Path, "config"), Path.Combine(state.Path, "state"));
        KeyMapper keys = new();
        AvaloniaUIDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, keys);
        if (!Engine.IsLoaded)
            EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpegDir!, paths));
        Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
        config.Subtitles.SearchLocal = false;
        config.Subtitles.LanguageAutoDetect = false;
        Player player = new(config);
        FlyleafPlaybackController controller = new(player, dispatcher, bridge);
        MainWindowViewModel vm = new(controller, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        try
        {
            List<string> raised = [];
            controller.PropertyChanged += (_, e) => { if (e.PropertyName != null) raised.Add(e.PropertyName); };
            List<string> vmRaised = [];
            vm.PropertyChanged += (_, e) => { if (e.PropertyName != null) vmRaised.Add(e.PropertyName); };

            controller.Open(media!);
            (await WaitUntil(() => controller.IsOpened, TimeSpan.FromSeconds(30))).Should().BeTrue("the fixture clip opens");
            controller.Pause();
            controller.SeekTo(TimeSpan.FromSeconds(1));
            (await WaitUntil(() => controller.CurTime >= Ticks(1), TimeSpan.FromSeconds(15))).Should().BeTrue("the seek reaches 1 s");

            Dispatcher.UIThread.RunJobs();
            raised.Clear();
            vmRaised.Clear();
            controller.SetABLoopStart();
            Dispatcher.UIThread.RunJobs();
            raised.Should().Contain(nameof(IPlaybackController.ABLoopA))
                .And.Contain(nameof(IPlaybackController.HasABLoopA))
                .And.Contain(nameof(IPlaybackController.ABLoopActive));
            vm.HasAbLoopA.Should().BeTrue("the adapter re-raise drives the view model");
            vm.AbLoopStartSeconds.Should().BeApproximately(controller.ABLoopA / (double)TimeSpan.TicksPerSecond, 0.01);
            vmRaised.Should().Contain(nameof(MainWindowViewModel.AbLoopStartSeconds));

            controller.SeekTo(TimeSpan.FromSeconds(3));
            (await WaitUntil(() => controller.CurTime >= Ticks(3), TimeSpan.FromSeconds(15))).Should().BeTrue("the seek reaches 3 s");
            Dispatcher.UIThread.RunJobs();
            raised.Clear();
            vmRaised.Clear();
            controller.SetABLoopEnd();
            Dispatcher.UIThread.RunJobs();
            raised.Should().Contain(nameof(IPlaybackController.ABLoopB))
                .And.Contain(nameof(IPlaybackController.HasABLoopB))
                .And.Contain(nameof(IPlaybackController.ABLoopEnabled));
            vm.AbLoopEnabled.Should().BeTrue("both points are set with A < B");
            vm.AbLoopEndSeconds.Should().BeGreaterThan(vm.AbLoopStartSeconds);
            vmRaised.Should().Contain(nameof(MainWindowViewModel.AbLoopEnabled))
                .And.Contain(nameof(MainWindowViewModel.AbLoopEndSeconds));

            raised.Clear();
            controller.ClearABLoop();
            Dispatcher.UIThread.RunJobs();
            raised.Should().Contain(nameof(IPlaybackController.ABLoopActive));
            vm.AbLoopActive.Should().BeFalse("the engine reset is mirrored");
            vm.HasAbLoopA.Should().BeFalse();
            vm.HasAbLoopB.Should().BeFalse();
            double.IsNaN(vm.AbLoopStartSeconds).Should().BeTrue("unset points are exposed as NaN to the marker overlay");
        }
        finally
        {
            controller.Dispose();
            player.Dispose();
        }
    }
}
