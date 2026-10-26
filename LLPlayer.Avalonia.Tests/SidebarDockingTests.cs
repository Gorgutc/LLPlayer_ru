using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Sidebar left/right docking plus row typography in the real headless window layout.</summary>
public class SidebarDockingTests
{
    static FakePlaybackController Player()
    {
        FakePlaybackController player = new() { IsOpened = true, CurTime = TimeSpan.FromSeconds(2).Ticks, Duration = TimeSpan.FromSeconds(90).Ticks };
        player.Cues[0].AddRange(
        [
            new CueInfo(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "First cue", false, "en", "ru", "First cue"),
            new CueInfo(1, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "Second cue", false, "en", "ru", "Second cue"),
        ]);
        return player;
    }

    [AvaloniaFact]
    public void SidebarLeft_docks_the_sidebar_before_the_video_and_keeps_the_splitter()
    {
        FakePlaybackController player = Player();
        AppPrefs prefs = new() { SidebarVisible = true, SidebarWidth = 400, SidebarLeft = true };
        MainWindow window = new(new KeyMapper(), null) { Width = 1280, Height = 760 };
        MainWindowViewModel vm = new(player, prefs.Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            vm.Sidebar.Reload();
            Dispatcher.UIThread.RunJobs();
            Grid content = window.FindControl<Grid>("ContentGrid")!;
            Grid video = window.FindControl<Grid>("VideoColumn")!;
            Border sidebar = window.FindControl<Border>("Sidebar")!;
            GridSplitter splitter = window.FindControl<GridSplitter>("SidebarSplitter")!;

            Grid.GetColumn(sidebar).Should().Be(0);
            Grid.GetColumn(splitter).Should().Be(1);
            Grid.GetColumn(video).Should().Be(2);
            sidebar.Bounds.Width.Should().BeApproximately(400, 0.5);
            video.Bounds.Width.Should().BeGreaterThanOrEqualTo(240);
            double sidebarRight = sidebar.TranslatePoint(new Point(sidebar.Bounds.Width, 0), content)!.Value.X;
            double videoLeft = video.TranslatePoint(default, content)!.Value.X;
            sidebarRight.Should().BeLessThanOrEqualTo(videoLeft + 0.5, "the sidebar must be left of the video");
            video.TranslatePoint(default, window)!.Value.X.Should().BeGreaterThan(sidebar.Bounds.Width - 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SidebarLeft_remembers_width_hides_cleanly_and_restores_on_the_right()
    {
        FakePlaybackController player = Player();
        AppPrefs prefs = new() { SidebarVisible = true, SidebarWidth = 900, SidebarLeft = true };
        MainWindow window = new(new KeyMapper(), null) { Width = 1280, Height = 760 };
        MainWindowViewModel vm = new(player, prefs.Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            vm.Sidebar.Reload();
            Dispatcher.UIThread.RunJobs();
            Border sidebar = window.FindControl<Border>("Sidebar")!;
            Grid video = window.FindControl<Grid>("VideoColumn")!;
            sidebar.Bounds.Width.Should().BeApproximately(900, 0.5);
            video.Bounds.Width.Should().BeGreaterThanOrEqualTo(240);

            window.Width = 640;
            Dispatcher.UIThread.RunJobs();
            video.Bounds.Width.Should().BeGreaterThanOrEqualTo(240, "narrowing must keep the video usable");
            sidebar.Bounds.Width.Should().BeLessThan(900);
            vm.SidebarWidth.Should().Be(900);

            window.Width = 1280;
            Dispatcher.UIThread.RunJobs();
            sidebar.Bounds.Width.Should().BeApproximately(900, 0.5);

            vm.ToggleSidebarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            sidebar.IsVisible.Should().BeFalse();
            video.Bounds.Width.Should().BeApproximately(window.ClientSize.Width, 1);

            vm.ToggleSidebarCommand.Execute(null);
            prefs = new AppPrefs { SidebarVisible = true, SidebarWidth = 360, SidebarLeft = false };
            MainWindow rightWindow = new(new KeyMapper(), null) { Width = 1280, Height = 760 };
            MainWindowViewModel rightVm = new(player, prefs.Normalize(), _ => { }, new FakeTranslator(), rightWindow);
            rightWindow.Attach(rightVm, renderer: null);
            rightWindow.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Border rightSidebar = rightWindow.FindControl<Border>("Sidebar")!;
                Grid rightVideo = rightWindow.FindControl<Grid>("VideoColumn")!;
                Grid rightContent = rightWindow.FindControl<Grid>("ContentGrid")!;
                Grid.GetColumn(rightSidebar).Should().Be(2);
                Grid.GetColumn(rightVideo).Should().Be(0);
                rightSidebar.Bounds.Width.Should().BeApproximately(360, 0.5);
                double videoRight = rightVideo.TranslatePoint(new Point(rightVideo.Bounds.Width, 0), rightContent)!.Value.X;
                double sidebarLeft = rightSidebar.TranslatePoint(default, rightContent)!.Value.X;
                videoRight.Should().BeLessThanOrEqualTo(sidebarLeft + 0.5);
            }
            finally { rightWindow.Close(); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Sidebar_rows_use_the_sidebar_typography_while_the_video_overlay_keeps_its_own()
    {
        FakePlaybackController player = Player();
        AppPrefs prefs = new()
        {
            SidebarVisible = true, SidebarWidth = 420, SidebarFontFamily = "Noto Sans", SidebarFontSize = 22,
            SidebarFontWeight = "Bold", SidebarSubPadding = 12,
        };
        MainWindow window = new(new KeyMapper(), null) { Width = 1280, Height = 760 };
        MainWindowViewModel vm = new(player, prefs.Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            vm.Sidebar.Reload();
            Dispatcher.UIThread.RunJobs();
            ListBox list = window.FindControl<ListBox>("CueList")!;
            ListBoxItem[] rows = list.GetRealizedContainers().OfType<ListBoxItem>().ToArray();
            rows.Should().NotBeEmpty();
            SubtitleText rowText = rows[0].GetVisualDescendants().OfType<SubtitleText>().Single();
            rowText.FontSize.Should().Be(22);
            rowText.FontWeight.Should().Be(global::Avalonia.Media.FontWeight.Bold);
            rowText.FontFamily.Name.Should().Be(vm.Sidebar.Cues[0].RowFontFamily.Name, "the realized row follows its resolved row typography");
            Border rowBorder = rows[0].GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("cue"));
            rowBorder.Padding.Should().Be(new Thickness(12));
            rowText.Text.Should().Be("First cue");

            SubtitleText overlay = window.FindControl<SubtitleText>("PrimarySubtitle")!;
            overlay.FontSize.Should().Be(34, "the overlay font size is a separate preference");

            vm.SidebarTextMask = true;
            vm.ApplySidebarAppearance();
            vm.Sidebar.Reload();
            vm.Sidebar.UpdateCurrent(0);
            Dispatcher.UIThread.RunJobs();
            rows = list.GetRealizedContainers().OfType<ListBoxItem>().ToArray();
            rows.SelectMany(r => r.GetVisualDescendants().OfType<SubtitleText>())
                .Select(t => t.Text).Should().Equal("First cue", new string('_', "Second cue".Length));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Live_settings_save_repaints_realized_rows_and_switches_dock_in_place()
    {
        FakePlaybackController player = Player();
        AppPrefs prefs = new() { SidebarVisible = true, SidebarWidth = 400 };
        MainWindow window = new(new KeyMapper(), null) { Width = 1280, Height = 760 };
        MainWindowViewModel vm = new(player, prefs.Normalize(), _ => { }, new FakeTranslator(), window);
        window.Attach(vm, renderer: null);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            vm.Sidebar.Reload();
            Dispatcher.UIThread.RunJobs();
            Grid content = window.FindControl<Grid>("ContentGrid")!;
            Grid video = window.FindControl<Grid>("VideoColumn")!;
            Border sidebar = window.FindControl<Border>("Sidebar")!;
            GridSplitter splitter = window.FindControl<GridSplitter>("SidebarSplitter")!;
            ListBox list = window.FindControl<ListBox>("CueList")!;
            ListBoxItem row = list.GetRealizedContainers().OfType<ListBoxItem>().First();
            SubtitleText rowText = row.GetVisualDescendants().OfType<SubtitleText>().Single();
            Border rowBorder = row.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("cue"));
            rowText.FontSize.Should().Be(16);
            rowBorder.Padding.Should().Be(new Thickness(5));

            // A live Settings save must restyle the already-realized row and swap the dock without rebuilding the list.
            SettingsViewModel draft = vm.CreateSettings();
            draft.SidebarFontSizeText = "30";
            draft.SidebarSubPaddingText = "12";
            draft.SidebarLeft = true;
            draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(draft.Error);
            vm.SidebarLeft.Should().BeTrue();
            Grid.GetColumn(sidebar).Should().Be(0);
            Grid.GetColumn(splitter).Should().Be(1);
            Grid.GetColumn(video).Should().Be(2);
            rowText = list.GetRealizedContainers().OfType<ListBoxItem>().First().GetVisualDescendants().OfType<SubtitleText>().Single();
            rowText.FontSize.Should().Be(30, "a live sidebar font change must repaint realized rows");
            rowBorder = list.GetRealizedContainers().OfType<ListBoxItem>().First().GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("cue"));
            rowBorder.Padding.Should().Be(new Thickness(12));

            // The swapped video column must not inherit the former sidebar's Min/Max constraints.
            void AssertVideoFillsRemainder()
            {
                double expected = content.Bounds.Width - sidebar.Bounds.Width - splitter.Bounds.Width;
                video.Bounds.Width.Should().BeApproximately(expected, 1, "the video column must fill the content grid after a dock switch");
            }

            AssertVideoFillsRemainder();
            window.Width = 700;
            Dispatcher.UIThread.RunJobs();
            video.Bounds.Width.Should().BeGreaterThanOrEqualTo(240);
            AssertVideoFillsRemainder();
            window.Width = 1600;
            Dispatcher.UIThread.RunJobs();
            AssertVideoFillsRemainder();

            vm.ToggleSidebarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            sidebar.IsVisible.Should().BeFalse();
            video.Bounds.Width.Should().BeApproximately(content.Bounds.Width, 1);
            vm.ToggleSidebarCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            AssertVideoFillsRemainder();

            window.IsFullScreen = true;
            Dispatcher.UIThread.RunJobs();
            Grid.GetColumn(video).Should().Be(2);
            Grid.GetColumn(sidebar).Should().Be(0);
            sidebar.IsVisible.Should().BeTrue();
            window.IsFullScreen = false;
            Dispatcher.UIThread.RunJobs();
        }
        finally { window.Close(); }
    }
}
