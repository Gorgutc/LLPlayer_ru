using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class TransportSidebarLayoutTests
{
    [AvaloniaTheory]
    [InlineData(640, 420, 360, false)]
    [InlineData(640, 420, 360, true)]
    [InlineData(640, 420, 900, false)]
    [InlineData(640, 420, 900, true)]
    [InlineData(1280, 760, 360, false)]
    [InlineData(1280, 760, 360, true)]
    [InlineData(1280, 760, 900, false)]
    [InlineData(1280, 760, 900, true)]
    public void TransportControls_StayWithinVideoColumn_WithWideSidebar(int width, int height, int sidebarWidth, bool longDuration)
    {
        FakePlaybackController player = new()
        {
            IsOpened = true,
            CurTime = longDuration ? TimeSpan.FromHours(119).Ticks : 0,
            Duration = longDuration ? TimeSpan.FromHours(120).Ticks : 0
        };
        MainWindow window = new(new KeyMapper(), null) { Width = width, Height = height };
        AppPrefs prefs = new() { SidebarVisible = true, SidebarWidth = sidebarWidth };
        MainWindowViewModel vm = new(player, prefs.Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Grid videoColumn = window.FindControl<Grid>("VideoColumn")!;
            Slider volume = window.FindControl<Slider>("VolumeSlider")!;
            Panel transportControls = (Panel)volume.Parent!;
            Grid content = window.FindControl<Grid>("ContentGrid")!;
            Border sidebar = window.FindControl<Border>("Sidebar")!;
            GridSplitter splitter = window.FindControl<GridSplitter>("SidebarSplitter")!;
            videoColumn.Bounds.Width.Should().BeGreaterThanOrEqualTo(240);
            videoColumn.Bounds.Width.Should().BeApproximately(content.Bounds.Width - sidebar.Bounds.Width - splitter.Bounds.Width, 0.5);
            Point videoOrigin = videoColumn.TranslatePoint(default, window)!.Value;
            videoOrigin.X.Should().BeGreaterThanOrEqualTo(0);
            (videoOrigin.X + videoColumn.Bounds.Width).Should().BeLessThanOrEqualTo(window.ClientSize.Width);
            var bounds = transportControls.Children.Select(control =>
            {
                Point origin = control.TranslatePoint(default, videoColumn)!.Value;
                return (Control: control, Bounds: new Rect(origin, control.Bounds.Size));
            }).ToArray();
            string measured = string.Join("; ", bounds.Select(item =>
                $"{item.Control.Name ?? item.Control.GetType().Name}={item.Bounds}"));
            bounds.Should().OnlyContain(item => item.Control.IsEffectivelyVisible && item.Bounds.Width > 0 && item.Bounds.Height > 0
                && item.Bounds.Left >= -0.5 && item.Bounds.Right <= videoColumn.Bounds.Width + 0.5
                && item.Bounds.Top >= 0 && item.Bounds.Bottom <= videoColumn.Bounds.Height + 0.5,
                $"all transport controls must remain reachable in the {videoColumn.Bounds.Width} DIP video column: {measured}");
            if (sidebarWidth == 360 && width == 1280)
            {
                bounds.Should().OnlyContain(item => Math.Abs(item.Bounds.Center.Y - bounds[0].Bounds.Center.Y) <= 0.5,
                    "a wide transport should remain on one row");
                (bounds[3].Bounds.Left - bounds[2].Bounds.Right).Should().BeGreaterThan(100,
                    "the original left/right control grouping should retain its flexible gap");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WideRememberedSidebar_ShrinksOnResize_WithoutLosingItsPreference()
    {
        FakePlaybackController player = new()
        {
            IsOpened = true,
            CurTime = TimeSpan.FromHours(119).Ticks,
            Duration = TimeSpan.FromHours(120).Ticks
        };
        MainWindow window = new(new KeyMapper(), null) { Width = 1280, Height = 760 };
        AppPrefs prefs = new() { SidebarVisible = true, SidebarWidth = 900 };
        MainWindowViewModel vm = new(player, prefs.Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Border sidebar = window.FindControl<Border>("Sidebar")!;
            sidebar.Bounds.Width.Should().BeApproximately(900, 0.5);

            window.Width = 640;
            Dispatcher.UIThread.RunJobs();
            Grid video = window.FindControl<Grid>("VideoColumn")!;
            video.Bounds.Width.Should().BeGreaterThanOrEqualTo(240, "resizing must preserve usable transport space");
            sidebar.Bounds.Width.Should().BeLessThan(900);
            vm.SidebarWidth.Should().Be(900);
            prefs.SidebarWidth.Should().Be(900);
            Slider volume = window.FindControl<Slider>("VolumeSlider")!;
            Panel controls = (Panel)volume.Parent!;
            foreach (Control control in controls.Children)
            {
                Point origin = control.TranslatePoint(default, video)!.Value;
                control.IsEffectivelyVisible.Should().BeTrue();
                origin.X.Should().BeGreaterThanOrEqualTo(0);
                (origin.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(video.Bounds.Width);
            }

            window.Width = 1280;
            Dispatcher.UIThread.RunJobs();
            sidebar.Bounds.Width.Should().BeApproximately(900, 0.5, "widening must restore the remembered width");
            prefs.SidebarWidth.Should().Be(900);
        }
        finally
        {
            window.Close();
        }
    }
}
