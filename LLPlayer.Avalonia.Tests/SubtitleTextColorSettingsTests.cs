using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class SubtitleTextColorSettingsTests
{
    const string Primary = "SubtitleTextColor";
    const string Secondary = "SecondarySubtitleTextColor";

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Volume\":42,\"Theme\":\"Light\",\"RecentFiles\":[\"old.mp4\"]}")]
    public void Legacy_preferences_restore_the_original_independent_overlay_colors(string json)
    {
        var prefs = JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();
        using var main = NewMain(prefs);
        AssertColors(prefs, "#FFFFFF", "#E8E8E8");
        AssertLive(main, "#FFFFFF", "#E8E8E8");
    }

    [Theory]
    [InlineData(" #a1b2c3 ", "\t#d4E5f6\r\n", "#A1B2C3", "#D4E5F6")]
    [InlineData("#000000", "#FFFFFF", "#000000", "#FFFFFF")]
    [InlineData(null, "#123456", "#FFFFFF", "#123456")]
    [InlineData("#654321", null, "#654321", "#E8E8E8")]
    [InlineData("", "#abcdef", "#FFFFFF", "#ABCDEF")]
    [InlineData("#123456", "   ", "#123456", "#E8E8E8")]
    [InlineData("#FFF", "#123456", "#FFFFFF", "#123456")]
    [InlineData("#123456", "#FFFF", "#123456", "#E8E8E8")]
    [InlineData("#FF123456", "#abcdef", "#FFFFFF", "#ABCDEF")]
    [InlineData("#123456", "red", "#123456", "#E8E8E8")]
    [InlineData("#１２３４５６", "#123456", "#FFFFFF", "#123456")]
    [InlineData("#123456", "#١٢٣٤٥٦", "#123456", "#E8E8E8")]
    [InlineData("123456", "#12GG56", "#FFFFFF", "#E8E8E8")]
    public void Startup_normalizes_or_defaults_each_color_without_replacing_the_other_slot(
        string? primary, string? secondary, string expectedPrimary, string expectedSecondary)
    {
        var prefs = JsonSerializer.Deserialize<AppPrefs>(ColorsJson(primary, secondary), PrefsStore.JsonOptions)!.Normalize();
        using var main = NewMain(prefs);
        AssertColors(prefs, expectedPrimary, expectedSecondary);
        AssertLive(main, expectedPrimary, expectedSecondary);
    }

    [Fact]
    public void Canonical_color_roundtrip_preserves_unrelated_preferences()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = JsonSerializer.Deserialize<AppPrefs>(ColorsJson(" #aBcD09 ", "\t#012efA\n"), PrefsStore.JsonOptions)!;
        prefs.Volume = 77; prefs.Mute = true; prefs.Theme = "Light";
        prefs.SubtitleFontSize = 48; prefs.SecondaryFontScale = 1.25; prefs.SubtitleBottomOffset = 56;
        prefs.SubtitleOutlineThickness = 7.25; prefs.SecondarySubtitleOutlineThickness = 0;
        prefs.SubtitlesVisible = false; prefs.SidebarVisible = true; prefs.SidebarWidth = 460;
        prefs.LastFolder = "/media"; prefs.RecentFiles = ["one.mp4", "two.mp4"];
        prefs.WordTranslationService = "Off"; prefs.TranslateTargetLanguage = "Russian";
        store.Save(prefs.Normalize());

        var loaded = store.Load();
        Assert.Null(store.LoadWarning);
        AssertColors(loaded, "#ABCD09", "#012EFA");
        Assert.Equal(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions), JsonSerializer.Serialize(loaded, PrefsStore.JsonOptions));
        using var restarted = NewMain(loaded);
        AssertLive(restarted, "#ABCD09", "#012EFA");
    }

    [AvaloniaFact]
    public void Bound_editors_preview_independently_and_Save_persists_a_fresh_candidate_before_changing_the_overlay()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        File.WriteAllText(store.Path, ColorsJson("#102030", "#405060"));
        var prefs = store.Load();
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary", SecondaryText = "Secondary" };
        var window = new MainWindow { Width = 1000, Height = 700 };
        MainWindowViewModel? live = null; int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate =>
        {
            AssertLive(live!, "#102030", "#405060");
            AssertColors(prefs, "#102030", "#405060");
            AssertRendered(window, "#102030", "#405060");
            Assert.NotSame(prefs, candidate); Assert.NotSame(prefs.RecentFiles, candidate.RecentFiles);
            AssertColors(candidate, "#12AB34", "#C056EF");
            store.Save(candidate); writes++;
        }, new FakeTranslator(), window);
        live = main;
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var settings = ShowSettings(draft);
        try
        {
            AssertRendered(window, "#102030", "#405060");
            Assert.Equal("#102030", Editor(settings, 0).Text);
            Assert.Equal("#405060", Editor(settings, 1).Text);
            AssertSwatches(settings, "#102030", "#405060");

            SetText(Editor(settings, 0), " #12ab34 ");
            Assert.Equal(" #12ab34 ", ReadString(draft, Primary));
            Assert.Equal("#405060", ReadString(draft, Secondary));
            AssertSwatches(settings, "#12AB34", "#405060");
            SetText(Editor(settings, 1), "\t#c056ef ");
            Assert.Equal(" #12ab34 ", ReadString(draft, Primary));
            Assert.Equal("\t#c056ef ", ReadString(draft, Secondary));
            AssertSwatches(settings, "#12AB34", "#C056EF");
            AssertLive(main, "#102030", "#405060");
            AssertColors(prefs, "#102030", "#405060");
            AssertRendered(window, "#102030", "#405060");

            prefs.Volume = 77; prefs.Mute = true; prefs.LastFolder = "/new"; prefs.RecentFiles.Add("new.mp4");
            prefs.WordTranslationService = "Off"; prefs.TranslateTargetLanguage = "Russian";
            Click(settings, settings.FindControl<Button>("SaveButton")!);
            Assert.False(settings.IsVisible); Assert.Null(draft.Error); Assert.Equal(1, writes);
            AssertLive(main, "#12AB34", "#C056EF");
            AssertColors(prefs, "#12AB34", "#C056EF");
            AssertRendered(window, "#12AB34", "#C056EF");
            var saved = store.Load();
            AssertColors(saved, "#12AB34", "#C056EF");
            Assert.Equal(77, saved.Volume); Assert.True(saved.Mute); Assert.Equal("/new", saved.LastFolder);
            Assert.Equal(["new.mp4"], saved.RecentFiles); Assert.Equal("Off", saved.WordTranslationService);
            Assert.Equal("Russian", saved.TranslateTargetLanguage);
            Assert.Equal(34, main.SubtitleFontSize); Assert.Equal(24, main.SubtitleBottomOffset);
            Assert.Equal(3, main.SubtitleOutlineThickness); Assert.Equal(2.5, main.SecondarySubtitleOutlineThickness);
            using var restarted = NewMain(saved);
            AssertLive(restarted, "#12AB34", "#C056EF");
        }
        finally { settings.Close(); window.ShutdownRequested = () => Task.CompletedTask; window.Close(); }
    }

    public static IEnumerable<object[]> InvalidEditorColors()
    {
        string[] invalid = ["", "   ", "#FFF", "#FFFF", "#FFFFFFFF", "123456", "#12345", "#1234567", "red",
            "#12GG56", "#１２３４５６", "#١٢٣٤٥٦", "#123 56", "##123456", "\u200B#123456", "#123456\njunk"];
        foreach (int slot in new[] { 0, 1 })
            foreach (string text in invalid)
                yield return [slot, text];
    }

    [AvaloniaTheory]
    [MemberData(nameof(InvalidEditorColors))]
    public void Invalid_color_text_blocks_the_whole_draft_without_writing_or_applying(int slot, string text)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs);
        string beforeFile = File.ReadAllText(store.Path), beforePrefs = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        int writes = 0;
        var player = new FakePlaybackController();
        using var main = new MainWindowViewModel(player, prefs, _ => writes++, new FakeTranslator(), new FakeShell());
        var draft = main.CreateSettings(); draft.SelectedSection = 1; draft.Theme = "Light"; draft.SubtitlesVisible = false;
        var window = ShowSettings(draft);
        try
        {
            SetText(Editor(window, 1 - slot), "#ABCDEF");
            SetText(Editor(window, slot), text);
            Assert.Equal(text, ReadString(draft, slot == 0 ? Primary : Secondary));
            AssertSwatches(window, slot == 0 ? "#FFFFFF" : "#ABCDEF", slot == 1 ? "#E8E8E8" : "#ABCDEF");
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Equal(0, writes);
            Assert.False(string.IsNullOrWhiteSpace(window.FindControl<TextBlock>("SaveError")!.Text));
            Assert.Equal(beforeFile, File.ReadAllText(store.Path));
            Assert.Equal(beforePrefs, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            AssertLive(main, "#FFFFFF", "#E8E8E8");
            Assert.True(main.IsDarkTheme); Assert.True(player.SubtitlesVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_and_Escape_discard_both_colors_and_reopening_restores_the_saved_values(bool escape)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        File.WriteAllText(store.Path, ColorsJson("#112233", "#445566"));
        var prefs = store.Load(); string before = File.ReadAllText(store.Path); int writes = 0;
        using var main = NewMain(prefs, _ => writes++);
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var window = ShowSettings(draft);
        try
        {
            SetText(Editor(window, 0), "#AABBCC"); SetText(Editor(window, 1), "#DDEEFF");
            if (escape) window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            else Click(window, window.FindControl<Button>("CloseButton")!);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible); Assert.Equal(0, writes); Assert.Equal(before, File.ReadAllText(store.Path));
            AssertLive(main, "#112233", "#445566"); AssertColors(prefs, "#112233", "#445566");
            var reopened = main.CreateSettings(); reopened.SelectedSection = 1;
            var second = ShowSettings(reopened);
            try
            {
                Assert.Equal("#112233", Editor(second, 0).Text); Assert.Equal("#445566", Editor(second, 1).Text);
                AssertSwatches(second, "#112233", "#445566");
            }
            finally { second.Close(); }
            using var restarted = NewMain(store.Load());
            AssertLive(restarted, "#112233", "#445566");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_real_failed_write_keeps_both_colors_and_other_live_state_unchanged_until_retry_succeeds()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs);
        string beforeFile = File.ReadAllText(store.Path), beforePrefs = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        var player = new FakePlaybackController(); var shell = new FakeShell(); int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate => { writes++; store.Save(candidate); }, new FakeTranslator(), shell);
        var draft = main.CreateSettings(); draft.SelectedSection = 1; draft.Theme = "Light"; draft.SubtitlesVisible = false;
        var window = ShowSettings(draft);
        // A directory at the atomic store's temp-file path causes a real write failure on either OS.
        Directory.CreateDirectory(store.Path + ".tmp");
        try
        {
            SetText(Editor(window, 0), "#00AA44"); SetText(Editor(window, 1), "#BB55EE");
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Equal(1, writes);
            Assert.False(string.IsNullOrWhiteSpace(window.FindControl<TextBlock>("SaveError")!.Text));
            Assert.Equal(beforeFile, File.ReadAllText(store.Path)); Assert.Equal(beforePrefs, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            AssertLive(main, "#FFFFFF", "#E8E8E8"); Assert.True(main.IsDarkTheme); Assert.True(player.SubtitlesVisible); Assert.Empty(shell.Themes);
            Assert.Equal("#00AA44", Editor(window, 0).Text); Assert.Equal("#BB55EE", Editor(window, 1).Text);
            AssertSwatches(window, "#00AA44", "#BB55EE");
            Directory.Delete(store.Path + ".tmp");
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Equal(2, writes);
            AssertLive(main, "#00AA44", "#BB55EE"); AssertColors(prefs, "#00AA44", "#BB55EE");
            AssertColors(store.Load(), "#00AA44", "#BB55EE");
            Assert.False(main.IsDarkTheme); Assert.False(player.SubtitlesVisible); Assert.Equal(["Light"], shell.Themes);
            using var restarted = NewMain(store.Load());
            AssertLive(restarted, "#00AA44", "#BB55EE");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Both_color_editors_are_named_scrollable_and_keyboard_editable_at_the_minimum_window_size()
    {
        var (main, _, _, _, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var window = new SettingsWindow(draft) { Width = 560, Height = 440 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            for (int slot = 0; slot < 2; slot++)
            {
                var editor = Editor(window, slot);
                string? name = AutomationProperties.GetName(editor);
                Assert.Contains(slot == 0 ? "primary" : "secondary", name ?? "", StringComparison.OrdinalIgnoreCase);
                Assert.Contains("color", name ?? "", StringComparison.OrdinalIgnoreCase);
                editor.BringIntoView(); Dispatcher.UIThread.RunJobs();
                Assert.True(editor.IsEffectivelyVisible);
                Point point = editor.TranslatePoint(default, scroll)!.Value;
                Assert.True(editor.Bounds.Width > 0 && editor.Bounds.Height > 0);
                Assert.True(point.Y >= -1 && point.Y + editor.Bounds.Height <= scroll.Bounds.Height + 1);
                Assert.True(editor.Focus(NavigationMethod.Tab)); editor.SelectAll(); Dispatcher.UIThread.RunJobs();
                window.KeyTextInput(slot == 0 ? "#123ABC" : "#DEF456"); Dispatcher.UIThread.RunJobs();
                Assert.True(editor.IsFocused); Assert.Equal(slot == 0 ? "#123ABC" : "#DEF456", editor.Text);
                AssertLive(main, "#FFFFFF", "#E8E8E8");
            }
            foreach (string name in new[] { "CloseButton", "SaveButton" })
            {
                var button = window.FindControl<Button>(name)!;
                Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEnabled);
                Point point = button.TranslatePoint(default, window)!.Value;
                Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
                Assert.True(point.X >= 0 && point.Y >= 0 && point.X + button.Bounds.Width <= window.Bounds.Width + 1
                    && point.Y + button.Bounds.Height <= window.Bounds.Height + 1, name);
            }
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Single(saved);
            AssertLive(main, "#123ABC", "#DEF456");
        }
        finally { window.Close(); }
    }

    static MainWindowViewModel NewMain(AppPrefs prefs, Action<AppPrefs>? save = null)
        => new(new FakePlaybackController(), prefs, save ?? (_ => { }), new FakeTranslator(), new FakeShell());

    static SettingsWindow ShowSettings(SettingsViewModel draft)
    {
        var window = new SettingsWindow(draft) { Height = 850 }; window.Show(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    static string ColorsJson(string? primary, string? secondary)
        => JsonSerializer.Serialize(new Dictionary<string, string?> { [Primary] = primary, [Secondary] = secondary });

    static void AssertColors(AppPrefs prefs, string primary, string secondary)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
        Assert.True(json.RootElement.TryGetProperty(Primary, out var first), "Persisted preferences must contain " + Primary + ".");
        Assert.True(json.RootElement.TryGetProperty(Secondary, out var second), "Persisted preferences must contain " + Secondary + ".");
        Assert.Equal(primary, first.GetString()); Assert.Equal(secondary, second.GetString());
    }

    static void AssertLive(MainWindowViewModel main, string primary, string secondary)
    {
        AssertBrush(Read(main, "SubtitleTextBrush"), primary);
        AssertBrush(Read(main, "SecondarySubtitleTextBrush"), secondary);
    }

    // Reflection keeps the tests compilable against the pre-feature baseline for intentional RED.
    static object? Read(object instance, string name)
    {
        PropertyInfo? property = instance.GetType().GetProperty(name);
        Assert.NotNull(property);
        return property.GetValue(instance);
    }

    static string ReadString(object instance, string name) => Assert.IsType<string>(Read(instance, name));

    static void AssertBrush(object? brush, string expected)
        => Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);

    static void AssertRendered(MainWindow window, string primary, string secondary)
    {
        AssertBrush(window.FindControl<SubtitleText>("PrimarySubtitle")!.Fill, primary);
        AssertBrush(window.FindControl<SubtitleText>("SecondarySubtitle")!.Fill, secondary);
    }

    static void AssertSwatches(SettingsWindow window, string primary, string secondary)
    {
        AssertBrush(Assert.IsType<Border>(window.FindControl<Control>("PrimaryTextColorSwatch")).Background, primary);
        AssertBrush(Assert.IsType<Border>(window.FindControl<Control>("SecondaryTextColorSwatch")).Background, secondary);
    }

    static TextBox Editor(SettingsWindow window, int slot)
        => Assert.IsType<TextBox>(window.FindControl<Control>(slot == 0 ? "PrimaryTextColorEditor" : "SecondaryTextColorEditor"));

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
