using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class SettingsWindowTests
{
    [AvaloniaFact]
    public void Main_window_has_Settings_menu_and_gear()
    {
        using var vm = new MainWindowViewModel(new FakePlaybackController(), new AppPrefs(), _ => { }, new FakeTranslator(), new FakeShell());
        var window = new MainWindow { Width = 640, Height = 420 };
        window.Attach(vm, null); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.NotNull(window.FindControl<MenuItem>("SettingsMenu"));
            var gear = window.FindControl<Button>("SettingsButton")!;
            Assert.NotNull(gear); AssertContained(gear, window);
            var menu = window.FindControl<Menu>("MainMenu")!;
            Assert.Equal(8, menu.Items.Count);
            foreach (object? item in menu.Items)
            {
                var header = Assert.IsType<MenuItem>(item); AssertContained(header, window);
                Point headerPoint = header.TranslatePoint(default, menu)!.Value;
                Assert.True(headerPoint.Y + header.Bounds.Height <= menu.Bounds.Height + 1);
            }
            Capture(window, "main-minimum");
            var settings = window.FindControl<MenuItem>("SettingsMenu")!;
            var view = (MenuItem)settings.Parent!;
            view.IsSubMenuOpen = true; Dispatcher.UIThread.RunJobs();
            Assert.True(settings.IsEffectivelyVisible); Assert.True(settings.IsEnabled);
            Capture(window, "main-view-settings");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Bound_editors_Save_roundtrips_and_Close_Escape_discard()
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var model = main.CreateSettings(); var window = new SettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        window.FindControl<ComboBox>("ThemeEditor")!.SelectedItem = "Light";
        window.FindControl<CheckBox>("SidebarVisibleEditor")!.IsChecked = true;
        window.FindControl<NumericUpDown>("SidebarWidthEditor")!.Value = 420;
        window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs(); Assert.True(window.FindControl<StackPanel>("SubtitlesPage")!.IsVisible);
        window.FindControl<NumericUpDown>("FontSizeEditor")!.Value = 44;
        window.FindControl<NumericUpDown>("ScaleEditor")!.Value = 1.25m;
        window.FindControl<CheckBox>("SubtitlesVisibleEditor")!.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Click(window, window.FindControl<Button>("SaveButton")!);
        Assert.False(window.IsVisible); Assert.Single(saved);
        Assert.Equal("Light", prefs.Theme); Assert.True(prefs.SidebarVisible); Assert.Equal(420, prefs.SidebarWidth);
        Assert.Equal(44, prefs.SubtitleFontSize); Assert.Equal(1.25, prefs.SecondaryFontScale); Assert.False(prefs.SubtitlesVisible);
        foreach (bool escape in new[] { false, true })
        {
            model = main.CreateSettings(); model.Theme = "Dark"; model.SubtitleFontSize = 90;
            window = new SettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            if (escape) window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); else Click(window, window.FindControl<Button>("CloseButton")!);
            Dispatcher.UIThread.RunJobs(); Assert.False(window.IsVisible); Assert.Single(saved);
            Assert.Equal("Light", prefs.Theme); Assert.Equal(44, prefs.SubtitleFontSize);
        }
    }

    [AvaloniaFact]
    public void Failed_save_leaves_dialog_open_and_error_visible()
    {
        using var main = new MainWindowViewModel(new FakePlaybackController(), new AppPrefs(), _ => throw new IOException("Read-only destination"), new FakeTranslator(), new FakeShell());
        var draft = main.CreateSettings(); draft.Theme = "Light";
        var window = new SettingsWindow(draft); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.True(main.IsDarkTheme);
            var error = window.FindControl<TextBlock>("SaveError")!;
            Assert.Contains("Read-only destination", error.Text!); AssertContained(error, window);
            Capture(window, "settings-error");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Hub_renders_dark_light_normal_and_minimum_all_navigation_pages()
    {
        var (main, _, _, _, _) = TestVm.Create(); using var owned = main;
        var model = main.CreateSettings(); var window = new SettingsWindow(model); window.Show();
        try
        {
            foreach (string theme in new[] { "Dark", "Light" })
            foreach (bool small in new[] { false, true })
            {
                ThemeService.Apply(theme); window.Width = small ? 560 : 760; window.Height = small ? 440 : 550;
                foreach (int page in new[] { 0, 1, 2 })
                {
                    window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = page;
                    Dispatcher.UIThread.RunJobs();
                    AssertContained(window.FindControl<Button>("SaveButton")!, window);
                    AssertContained(window.FindControl<Button>("CloseButton")!, window);
                    AssertContained(window.FindControl<ListBox>("SettingsNavigation")!, window);
                    foreach (string name in page switch
                    {
                        0 => new[] { "ThemeEditor", "SidebarVisibleEditor", "SidebarWidthEditor" },
                        1 => new[] { "FontSizeEditor", "ScaleEditor", "SubtitlesVisibleEditor" },
                        _ => new[] { "TranslationLink", "RecognitionLink", "ShortcutsLink" }
                    })
                    {
                        var control = window.FindControl<Control>(name)!; AssertContained(control, window);
                        var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
                        Point controlPoint = control.TranslatePoint(default, scroll)!.Value;
                        Assert.True(controlPoint.Y + control.Bounds.Height <= scroll.Bounds.Height + 1, name + " clipped by scroll viewport");
                    }
                    // Let the existing 120 ms theme brush transition settle before visual evidence.
                    await Task.Delay(160); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs();
                    Capture(window, $"settings-{theme.ToLowerInvariant()}-{(small ? "minimum" : "normal")}-{page}");
                }
            }
        }
        finally { window.Close(); ThemeService.Apply("Dark"); }
    }

    [AvaloniaTheory]
    [InlineData(SettingsLink.Translation)] [InlineData(SettingsLink.Recognition)] [InlineData(SettingsLink.Shortcuts)]
    public async Task Main_owned_singleton_modal_links_discard_then_route_after_cleanup(SettingsLink link)
    {
        var player = new RecognitionPlayer();
        var mainWindow = new MainWindow { Width = 640, Height = 420 }; var prefs = new AppPrefs(); int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, _ => writes++, new FakeTranslator(), mainWindow);
        main.TranslationDialog = () => new TranslationViewModel(new(), _ => Task.CompletedTask);
        mainWindow.Attach(main, null); mainWindow.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Click(mainWindow, mainWindow.FindControl<Button>("SettingsButton")!); Dispatcher.UIThread.RunJobs();
            var hub = Assert.IsType<SettingsWindow>(Assert.Single(mainWindow.OwnedWindows));
            Assert.Same(mainWindow, hub.Owner); Assert.True(hub.IsDialog);
            main.ShowSettingsCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            Assert.Same(hub, Assert.Single(mainWindow.OwnedWindows));
            var draft = (SettingsViewModel)hub.DataContext!; draft.Theme = "Light"; draft.SubtitleFontSize = 90;
            draft.SelectedSection = 2; Dispatcher.UIThread.RunJobs();
            string name = link switch { SettingsLink.Translation => "TranslationLink", SettingsLink.Recognition => "RecognitionLink", _ => "ShortcutsLink" };
            Click(hub, hub.FindControl<Button>(name)!);
            // Yield to the awaited ShowDialog continuation, then run the queued UI work.
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.False(hub.IsVisible); Assert.Equal(0, writes); Assert.Equal("Dark", prefs.Theme); Assert.Equal(34, main.SubtitleFontSize);
            var routed = Assert.Single(mainWindow.OwnedWindows);
            Assert.Same(mainWindow, routed.Owner);
            Assert.True(link switch { SettingsLink.Translation => routed is TranslationWindow, SettingsLink.Recognition => routed is RecognitionWindow, _ => routed is CheatSheetWindow });
            routed.Close(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.True(mainWindow.IsVisible);
            main.ShowSettingsCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            var reopened = Assert.IsType<SettingsWindow>(Assert.Single(mainWindow.OwnedWindows));
            Assert.NotSame(hub, reopened); Assert.Equal("Dark", ((SettingsViewModel)reopened.DataContext!).Theme);
            reopened.Close(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        }
        finally { mainWindow.Close(); player.Recognition.Dispose(); }
    }

    static void Click(Window window, Control control)
    {
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
    static void AssertContained(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible, control.Name);
        Point point = control.TranslatePoint(default, window)!.Value;
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, control.Name);
        Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.Bounds.Width + 1 && point.Y + control.Bounds.Height <= window.Bounds.Height + 1,
            $"{control.Name}: {point} / {control.Bounds.Size} in {window.Bounds.Size}");
    }
    static void Capture(Window window, string name)
    {
        string? dir = Environment.GetEnvironmentVariable("LLPLAYER_SETTINGS_ARTIFACTS");
        if (dir == null) return; Directory.CreateDirectory(dir);
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    sealed class RecognitionPlayer : FakePlaybackController, IRecognitionController
    {
        public event Action<string, string?, string?>? RecognitionError { add { } remove { } }
        public RecognitionActions Recognition { get; } = new(new Backend());
        public RecognitionSettingsStore? RecognitionStore { get; } = new(Path.Combine(Path.GetTempPath(), "settings-test-unused-recognition.json"));
        public string? RecognitionWarning => null;
        public RecognitionSettings RecognitionSettings { get; } = new();
        public string RecognitionStatus => "Recognition idle";
        public Task StartRecognitionAsync(int slot) => Task.CompletedTask;
        public Task CancelRecognitionAsync(int slot) => Task.CompletedTask;
        public Task OcrRecognitionAsync(int slot) => Task.CompletedTask;
        public Task ToggleRecognitionPauseAsync() => Task.CompletedTask;
    }
    sealed class Backend : IRecognitionBackend
    {
        public bool HasAudio => false; public bool IsLive => false; public bool IsBusy => false; public bool IsPaused => false;
        public void Start(int slot) { } public void Pause() { } public void Resume() { } public void Cancel(int slot) { }
        public void Ocr(int slot) { } public void Apply(RecognitionSettings settings) { }
    }
}
