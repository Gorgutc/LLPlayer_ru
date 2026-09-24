using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class MainWindowTests
{
    static (MainWindow Window, MainWindowViewModel Vm, FakePlaybackController Player, FakeShell Shell) Build(string theme = "Dark", TimeProvider? time = null)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        FakePlaybackController player = new();
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        MainWindowViewModel vm = new(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), window, time);
        window.Attach(vm, renderer: null);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, player, new FakeShell());
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

    [AvaloniaTheory]
    [InlineData("Dark", 0x0A)]
    [InlineData("Light", 0xFF)]
    public void MainWindow_Builds_InBothThemes_WithTokenBackground(string theme, byte expectedGrey)
    {
        var (window, _, _, _) = Build(theme);
        try
        {
            window.FindControl<Menu>("MainMenu").Should().NotBeNull();
            window.FindControl<VideoView>("Video").Should().NotBeNull();
            window.FindControl<Border>("TransportBar")!.IsVisible.Should().BeTrue();

            WriteableBitmap frame = window.CaptureRenderedFrame()!;
            // right end of the menu bar: plain window/background token colour
            Color c = PixelAt(frame, 700, 6);
            c.R.Should().Be(expectedGrey);
            c.G.Should().Be(expectedGrey);
            c.B.Should().Be(expectedGrey);
            // empty state card is shown without media
            window.GetVisualDescendants().OfType<Border>().Should().Contain(b => b.Classes.Contains("card") && b.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Overlay_ShowsGivenText_AndHidesWhenEmpty()
    {
        var (window, vm, player, _) = Build();
        try
        {
            SubtitleText primary = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            SubtitleText secondary = window.FindControl<SubtitleText>("SecondarySubtitle")!;
            primary.IsVisible.Should().BeFalse();
            secondary.IsVisible.Should().BeFalse();

            player.PrimaryText = "Hello world, this is a test.";
            player.SecondaryText = "Привет, мир";
            Dispatcher.UIThread.RunJobs();
            primary.IsVisible.Should().BeTrue();
            primary.Text.Should().Be("Hello world, this is a test.");
            secondary.IsVisible.Should().BeTrue();
            secondary.Text.Should().Be("Привет, мир");
            primary.FontSize.Should().Be(vm.SubtitleFontSize);
            secondary.FontSize.Should().BeLessThan(primary.FontSize);

            // subtitles toggled off -> hidden although text exists
            player.SubtitlesVisible = false;
            Dispatcher.UIThread.RunJobs();
            primary.IsVisible.Should().BeFalse();
            secondary.IsVisible.Should().BeFalse();

            player.SubtitlesVisible = true;
            player.PrimaryText = "";
            Dispatcher.UIThread.RunJobs();
            primary.IsVisible.Should().BeFalse();
            secondary.IsVisible.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OverlayText_IsDrawnOverTheVideoArea()
    {
        var (window, _, player, _) = Build();
        try
        {
            player.IsOpened = true;
            player.PrimaryText = "MMMMMMMM";
            Dispatcher.UIThread.RunJobs();

            SubtitleText primary = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            Point center = primary.TranslatePoint(new Point(primary.Bounds.Width / 2, primary.Bounds.Height / 2), window)!.Value;
            WriteableBitmap frame = window.CaptureRenderedFrame()!;

            // Some white (fill) pixels must exist in the text band.
            int whites = 0;
            for (int dx = -120; dx <= 120; dx += 2)
            {
                Color c = PixelAt(frame, (int)center.X + dx, (int)center.Y);
                if (c.R > 230 && c.G > 230 && c.B > 230)
                    whites++;
            }
            whites.Should().BeGreaterThan(5);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WordClick_OnOverlay_PausesAndOpensPopup()
    {
        var (window, vm, player, _) = Build();
        try
        {
            player.IsOpened = true;
            player.IsPlaying = true;
            player.PrimaryText = "Hello world";
            Dispatcher.UIThread.RunJobs();

            SubtitleText primary = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            primary.TryGetWordAt(new Point(primary.Bounds.Width / 2 - 30, primary.Bounds.Height / 2), out string word).Should().BeTrue();
            word.Should().Be("Hello");

            Point p = primary.TranslatePoint(new Point(primary.Bounds.Width / 2 - 30, primary.Bounds.Height / 2), window)!.Value;
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            player.PauseCalls.Should().Be(1);
            vm.WordPopup.IsOpen.Should().BeTrue();
            vm.WordPopup.Word.Should().Be("Hello");
            vm.WordPopup.Translation.Should().Be("[Hello]");
            window.FindControl<Border>("WordPopupCard")!.IsVisible.Should().BeTrue();

            // Esc closes the popup (app-level, before the engine sees the key)
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            vm.WordPopup.IsOpen.Should().BeFalse();
            player.KeysDown.Should().BeEmpty();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Keyboard_AppShortcutsRunInTheApp_OtherKeysGoToTheEngine()
    {
        var (window, vm, player, _) = Build();
        try
        {
            vm.SidebarVisible.Should().BeFalse();
            window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, "b");
            Dispatcher.UIThread.RunJobs();
            vm.SidebarVisible.Should().BeTrue();
            window.FindControl<Border>("Sidebar")!.IsVisible.Should().BeTrue();
            player.KeysDown.Should().NotContain(System.Windows.Input.Key.B);

            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            player.KeysDown.Should().Equal(System.Windows.Input.Key.Space);
            player.KeysUp.Should().Equal(System.Windows.Input.Key.Space);

            // typing in the sidebar filter must not reach the player
            TextBox filter = window.FindControl<TextBox>("FilterBox")!;
            filter.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            Dispatcher.UIThread.RunJobs();
            player.KeysDown.Should().Equal(System.Windows.Input.Key.Space);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SeekBar_FollowsPlayback_WithoutSeeking_EvenWhenANewShorterMediaOpens()
    {
        var (window, vm, player, _) = Build();
        try
        {
            Slider seek = window.FindControl<Slider>("SeekSlider")!;
            player.IsOpened = true;
            player.Duration = TimeSpan.FromSeconds(100).Ticks;
            player.CurTime = TimeSpan.FromSeconds(90).Ticks;
            Dispatcher.UIThread.RunJobs();
            seek.Maximum.Should().Be(100);
            seek.Value.Should().Be(90);

            // next file: 12 s long -> the slider coerces 90 to 12 and writes it back; that is not a user seek
            player.Duration = TimeSpan.FromSeconds(12).Ticks;
            Dispatcher.UIThread.RunJobs();
            player.CurTime = 0;
            Dispatcher.UIThread.RunJobs();
            seek.Value.Should().Be(0);
            player.Seeks.Should().BeEmpty();
            window.FindControl<TextBlock>("DurationTime")!.Text.Should().Be("0:12");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FullScreen_OverlaysTheBar_AndHidesBarAndCursorWhenIdle()
    {
        ManualTimeProvider time = new();
        var (window, vm, player, _) = Build(time: time);
        try
        {
            Border bar = window.FindControl<Border>("TransportBar")!;
            player.IsOpened = true;
            player.IsPlaying = true;

            vm.ToggleFullScreen();   // F / double-click in the real app
            Dispatcher.UIThread.RunJobs();
            window.WindowState.Should().Be(WindowState.FullScreen);
            vm.IsFullScreen.Should().BeTrue();
            window.FindControl<Border>("MenuBar")!.IsVisible.Should().BeFalse();
            bar.Classes.Should().Contain("overlay");
            Grid.GetRow(bar).Should().Be(0);

            time.Advance(MainWindowViewModel.IdleTimeout + TimeSpan.FromSeconds(1));
            vm.UpdateIdle();
            Dispatcher.UIThread.RunJobs();
            bar.IsHitTestVisible.Should().BeFalse();
            vm.ControlsVisible.Should().BeFalse();
            // (the bar's Opacity animates to 0 through a 0.2 s transition; not asserted: no timing-bound checks)
            window.FindControl<Panel>("VideoHost")!.Cursor.Should().NotBe(Cursor.Default);

            window.MouseMove(new Point(200, 200));
            Dispatcher.UIThread.RunJobs();
            vm.ControlsVisible.Should().BeTrue();
            bar.IsHitTestVisible.Should().BeTrue();

            vm.ToggleFullScreen();
            Dispatcher.UIThread.RunJobs();
            window.WindowState.Should().NotBe(WindowState.FullScreen);
            bar.Classes.Should().NotContain("overlay");
            Grid.GetRow(bar).Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CheatSheetWindow_ShowsKeyCaps_AndFilters()
    {
        FlyleafLib.MediaPlayer.KeysConfig engine = new();
        engine.LoadDefault();
        CheatSheetViewModel sheet = new(AppShortcutMap.BuildCheatSheet(engine.Keys));
        CheatSheetWindow window = new(sheet) { Width = 640, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("kbd")).Should().BeGreaterThan(40);
            SaveEvidence(window, "cheat-sheet.png");

            window.FindControl<TextBox>("SearchBox")!.Text = "subtitle";
            Dispatcher.UIThread.RunJobs();
            sheet.Groups.SelectMany(g => g.Rows).Should().NotBeEmpty()
                .And.OnlyContain(r => r.Description.Contains("subtitle", StringComparison.OrdinalIgnoreCase)
                                      || r.ActionName.Contains("subtitle", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Saves a rendered frame when LLPLAYER_EVIDENCE_DIR is set (visual review; never required).</summary>
    static void SaveEvidence(TopLevel top, string name)
    {
        string? dir = Environment.GetEnvironmentVariable("LLPLAYER_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(dir))
            return;
        Directory.CreateDirectory(dir);
        top.CaptureRenderedFrame()!.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void Sidebar_ListsCues_HighlightsCurrent_AndClickSeeks()
    {
        var (window, vm, player, _) = Build();
        try
        {
            player.Cues[0].AddRange(
            [
                new CueInfo(0, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3), "Hello world, this is a test."),
                new CueInfo(1, TimeSpan.FromSeconds(3.5), TimeSpan.FromSeconds(6.5), "The quick brown fox jumps over the lazy dog."),
                new CueInfo(2, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(11), "Language learning with subtitles."),
            ]);
            player.RaiseCuesChanged(0);
            vm.ToggleSidebar();
            player.PrimaryCueIndex = 1;
            Dispatcher.UIThread.RunJobs();

            ListBox list = window.FindControl<ListBox>("CueList")!;
            list.ItemCount.Should().Be(3);
            Grid content = window.FindControl<Grid>("ContentGrid")!;
            content.ColumnDefinitions[2].Width.Should().Be(new GridLength(vm.SidebarWidth));
            window.FindControl<Border>("Sidebar")!.Bounds.Width.Should().BeApproximately(vm.SidebarWidth, 0.5);
            vm.Sidebar.CurrentCue!.Index.Should().Be(1);
            vm.Sidebar.Cues[1].IsCurrent.Should().BeTrue();

            // click (tap) the third row -> seek to its start
            Control row = list.ContainerFromIndex(2)!;
            Point p = row.TranslatePoint(new Point(20, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            player.Seeks.Should().Equal(TimeSpan.FromSeconds(7));

            vm.ToggleSidebar();
            Dispatcher.UIThread.RunJobs();
            content.ColumnDefinitions[2].Width.Should().Be(GridLength.Auto);
            window.FindControl<Border>("Sidebar")!.IsVisible.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }
}
