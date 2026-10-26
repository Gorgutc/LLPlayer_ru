using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class SubtitleOutlineSettingsTests
{
    const string Primary = "SubtitleOutlineThickness";
    const string Secondary = "SecondarySubtitleOutlineThickness";

    [AvaloniaTheory]
    [InlineData("{}")]
    [InlineData("{\"Volume\":42,\"Theme\":\"Light\",\"RecentFiles\":[\"old.mp4\"]}")]
    public void Legacy_preferences_restore_independent_primary_and_secondary_outline_defaults(string json)
    {
        var prefs = JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();
        using var main = NewMain(prefs);
        AssertOutlines(prefs, 3, 2.5);
        AssertLive(main, 3, 2.5);
    }

    [AvaloniaTheory]
    [InlineData("-1", "9", 0, 8)]
    [InlineData("9", "-1", 8, 0)]
    [InlineData("0", "8", 0, 8)]
    [InlineData("1.25", "6.75", 1.25, 6.75)]
    [InlineData("\"NaN\"", "1.5", 3, 1.5)]
    [InlineData("1.25", "\"Infinity\"", 1.25, 2.5)]
    [InlineData("\"-Infinity\"", "\"NaN\"", 3, 2.5)]
    public void Startup_normalizes_each_outline_without_replacing_the_other_slot(string primary, string secondary, double expectedPrimary, double expectedSecondary)
    {
        var options = new JsonSerializerOptions(PrefsStore.JsonOptions) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var prefs = JsonSerializer.Deserialize<AppPrefs>(OutlineJson(primary, secondary), options)!.Normalize();
        using var main = NewMain(prefs);
        AssertOutlines(prefs, expectedPrimary, expectedSecondary);
        AssertLive(main, expectedPrimary, expectedSecondary);
    }

    [AvaloniaTheory]
    [InlineData("0", "0", 0, 0)]
    [InlineData("0", "2.5", 0, 2.5)]
    [InlineData("3", "0", 3, 0)]
    public void Loaded_zero_has_explicit_editor_text_and_can_be_saved_unchanged(string primary, string secondary, double expectedPrimary, double expectedSecondary)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        File.WriteAllText(store.Path, OutlineJson(primary, secondary));
        var prefs = store.Load();
        using var main = NewMain(prefs, store.Save);
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var window = ShowSettings(draft);
        try
        {
            Assert.False(string.IsNullOrWhiteSpace(Read<string>(draft, Primary + "Text")));
            Assert.False(string.IsNullOrWhiteSpace(Read<string>(draft, Secondary + "Text")));
            Assert.Equal((decimal)expectedPrimary, Editor(window, 0).Value);
            Assert.Equal((decimal)expectedSecondary, Editor(window, 1).Value);
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(draft.Error);
            AssertOutlines(store.Load(), expectedPrimary, expectedSecondary);
            using var restarted = NewMain(store.Load());
            AssertLive(restarted, expectedPrimary, expectedSecondary);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    public void Real_editors_keep_their_captured_culture_and_persist_a_fresh_detached_candidate_before_applying(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            using TempDir temp = new();
            var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
            var prefs = new AppPrefs().Normalize(); store.Save(prefs);
            var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary", SecondaryText = "Secondary" };
            var window = new MainWindow { Width = 1000, Height = 700 };
            MainWindowViewModel? live = null; int writes = 0;
            using var main = new MainWindowViewModel(player, prefs, candidate =>
            {
                AssertLive(live!, 3, 2.5); AssertOutlines(prefs, 3, 2.5);
                AssertRendered(window, 3, 2.5);
                Assert.NotSame(prefs, candidate); Assert.NotSame(prefs.RecentFiles, candidate.RecentFiles);
                AssertOutlines(candidate, 4.25, 1.75);
                store.Save(candidate); writes++;
            }, new FakeTranslator(), window);
            live = main;
            window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
            var draft = main.CreateSettings(); draft.SelectedSection = 1;
            var settings = ShowSettings(draft);
            try
            {
                AssertRendered(window, 3, 2.5);
                SetText(Editor(settings, 0), 4.25m.ToString(CultureInfo.CurrentCulture));
                SetText(Editor(settings, 1), 1.75m.ToString(CultureInfo.CurrentCulture));
                AssertLive(main, 3, 2.5); AssertOutlines(prefs, 3, 2.5); AssertRendered(window, 3, 2.5);
                prefs.Volume = 77; prefs.Mute = true; prefs.LastFolder = "/new"; prefs.RecentFiles.Add("new.mp4");
                prefs.WordTranslationService = "Off"; prefs.TranslateTargetLanguage = "Russian";
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture == "ru-RU" ? "en-US" : "ru-RU");
                Click(settings, settings.FindControl<Button>("SaveButton")!);
                Assert.False(settings.IsVisible); Assert.Null(draft.Error); Assert.Equal(1, writes);
                AssertLive(main, 4.25, 1.75); AssertOutlines(prefs, 4.25, 1.75); AssertRendered(window, 4.25, 1.75);
                var saved = store.Load();
                AssertOutlines(saved, 4.25, 1.75);
                Assert.Equal(77, saved.Volume); Assert.True(saved.Mute); Assert.Equal("/new", saved.LastFolder);
                Assert.Equal(["new.mp4"], saved.RecentFiles); Assert.Equal("Off", saved.WordTranslationService);
                Assert.Equal("Russian", saved.TranslateTargetLanguage);
                Assert.Equal(24, main.SubtitleBottomOffset); Assert.Equal(34, main.SubtitleFontSize);
                using var restarted = NewMain(saved);
                AssertLive(restarted, 4.25, 1.75);
            }
            finally { settings.Close(); window.ShutdownRequested = () => Task.CompletedTask; window.Close(); }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [AvaloniaTheory]
    [InlineData(0, "")]
    [InlineData(1, "abc")]
    [InlineData(0, "-0.5")]
    [InlineData(1, "8.01")]
    [InlineData(0, "NaN")]
    [InlineData(1, "Infinity")]
    [InlineData(0, "-Infinity")]
    [InlineData(1, "1e9999")]
    public void Invalid_outline_text_blocks_the_whole_draft_without_writing_or_applying(int slot, string text)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs);
        string beforeFile = File.ReadAllText(store.Path), beforePrefs = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        int writes = 0;
        using var main = NewMain(prefs, _ => writes++);
        var draft = main.CreateSettings(); draft.SelectedSection = 1; draft.Theme = "Light";
        var window = ShowSettings(draft);
        try
        {
            SetText(Editor(window, 1 - slot), "6");
            SetText(Editor(window, slot), text);
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Equal(0, writes);
            Assert.False(string.IsNullOrWhiteSpace(window.FindControl<TextBlock>("SaveError")!.Text));
            Assert.Equal(beforeFile, File.ReadAllText(store.Path));
            Assert.Equal(beforePrefs, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            AssertLive(main, 3, 2.5); Assert.True(main.IsDarkTheme);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("0", "8", 0, 8)]
    [InlineData("8", "0", 8, 0)]
    public void Both_outline_editors_accept_the_inclusive_range_boundaries(string primary, string secondary, double expectedPrimary, double expectedSecondary)
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var window = ShowSettings(draft);
        try
        {
            SetText(Editor(window, 0), primary); SetText(Editor(window, 1), secondary);
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Single(saved);
            AssertLive(main, expectedPrimary, expectedSecondary);
            AssertOutlines(prefs, expectedPrimary, expectedSecondary); AssertOutlines(saved[0], expectedPrimary, expectedSecondary);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_and_Escape_discard_both_outlines_and_reopening_restores_the_saved_values(bool escape)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        File.WriteAllText(store.Path, OutlineJson("1", "6"));
        var prefs = store.Load(); string before = File.ReadAllText(store.Path); int writes = 0;
        using var main = NewMain(prefs, _ => writes++);
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var window = ShowSettings(draft);
        try
        {
            SetText(Editor(window, 0), "7"); SetText(Editor(window, 1), "0");
            if (escape) window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            else Click(window, window.FindControl<Button>("CloseButton")!);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible); Assert.Equal(0, writes); Assert.Equal(before, File.ReadAllText(store.Path));
            AssertLive(main, 1, 6); AssertOutlines(prefs, 1, 6);
            var reopened = main.CreateSettings(); reopened.SelectedSection = 1;
            var second = ShowSettings(reopened);
            try { Assert.Equal(1m, Editor(second, 0).Value); Assert.Equal(6m, Editor(second, 1).Value); }
            finally { second.Close(); }
            using var restarted = NewMain(store.Load());
            AssertLive(restarted, 1, 6);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_real_failed_write_keeps_both_outlines_and_other_live_state_unchanged_until_retry_succeeds()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs);
        string beforeFile = File.ReadAllText(store.Path), beforePrefs = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        var player = new FakePlaybackController(); var shell = new FakeShell(); int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate => { writes++; store.Save(candidate); }, new FakeTranslator(), shell);
        var draft = main.CreateSettings(); draft.SelectedSection = 1; draft.Theme = "Light"; draft.SubtitlesVisible = false;
        var window = ShowSettings(draft);
        // A directory occupying the store's temp-file path gives a real, cross-platform write failure.
        Directory.CreateDirectory(store.Path + ".tmp");
        try
        {
            SetText(Editor(window, 0), "7"); SetText(Editor(window, 1), "1");
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Equal(1, writes);
            Assert.Equal(beforeFile, File.ReadAllText(store.Path)); Assert.Equal(beforePrefs, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            AssertLive(main, 3, 2.5); Assert.True(main.IsDarkTheme); Assert.True(player.SubtitlesVisible); Assert.Empty(shell.Themes);
            Assert.Equal(7m, Editor(window, 0).Value); Assert.Equal(1m, Editor(window, 1).Value);
            Directory.Delete(store.Path + ".tmp");
            Click(window, window.FindControl<Button>("SaveButton")!);
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Equal(2, writes);
            AssertLive(main, 7, 1); AssertOutlines(prefs, 7, 1); AssertOutlines(store.Load(), 7, 1);
            Assert.False(main.IsDarkTheme); Assert.False(player.SubtitlesVisible); Assert.Equal(["Light"], shell.Themes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Both_outline_editors_are_named_scrollable_and_keyboard_editable_at_the_minimum_window_size()
    {
        var (main, _, _, _, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var window = new SettingsWindow(draft) { Width = 560, Height = 440 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
            for (int slot = 0; slot < 2; slot++)
            {
                var editor = Editor(window, slot);
                string? name = AutomationProperties.GetName(editor);
                Assert.Contains(slot == 0 ? "primary" : "secondary", name ?? "", StringComparison.OrdinalIgnoreCase);
                Assert.Contains("outline", name ?? "", StringComparison.OrdinalIgnoreCase);
                editor.BringIntoView(); Dispatcher.UIThread.RunJobs();
                Assert.True(scroll.Offset.Y > 0); Assert.True(editor.IsEffectivelyVisible);
                Point point = editor.TranslatePoint(default, scroll)!.Value;
                Assert.True(editor.Bounds.Width > 0 && editor.Bounds.Height > 0);
                Assert.True(point.Y >= -1 && point.Y + editor.Bounds.Height <= scroll.Bounds.Height + 1);
                var textBox = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>());
                Assert.True(textBox.Focus(NavigationMethod.Tab)); textBox.SelectAll(); Dispatcher.UIThread.RunJobs();
                window.KeyTextInput(slot == 0 ? "5" : "1"); Dispatcher.UIThread.RunJobs();
                Assert.True(textBox.IsFocused); Assert.Equal(slot == 0 ? "5" : "1", editor.Text);
                AssertLive(main, 3, 2.5);
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
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Single(saved); AssertLive(main, 5, 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Live_outline_changes_remeasure_wrapped_text_and_keep_word_hit_testing_aligned()
    {
        var subtitle = new SubtitleText
        {
            Text = "alpha beta gamma delta epsilon zeta eta theta", Slot = 1, FontSize = 30,
            OutlineThickness = 0, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = new Window { Width = 210, Height = 600, Content = subtitle };
        WordClickedEventArgs? clicked = null; subtitle.WordClicked += (_, args) => clicked = args;
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            double plainHeight = subtitle.Bounds.Height;
            Assert.True(plainHeight > 60);
            subtitle.OutlineThickness = 8; Dispatcher.UIThread.RunJobs();
            double outlinedHeight = subtitle.Bounds.Height;
            Assert.True(outlinedHeight >= plainHeight + 16, $"Expected live outline padding: {plainHeight} -> {outlinedHeight}.");
            Point lastWord = FindWord(subtitle, "theta");
            Assert.True(lastWord.Y > subtitle.Bounds.Height / 2);
            Point point = subtitle.TranslatePoint(lastWord, window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(clicked); Assert.Equal("theta", clicked.Word); Assert.Equal(1, clicked.Slot);
            Assert.False(subtitle.TryGetWordAt(new Point(0, 0), out _));
            window.Width = 500; Dispatcher.UIThread.RunJobs();
            Assert.True(subtitle.Bounds.Height < outlinedHeight); FindWord(subtitle, "theta");
            window.Width = 210; subtitle.OutlineThickness = 0; Dispatcher.UIThread.RunJobs();
            Assert.Equal(plainHeight, subtitle.Bounds.Height); FindWord(subtitle, "alpha"); FindWord(subtitle, "theta");
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

    static string OutlineJson(string primary, string secondary)
        => "{\"" + Primary + "\":" + primary + ",\"" + Secondary + "\":" + secondary + "}";

    static void AssertOutlines(AppPrefs prefs, double primary, double secondary)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
        Assert.True(json.RootElement.TryGetProperty(Primary, out var first), "Persisted preferences must contain " + Primary + ".");
        Assert.True(json.RootElement.TryGetProperty(Secondary, out var second), "Persisted preferences must contain " + Secondary + ".");
        Assert.Equal(primary, first.GetDouble()); Assert.Equal(secondary, second.GetDouble());
    }

    static void AssertLive(MainWindowViewModel main, double primary, double secondary)
    {
        Assert.Equal(primary, Read<double>(main, Primary)); Assert.Equal(secondary, Read<double>(main, Secondary));
    }

    // Reflection lets the tests compile against the pre-feature baseline and fail on missing behavior during RED.
    static T Read<T>(object instance, string name)
    {
        PropertyInfo? property = instance.GetType().GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsType<T>(property.GetValue(instance));
    }

    static void AssertRendered(MainWindow window, double primary, double secondary)
    {
        Assert.Equal(primary, window.FindControl<SubtitleText>("PrimarySubtitle")!.OutlineThickness);
        Assert.Equal(secondary, window.FindControl<SubtitleText>("SecondarySubtitle")!.OutlineThickness);
    }

    static NumericUpDown Editor(SettingsWindow window, int slot)
        => Assert.IsType<NumericUpDown>(window.FindControl<Control>(slot == 0 ? "PrimaryOutlineEditor" : "SecondaryOutlineEditor"));

    static void SetText(NumericUpDown editor, string text)
    {
        var textBox = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>());
        textBox.Text = text; Dispatcher.UIThread.RunJobs(); Assert.Equal(text, editor.Text);
    }

    static void Click(Window window, Control control)
    {
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }

    static Point FindWord(SubtitleText subtitle, string expected)
    {
        for (double y = 1; y < subtitle.Bounds.Height; y += 2)
            for (double x = 1; x < subtitle.Bounds.Width; x += 2)
                if (subtitle.TryGetWordAt(new Point(x, y), out string word) && word == expected)
                    return new Point(x, y);
        Assert.Fail($"'{expected}' must remain clickable in the laid-out subtitle ({subtitle.Bounds.Size}).");
        return default;
    }
}
