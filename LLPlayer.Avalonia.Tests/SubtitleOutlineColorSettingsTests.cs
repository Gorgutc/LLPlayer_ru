using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class SubtitleOutlineColorSettingsTests
{
    const string OutlineColor = "SubtitleOutlineColor";

    [AvaloniaTheory]
    [InlineData("{}")]
    [InlineData("{\"Volume\":42,\"Theme\":\"Light\",\"SubtitleTextColor\":\"#123456\",\"SecondarySubtitleTextColor\":\"#ABCDEF\"}")]
    public void Legacy_preferences_restore_the_common_black_outline_without_changing_foreground_colors(string json)
    {
        var prefs = JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();
        string primary = prefs.SubtitleTextColor, secondary = prefs.SecondarySubtitleTextColor;
        using var main = NewMain(prefs);
        AssertColor(prefs, "#000000"); AssertLive(main, "#000000");
        Assert.Equal(primary, prefs.SubtitleTextColor); Assert.Equal(secondary, prefs.SecondarySubtitleTextColor);
        AssertBrush(main.SubtitleTextBrush, primary); AssertBrush(main.SecondarySubtitleTextBrush, secondary);
        Assert.Equal(3, main.SubtitleOutlineThickness); Assert.Equal(2.5, main.SecondarySubtitleOutlineThickness);
    }

    [AvaloniaTheory]
    [InlineData(" #a1b2c3 ", "#A1B2C3")]
    [InlineData("\t#d4E5f6\r\n", "#D4E5F6")]
    [InlineData("#000000", "#000000")]
    [InlineData("#FFFFFF", "#FFFFFF")]
    [InlineData(null, "#000000")]
    [InlineData("", "#000000")]
    [InlineData("   ", "#000000")]
    [InlineData("#FFF", "#000000")]
    [InlineData("#FFFF", "#000000")]
    [InlineData("#FF123456", "#000000")]
    [InlineData("red", "#000000")]
    [InlineData("123456", "#000000")]
    [InlineData("#12GG56", "#000000")]
    [InlineData("#１２３４５６", "#000000")]
    [InlineData("#١٢٣٤٥٦", "#000000")]
    [InlineData("\u200B#123456", "#000000")]
    public void Startup_normalizes_valid_RGB_or_defaults_invalid_saved_strings_without_a_corrupt_file_warning(string? raw, string expected)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        string json = ColorJson(raw); File.WriteAllText(store.Path, json);
        var prefs = store.Load();
        Assert.Null(store.LoadWarning); Assert.False(File.Exists(store.Path + ".bad"));
        Assert.Equal(json, File.ReadAllText(store.Path)); AssertColor(prefs, expected);
        using var main = NewMain(prefs); AssertLive(main, expected);
    }

    [Theory]
    [InlineData("{\"Volume\":42,\"SubtitleOutlineColor\":42}")]
    [InlineData("{\"Volume\":42,\"SubtitleOutlineColor\":true}")]
    [InlineData("{\"Volume\":42,\"SubtitleOutlineColor\":[]}")]
    [InlineData("{\"Volume\":42,\"SubtitleOutlineColor\":{}}")]
    [InlineData("{ not json")]
    [InlineData("{\"Volume\":42,\"SubtitleOutlineColor\":\"#123456\"")]
    public void Wrong_JSON_types_and_malformed_files_keep_the_existing_whole_file_failure_behavior(string json)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        File.WriteAllText(store.Path, json);
        var prefs = store.Load();
        Assert.NotNull(store.LoadWarning); Assert.Equal(100, prefs.Volume);
        Assert.Equal(json, File.ReadAllText(store.Path)); Assert.Equal(json, File.ReadAllText(store.Path + ".bad"));
        AssertColor(prefs, "#000000");
    }

    [AvaloniaFact]
    public void Canonical_outline_roundtrip_preserves_both_foregrounds_independent_thicknesses_and_unrelated_preferences()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = JsonSerializer.Deserialize<AppPrefs>(ColorJson(" #aBcD09 "), PrefsStore.JsonOptions)!;
        prefs.Volume = 77; prefs.Mute = true; prefs.Theme = "Light";
        prefs.SubtitleFontSize = 48; prefs.SecondaryFontScale = 1.25; prefs.SubtitleBottomOffset = 56;
        prefs.SubtitleOutlineThickness = 7.25; prefs.SecondarySubtitleOutlineThickness = 0;
        prefs.SubtitleTextColor = "#123456"; prefs.SecondarySubtitleTextColor = "#FEDCBA";
        prefs.SubtitlesVisible = false; prefs.SidebarVisible = true; prefs.SidebarWidth = 460;
        prefs.LastFolder = "/media"; prefs.RecentFiles = ["one.mp4", "two.mp4"];
        prefs.WordTranslationService = "Off"; prefs.TranslateTargetLanguage = "Russian";
        store.Save(prefs.Normalize());

        var loaded = store.Load();
        Assert.Null(store.LoadWarning); AssertColor(loaded, "#ABCD09");
        Assert.Equal(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions), JsonSerializer.Serialize(loaded, PrefsStore.JsonOptions));
        using var restarted = NewMain(loaded); AssertLive(restarted, "#ABCD09");
        AssertBrush(restarted.SubtitleTextBrush, "#123456"); AssertBrush(restarted.SecondarySubtitleTextBrush, "#FEDCBA");
        Assert.Equal(7.25, restarted.SubtitleOutlineThickness); Assert.Equal(0, restarted.SecondarySubtitleOutlineThickness);
    }

    [AvaloniaFact]
    public void One_bound_editor_previews_a_detached_color_and_Save_merges_all_twelve_owned_fields_before_applying_both_overlays()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        File.WriteAllText(store.Path, ColorJson("#102030"));
        var prefs = store.Load();
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary", SecondaryText = "Secondary" };
        var window = new MainWindow { Width = 1000, Height = 700 };
        MainWindowViewModel? live = null; int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate =>
        {
            AssertLive(live!, "#102030"); AssertColor(prefs, "#102030"); AssertRendered(window, "#102030");
            Assert.Equal("Dark", prefs.Theme); Assert.True(live!.IsDarkTheme); Assert.True(player.SubtitlesVisible);
            Assert.Equal(34, live.SubtitleFontSize); Assert.Equal(24, live.SubtitleBottomOffset);
            Assert.Equal(3, live.SubtitleOutlineThickness); Assert.Equal(2.5, live.SecondarySubtitleOutlineThickness);
            AssertBrush(live.SubtitleTextBrush, "#FFFFFF"); AssertBrush(live.SecondarySubtitleTextBrush, "#E8E8E8");
            Assert.False(live.SidebarVisible); Assert.Equal(360, live.SidebarWidth);
            Assert.NotSame(prefs, candidate); Assert.NotSame(prefs.RecentFiles, candidate.RecentFiles);
            AssertAllTwelveSaved(candidate);
            store.Save(candidate); writes++;
        }, new FakeTranslator(), window);
        live = main;
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var settings = ShowSettings(draft);
        try
        {
            Assert.Equal("#102030", Editor(settings).Text); AssertSwatch(settings, "#102030");
            SetText(Editor(settings), " #12ab34 ");
            Assert.Equal(" #12ab34 ", ReadString(draft, OutlineColor)); AssertSwatch(settings, "#12AB34");
            AssertLive(main, "#102030"); AssertColor(prefs, "#102030"); AssertRendered(window, "#102030");
            draft.Theme = "Light"; draft.SubtitleFontSize = 42; draft.SecondaryFontScale = 1.2;
            draft.SubtitleBottomOffset = 64; draft.SubtitleOutlineThickness = 7.25; draft.SecondarySubtitleOutlineThickness = 0;
            draft.SubtitleTextColor = "#AABBCC"; draft.SecondarySubtitleTextColor = "#DDEEFF";
            draft.SubtitlesVisible = false; draft.SidebarVisible = true; draft.SidebarWidth = 460;
            prefs.Volume = 77; prefs.Mute = true; prefs.LastFolder = "/new"; prefs.RecentFiles.Add("new.mp4");
            prefs.WordTranslationService = "Off"; prefs.TranslateTargetLanguage = "Russian";
            Click(settings, settings.FindControl<Button>("SaveButton")!);

            Assert.False(settings.IsVisible); Assert.Null(draft.Error); Assert.Equal(1, writes);
            AssertAllTwelveSaved(prefs); AssertLive(main, "#12AB34"); AssertRendered(window, "#12AB34");
            Assert.Equal(42, main.SubtitleFontSize); Assert.Equal(50.4, main.SecondaryFontSize);
            Assert.Equal(64, main.SubtitleBottomOffset); Assert.False(main.IsDarkTheme);
            Assert.Equal(7.25, main.SubtitleOutlineThickness); Assert.Equal(0, main.SecondarySubtitleOutlineThickness);
            AssertBrush(main.SubtitleTextBrush, "#AABBCC"); AssertBrush(main.SecondarySubtitleTextBrush, "#DDEEFF");
            Assert.False(player.SubtitlesVisible); Assert.True(main.SidebarVisible); Assert.Equal(460, main.SidebarWidth);
            var saved = store.Load(); AssertAllTwelveSaved(saved);
            Assert.Equal(77, saved.Volume); Assert.True(saved.Mute); Assert.Equal("/new", saved.LastFolder);
            Assert.Equal(["new.mp4"], saved.RecentFiles); Assert.Equal("Off", saved.WordTranslationService);
            Assert.Equal("Russian", saved.TranslateTargetLanguage);
            using var restarted = NewMain(saved); AssertLive(restarted, "#12AB34");
            Assert.Equal(7.25, restarted.SubtitleOutlineThickness); Assert.Equal(0, restarted.SecondarySubtitleOutlineThickness);
            AssertBrush(restarted.SubtitleTextBrush, "#AABBCC"); AssertBrush(restarted.SecondarySubtitleTextBrush, "#DDEEFF");
        }
        finally { settings.Close(); window.ShutdownRequested = () => Task.CompletedTask; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#FFF")]
    [InlineData("#FFFF")]
    [InlineData("#FFFFFFFF")]
    [InlineData("123456")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("red")]
    [InlineData("#12GG56")]
    [InlineData("#１２３４５６")]
    [InlineData("#١٢٣٤٥٦")]
    [InlineData("#123 56")]
    [InlineData("##123456")]
    [InlineData("\u200B#123456")]
    [InlineData("#123456\njunk")]
    public void Invalid_raw_outline_color_blocks_the_entire_draft_and_shows_an_error_without_writing_or_applying(string text)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs);
        string beforeFile = File.ReadAllText(store.Path), beforePrefs = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        int writes = 0; var player = new FakePlaybackController();
        using var main = new MainWindowViewModel(player, prefs, _ => writes++, new FakeTranslator(), new FakeShell());
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        draft.Theme = "Light"; draft.SubtitlesVisible = false; draft.SubtitleTextColor = "#ABCDEF";
        draft.SecondarySubtitleTextColor = "#123456"; draft.SubtitleOutlineThickness = 0; draft.SecondarySubtitleOutlineThickness = 8;
        var window = ShowSettings(draft);
        try
        {
            SetText(Editor(window), text); Assert.Equal(text, ReadString(draft, OutlineColor)); AssertSwatch(window, "#000000");
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Equal(0, writes);
            Assert.False(string.IsNullOrWhiteSpace(window.FindControl<TextBlock>("SaveError")!.Text));
            Assert.Equal(text, Editor(window).Text); Assert.Equal(beforeFile, File.ReadAllText(store.Path));
            Assert.Equal(beforePrefs, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            AssertLive(main, "#000000"); Assert.True(main.IsDarkTheme); Assert.True(player.SubtitlesVisible);
            Assert.Equal(3, main.SubtitleOutlineThickness); Assert.Equal(2.5, main.SecondarySubtitleOutlineThickness);
            AssertBrush(main.SubtitleTextBrush, "#FFFFFF"); AssertBrush(main.SecondarySubtitleTextBrush, "#E8E8E8");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_and_Escape_discard_outline_color_and_reopening_and_restart_restore_the_saved_value(bool escape)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        File.WriteAllText(store.Path, ColorJson("#112233"));
        var prefs = store.Load(); string before = File.ReadAllText(store.Path); int writes = 0;
        using var main = NewMain(prefs, _ => writes++);
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var window = ShowSettings(draft);
        try
        {
            SetText(Editor(window), "#AABBCC"); AssertSwatch(window, "#AABBCC");
            if (escape) window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            else Click(window, window.FindControl<Button>("CloseButton")!);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible); Assert.Equal(0, writes); Assert.Equal(before, File.ReadAllText(store.Path));
            AssertLive(main, "#112233"); AssertColor(prefs, "#112233");
            var reopened = main.CreateSettings(); reopened.SelectedSection = 1;
            var second = ShowSettings(reopened);
            try { Assert.Equal("#112233", Editor(second).Text); AssertSwatch(second, "#112233"); }
            finally { second.Close(); }
            using var restarted = NewMain(store.Load()); AssertLive(restarted, "#112233");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_real_failed_write_preserves_live_outline_foregrounds_thicknesses_and_file_until_retry_succeeds()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs);
        string beforeFile = File.ReadAllText(store.Path), beforePrefs = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        var player = new FakePlaybackController(); var shell = new FakeShell(); int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate => { writes++; store.Save(candidate); }, new FakeTranslator(), shell);
        var draft = main.CreateSettings(); draft.SelectedSection = 1; draft.Theme = "Light"; draft.SubtitlesVisible = false;
        draft.SubtitleOutlineThickness = 0; draft.SecondarySubtitleOutlineThickness = 7.5;
        draft.SubtitleTextColor = "#ABCDEF"; draft.SecondarySubtitleTextColor = "#123456";
        var window = ShowSettings(draft);
        // Occupying the atomic temp-file path with a directory causes a real write failure on either OS.
        Directory.CreateDirectory(store.Path + ".tmp");
        try
        {
            SetText(Editor(window), "#00AA44");
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Equal(1, writes);
            Assert.False(string.IsNullOrWhiteSpace(window.FindControl<TextBlock>("SaveError")!.Text));
            Assert.Equal(beforeFile, File.ReadAllText(store.Path)); Assert.Equal(beforePrefs, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            AssertLive(main, "#000000"); Assert.True(main.IsDarkTheme); Assert.True(player.SubtitlesVisible); Assert.Empty(shell.Themes);
            Assert.Equal(3, main.SubtitleOutlineThickness); Assert.Equal(2.5, main.SecondarySubtitleOutlineThickness);
            AssertBrush(main.SubtitleTextBrush, "#FFFFFF"); AssertBrush(main.SecondarySubtitleTextBrush, "#E8E8E8");
            Assert.Equal("#00AA44", Editor(window).Text); AssertSwatch(window, "#00AA44");
            Directory.Delete(store.Path + ".tmp");
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Equal(2, writes);
            AssertLive(main, "#00AA44"); AssertColor(prefs, "#00AA44"); AssertColor(store.Load(), "#00AA44");
            Assert.Equal(0, main.SubtitleOutlineThickness); Assert.Equal(7.5, main.SecondarySubtitleOutlineThickness);
            AssertBrush(main.SubtitleTextBrush, "#ABCDEF"); AssertBrush(main.SecondarySubtitleTextBrush, "#123456");
            Assert.False(main.IsDarkTheme); Assert.False(player.SubtitlesVisible); Assert.Equal(["Light"], shell.Themes);
            using var restarted = NewMain(store.Load()); AssertLive(restarted, "#00AA44");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Common_outline_editor_and_swatch_fit_the_minimum_window_and_keep_the_invalid_error_and_footer_reachable(string theme)
    {
        var (main, _, _, _, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var window = new SettingsWindow(draft)
        {
            Width = 560, Height = 440, RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var editor = Editor(window);
            string? name = AutomationProperties.GetName(editor);
            Assert.Contains("outline", name ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Contains("color", name ?? "", StringComparison.OrdinalIgnoreCase);
            editor.BringIntoView(); Dispatcher.UIThread.RunJobs(); Assert.True(editor.IsEffectivelyVisible);
            Point point = editor.TranslatePoint(default, scroll)!.Value;
            Assert.True(editor.Bounds.Width > 0 && editor.Bounds.Height > 0);
            Assert.True(point.Y >= -1 && point.Y + editor.Bounds.Height <= scroll.Bounds.Height + 1);
            var swatch = Assert.IsType<Border>(window.FindControl<Control>("SubtitleOutlineColorSwatch"));
            Point swatchPoint = swatch.TranslatePoint(default, scroll)!.Value;
            Assert.True(swatch.IsEffectivelyVisible); Assert.True(swatch.Bounds.Width > 0 && swatch.Bounds.Height > 0);
            Assert.True(swatchPoint.X >= -1 && swatchPoint.Y >= -1
                && swatchPoint.X + swatch.Bounds.Width <= scroll.Bounds.Width + 1
                && swatchPoint.Y + swatch.Bounds.Height <= scroll.Bounds.Height + 1);
            Assert.True(editor.Focus(NavigationMethod.Tab)); editor.SelectAll(); Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("#invalid"); Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsFocused); Assert.Equal("#invalid", editor.Text);
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Empty(saved);
            var error = window.FindControl<TextBlock>("SaveError")!;
            Assert.True(error.IsEffectivelyVisible); Assert.False(string.IsNullOrWhiteSpace(error.Text));
            AssertWithinWindow(window, error);
            foreach (string buttonName in new[] { "CloseButton", "SaveButton" })
            {
                var button = window.FindControl<Button>(buttonName)!;
                Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEnabled); AssertWithinWindow(window, button);
            }
            editor.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Assert.True(editor.Focus(NavigationMethod.Tab)); editor.SelectAll(); Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("#123ABC"); Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsFocused); Assert.Equal("#123ABC", editor.Text); AssertSwatch(window, "#123ABC");
            AssertLive(main, "#000000");
            foreach (string buttonName in new[] { "CloseButton", "SaveButton" })
            {
                var button = window.FindControl<Button>(buttonName)!;
                Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEnabled);
                AssertWithinWindow(window, button);
            }
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Single(saved); AssertLive(main, "#123ABC");
        }
        finally { window.Close(); }
    }

    static void AssertAllTwelveSaved(AppPrefs prefs)
    {
        Assert.Equal("Light", prefs.Theme); Assert.Equal(42, prefs.SubtitleFontSize); Assert.Equal(1.2, prefs.SecondaryFontScale);
        Assert.Equal(64, prefs.SubtitleBottomOffset); Assert.Equal(7.25, prefs.SubtitleOutlineThickness);
        Assert.Equal(0, prefs.SecondarySubtitleOutlineThickness); Assert.Equal("#AABBCC", prefs.SubtitleTextColor);
        Assert.Equal("#DDEEFF", prefs.SecondarySubtitleTextColor); AssertColor(prefs, "#12AB34");
        Assert.False(prefs.SubtitlesVisible); Assert.True(prefs.SidebarVisible); Assert.Equal(460, prefs.SidebarWidth);
    }

    static void AssertWithinWindow(Window window, Control control)
    {
        Point point = control.TranslatePoint(default, window)!.Value;
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.Bounds.Width + 1
            && point.Y + control.Bounds.Height <= window.Bounds.Height + 1, control.Name);
    }

    static MainWindowViewModel NewMain(AppPrefs prefs, Action<AppPrefs>? save = null)
        => new(new FakePlaybackController(), prefs, save ?? (_ => { }), new FakeTranslator(), new FakeShell());

    static SettingsWindow ShowSettings(SettingsViewModel draft)
    {
        var window = new SettingsWindow(draft) { Height = 1000 }; window.Show(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    static string ColorJson(string? value) => JsonSerializer.Serialize(new Dictionary<string, string?> { [OutlineColor] = value });

    static void AssertColor(AppPrefs prefs, string expected)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
        Assert.True(json.RootElement.TryGetProperty(OutlineColor, out var color), "Persisted preferences must contain " + OutlineColor + ".");
        Assert.Equal(expected, color.GetString());
    }

    static void AssertLive(MainWindowViewModel main, string expected) => AssertBrush(Read(main, "SubtitleOutlineBrush"), expected);

    // Reflection keeps these tests compilable against the pre-feature baseline for intentional RED.
    static object? Read(object instance, string name)
    {
        PropertyInfo? property = instance.GetType().GetProperty(name);
        Assert.NotNull(property);
        return property.GetValue(instance);
    }

    static string ReadString(object instance, string name) => Assert.IsType<string>(Read(instance, name));

    static void AssertBrush(object? brush, string expected)
        => Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);

    static void AssertRendered(MainWindow window, string expected)
    {
        AssertBrush(window.FindControl<SubtitleText>("PrimarySubtitle")!.Outline, expected);
        AssertBrush(window.FindControl<SubtitleText>("SecondarySubtitle")!.Outline, expected);
    }

    static void AssertSwatch(SettingsWindow window, string expected)
        => AssertBrush(Assert.IsType<Border>(window.FindControl<Control>("SubtitleOutlineColorSwatch")).Background, expected);

    static TextBox Editor(SettingsWindow window) => Assert.IsType<TextBox>(window.FindControl<Control>("SubtitleOutlineColorEditor"));

    static void SetText(TextBox editor, string text)
    {
        editor.Text = text; Dispatcher.UIThread.RunJobs(); Assert.Equal(text, editor.Text);
    }

    static void Click(Window window, Control control)
    {
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
}
