using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class SettingsWindowTests
{
    [AvaloniaFact]
    public void Sidebar_editors_roundtrip_on_the_Subtitles_page_and_Close_discards_them()
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var model = main.CreateSettings(); var window = new SettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            window.FindControl<CheckBox>("SidebarLeftEditor")!.IsChecked = true;
            window.FindControl<CheckBox>("SidebarTextMaskEditor")!.IsChecked = true;
            window.FindControl<CheckBox>("SidebarShowOriginalTextEditor")!.IsChecked = true;
            window.FindControl<NumericUpDown>("SidebarSubPaddingEditor")!.Value = 12;
            window.FindControl<NumericUpDown>("SidebarFontSizeEditor")!.Value = 22;
            window.FindControl<ComboBox>("SidebarFontFamilyEditor")!.SelectedItem = "";
            window.FindControl<ComboBox>("SidebarFontWeightEditor")!.SelectedItem = "Bold";
            Dispatcher.UIThread.RunJobs();
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible, model.Error); Assert.Single(saved);
            Assert.True(prefs.SidebarLeft); Assert.True(prefs.SidebarTextMask); Assert.True(prefs.SidebarShowOriginalText);
            Assert.Equal(12, prefs.SidebarSubPadding); Assert.Equal(22, prefs.SidebarFontSize);
            Assert.Equal("", prefs.SidebarFontFamily); Assert.Equal("Bold", prefs.SidebarFontWeight);
            Assert.True(main.SidebarLeft); Assert.True(main.SidebarTextMask); Assert.Equal("", main.SidebarFontFamily);
            Assert.Equal(12, main.SidebarSubPadding);
        }
        finally { if (window.IsVisible) window.Close(); }

        // A fresh draft opens with the saved values; Close discards edits.
        var cancel = main.CreateSettings(); var cancelWindow = new SettingsWindow(cancel); cancelWindow.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            cancelWindow.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            cancelWindow.FindControl<CheckBox>("SidebarLeftEditor")!.IsChecked = false;
            cancelWindow.FindControl<NumericUpDown>("SidebarFontSizeEditor")!.Value = 30;
            Click(cancelWindow, cancelWindow.FindControl<Button>("CloseButton")!);
            Assert.False(cancelWindow.IsVisible); Assert.Single(saved);
            Assert.True(prefs.SidebarLeft); Assert.Equal(22, prefs.SidebarFontSize);
            Assert.True(main.SidebarLeft); Assert.Equal(22, main.SidebarFontSize);
        }
        finally { if (cancelWindow.IsVisible) cancelWindow.Close(); }
    }

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
        window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs(); Assert.True(window.FindControl<StackPanel>("SubtitlesPage")!.IsVisible);
        window.FindControl<CheckBox>("SidebarVisibleEditor")!.IsChecked = true;
        window.FindControl<NumericUpDown>("SidebarWidthEditor")!.Value = 420;
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
                        0 => new[] { "ThemeEditor" },
                        1 => new[]
                        {
                            "FontSizeEditor", "ScaleEditor", "SubtitlesVisibleEditor", "SidebarVisibleEditor", "SidebarWidthEditor",
                            "SidebarLeftEditor", "SidebarTextMaskEditor", "SidebarShowOriginalTextEditor", "SidebarSubPaddingEditor",
                            "SidebarFontFamilyEditor", "SidebarFontWeightEditor", "SidebarFontSizeEditor",
                        },
                        _ => new[]
                        {
                            "TranslationLink", "RecognitionLink", "ShortcutsLink", "ResetEngineKeysButton",
                            "WordClickActionEditor", "WordManagerLink", "AnkiDeckEditor",
                        }
                    })
                    {
                        var control = window.FindControl<Control>(name)!;
                        control.BringIntoView(); Dispatcher.UIThread.RunJobs(); AssertContained(control, window);
                        var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
                        Point controlPoint = control.TranslatePoint(default, scroll)!.Value;
                        Assert.True(controlPoint.Y >= -1 && controlPoint.Y + control.Bounds.Height <= scroll.Viewport.Height + 1,
                            name + " clipped by scroll viewport");
                        AssertContained(window.FindControl<Button>("SaveButton")!, window);
                        AssertContained(window.FindControl<Button>("CloseButton")!, window);
                        AssertContained(window.FindControl<ListBox>("SettingsNavigation")!, window);
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
    [InlineData(SettingsLink.Translation)] [InlineData(SettingsLink.Recognition)]
    [InlineData(SettingsLink.Shortcuts)] [InlineData(SettingsLink.WordManager)]
    public async Task Main_owned_singleton_modal_links_discard_then_route_after_cleanup(SettingsLink link)
    {
        var player = new RecognitionPlayer();
        var mainWindow = new MainWindow { Width = 640, Height = 420 }; var prefs = new AppPrefs(); int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, _ => writes++, new FakeTranslator(), mainWindow);
        main.TranslationDialog = () => new TranslationViewModel(new(), _ => Task.CompletedTask);
        string wordsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".words.json");
        var words = new WordLibrary(wordsPath); words.Load();
        main.WordLibrary = words;
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
            string name = link switch
            {
                SettingsLink.Translation => "TranslationLink",
                SettingsLink.Recognition => "RecognitionLink",
                SettingsLink.Shortcuts => "ShortcutsLink",
                SettingsLink.WordManager => "WordManagerLink",
                _ => throw new ArgumentOutOfRangeException(nameof(link)),
            };
            Button tool = hub.FindControl<Button>(name)!;
            tool.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Click(hub, tool);
            // Yield to the awaited ShowDialog continuation, then run the queued UI work.
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.False(hub.IsVisible); Assert.Equal(0, writes); Assert.Equal("Dark", prefs.Theme); Assert.Equal(34, main.SubtitleFontSize);
            var routed = Assert.Single(mainWindow.OwnedWindows);
            Assert.Same(mainWindow, routed.Owner);
            Assert.True(link switch
            {
                SettingsLink.Translation => routed is TranslationWindow,
                SettingsLink.Recognition => routed is RecognitionWindow,
                SettingsLink.Shortcuts => routed is CheatSheetWindow,
                SettingsLink.WordManager => routed is WordManagerWindow,
                _ => throw new ArgumentOutOfRangeException(nameof(link)),
            });
            routed.Close(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.True(mainWindow.IsVisible);
            main.ShowSettingsCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            var reopened = Assert.IsType<SettingsWindow>(Assert.Single(mainWindow.OwnedWindows));
            Assert.NotSame(hub, reopened); Assert.Equal("Dark", ((SettingsViewModel)reopened.DataContext!).Theme);
            reopened.Close(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        }
        finally { mainWindow.Close(); player.Recognition.Dispose(); }
    }

    [AvaloniaFact]
    public void Engine_key_editor_roundtrips_and_blocks_an_app_collision()
    {
        var (main, player, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var model = main.CreateSettings(); var window = new SettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            Assert.Equal(EngineKeyBindings.Defaults().Count, model.EngineKeyRows.Count);

            // The editor rows keep their Delete/Enabled controls reachable at the minimum window width.
            window.Width = 560; window.Height = 550; Dispatcher.UIThread.RunJobs();
            ItemsControl engineList = window.FindControl<ItemsControl>("EngineKeyRows")!;
            Control firstRow = engineList.GetRealizedContainers().First();
            firstRow.BringIntoView(); Dispatcher.UIThread.RunJobs();
            foreach (Button rowButton in firstRow.GetVisualDescendants().OfType<Button>().Where(b => b.Content as string == "Delete"))
            {
                Point origin = rowButton.TranslatePoint(default, window)!.Value;
                Assert.True(origin.X >= 0 && origin.Y >= 0 && origin.X + rowButton.Bounds.Width <= window.Bounds.Width + 1
                    && origin.Y + rowButton.Bounds.Height <= window.Bounds.Height + 1, $"engine row Delete must stay reachable at MinWidth: {origin}");
            }

            // Reset is non-vacuous: delete a row first, then restore it through the button.
            model.EngineKeyRows[0].DeleteCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            int afterDelete = model.EngineKeyRows.Count;
            Assert.Equal(EngineKeyBindings.Defaults().Count - 1, afterDelete);
            Button reset = window.FindControl<Button>("ResetEngineKeysButton")!;
            reset.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Click(window, reset);
            Assert.Equal(EngineKeyBindings.Defaults().Count, model.EngineKeyRows.Count);

            EngineKeyEditRow pause = model.EngineKeyRows.First(r => r.ActionName == "TogglePlayPause");
            pause.Chord = "Enter";
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible, model.Error); Assert.Single(saved);
            Assert.True(saved[0].EngineKeysCustomized);
            Assert.Contains(saved[0].EngineKeyBindings, b => b.Action == "TogglePlayPause" && b.Key == "Enter");
            Assert.Contains(player.CurrentEngineKeyBindings, b => b.Action.ToString() == "TogglePlayPause" && b.Key.ToString() == "Enter");
        }
        finally { if (window.IsVisible) window.Close(); }

        var colliding = main.CreateSettings(); var collidingWindow = new SettingsWindow(colliding); collidingWindow.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            collidingWindow.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            EngineKeyEditRow fullscreen = colliding.EngineKeyRows.First(r => r.ActionName == "ToggleFullScreen");
            fullscreen.Chord = "Ctrl+F"; // default ActivateSubsSearch app chord
            Click(collidingWindow, collidingWindow.FindControl<Button>("SaveButton")!);
            Assert.True(collidingWindow.IsVisible);
            Assert.Contains("collide", colliding.Error!);
            Assert.Single(saved);
        }
        finally { if (collidingWindow.IsVisible) collidingWindow.Close(); }
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
