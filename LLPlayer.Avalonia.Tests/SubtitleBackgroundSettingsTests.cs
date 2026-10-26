using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ICommand = System.Windows.Input.ICommand;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class SubtitleBackgroundSettingsTests
{
    const string ColorField = "SubtitleBackgroundColor";
    const string PrimaryField = "SubtitleBackgroundOpacityPercent";
    const string SecondaryField = "SecondarySubtitleBackgroundOpacityPercent";
    static readonly string[] PercentFields = [PrimaryField, SecondaryField];

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Volume\":42,\"Theme\":\"Light\",\"SubtitleFontSize\":48,\"SecondaryFontScale\":1.2,\"SubtitleTextColor\":\"#123456\",\"SubtitleOutlineColor\":\"#ABCDEF\",\"SecondarySubtitleInheritsFont\":false}")]
    public void Legacy_preferences_add_transparent_black_defaults_without_changing_existing_values(string json)
    {
        var prefs = JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();
        AssertBackground(prefs, "#000000", 0, 0);
        using var before = JsonDocument.Parse(json);
        using var after = JsonDocument.Parse(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
        foreach (var property in before.RootElement.EnumerateObject())
            Assert.Equal(property.Value.GetRawText(), after.RootElement.GetProperty(property.Name).GetRawText());
        foreach (string field in PercentFields)
        {
            Assert.Equal(typeof(int), RequiredProperty(typeof(AppPrefs), field).PropertyType);
            Assert.Equal(JsonValueKind.Number, after.RootElement.GetProperty(field).ValueKind);
            Assert.Equal(0, after.RootElement.GetProperty(field).GetInt32());
        }
    }

    public static IEnumerable<object[]> SavedPercentCases()
    {
        foreach (string field in PercentFields)
        foreach ((int raw, int expected) in new[] { (int.MinValue, 0), (-1, 0), (0, 0), (1, 1), (47, 47), (100, 100), (101, 100), (int.MaxValue, 100) })
            yield return [field, raw, expected];
    }

    [Theory]
    [MemberData(nameof(SavedPercentCases))]
    public void Loaded_Int32_percentages_clamp_each_field_independently_without_rewriting_the_file(string field, int raw, int expected)
    {
        using TempDir temp = new(); var store = NewStore(temp);
        var values = new Dictionary<string, object?> { [ColorField] = "#123456", [PrimaryField] = 23, [SecondaryField] = 71, ["Volume"] = 42 };
        values[field] = raw; string json = JsonSerializer.Serialize(values); File.WriteAllText(store.Path, json);
        var prefs = store.Load();
        Assert.Null(store.LoadWarning); Assert.False(File.Exists(store.Path + ".bad"));
        Assert.Equal(json, File.ReadAllText(store.Path)); Assert.Equal(42, prefs.Volume);
        AssertBackground(prefs, "#123456", field == PrimaryField ? expected : 23, field == SecondaryField ? expected : 71);
        AssertBackground(prefs.Normalize(), "#123456", field == PrimaryField ? expected : 23, field == SecondaryField ? expected : 71);
    }

    public static IEnumerable<object?[]> SavedColorCases()
    {
        foreach ((string? raw, string expected) in new (string?, string)[]
        {
            (null, "#000000"), ("", "#000000"), (" ", "#000000"), (" black ", "#000000"),
            ("#ABC", "#000000"), ("#00112233", "#000000"), ("#GG0011", "#000000"),
            ("#１２３４５６", "#000000"), ("#12\n3456", "#000000"), ("123456", "#000000"),
            (" #a1b2c3 \t", "#A1B2C3"), ("#000000", "#000000"), ("#FFFFFF", "#FFFFFF")
        }) yield return [raw, expected];
    }

    [Theory]
    [MemberData(nameof(SavedColorCases))]
    public void Stored_color_strings_normalize_only_their_field_and_keep_the_existing_file(string? raw, string expected)
    {
        using TempDir temp = new(); var store = NewStore(temp);
        string json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [ColorField] = raw, [PrimaryField] = 23, [SecondaryField] = 71,
            ["SubtitleTextColor"] = "#123456", ["SubtitleOutlineColor"] = "#ABCDEF", ["Volume"] = 42
        });
        File.WriteAllText(store.Path, json); var prefs = store.Load();
        Assert.Null(store.LoadWarning); Assert.False(File.Exists(store.Path + ".bad")); Assert.Equal(json, File.ReadAllText(store.Path));
        Assert.Equal(42, prefs.Volume); Assert.Equal("#123456", prefs.SubtitleTextColor); Assert.Equal("#ABCDEF", prefs.SubtitleOutlineColor);
        AssertBackground(prefs, expected, 23, 71); AssertBackground(prefs.Normalize(), expected, 23, 71);
    }

    public static IEnumerable<object[]> WrongJsonCases()
    {
        foreach (string field in PercentFields)
        foreach (string raw in new[] { "null", "true", "\"50\"", "[]", "{}", "1.5", "50.0", "1e1", "2147483648", "-2147483649", "99999999999999999999999" })
            yield return ["{\"Volume\":42,\"" + field + "\":" + raw + "}"];
        foreach (string raw in new[] { "42", "true", "[]", "{}" })
            yield return ["{\"Volume\":42,\"" + ColorField + "\":" + raw + "}"];
        yield return ["{ not json"];
        yield return ["{\"Volume\":42,\"SubtitleBackgroundColor\":\"#123456\""];
    }

    [Theory]
    [MemberData(nameof(WrongJsonCases))]
    public void Wrong_types_fractional_values_null_and_overflow_keep_whole_file_bad_copy_defaults_and_warning_policy(string json)
    {
        using TempDir temp = new(); var store = NewStore(temp); File.WriteAllText(store.Path, json);
        var prefs = store.Load();
        Assert.NotNull(store.LoadWarning); Assert.Equal(100, prefs.Volume);
        Assert.Equal(json, File.ReadAllText(store.Path)); Assert.Equal(json, File.ReadAllText(store.Path + ".bad"));
        AssertBackground(prefs, "#000000", 0, 0);
    }

    [AvaloniaFact]
    public void Bound_editors_update_two_detached_previews_and_opaque_swatch_independently_of_font_inheritance()
    {
        using TempDir temp = new(); var store = NewStore(temp); var prefs = SavedBackground(); store.Save(prefs);
        string fileBefore = File.ReadAllText(store.Path); int writes = 0;
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary", SecondaryText = "Secondary" };
        var window = new MainWindow { Width = 1100, Height = 700 };
        using var main = new MainWindowViewModel(player, prefs, _ => writes++, new FakeTranslator(), window);
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        var draft = main.CreateSettings(); var settings = ShowSettings(draft);
        try
        {
            AssertBackground(draft, "#123456", 35, 70);
            Assert.Equal(typeof(int), RequiredProperty(typeof(SettingsViewModel), PrimaryField).PropertyType);
            Assert.Equal(typeof(int), RequiredProperty(typeof(SettingsViewModel), SecondaryField).PropertyType);
            Assert.Equal("35", Text(draft, PrimaryField + "Text")); Assert.Equal("70", Text(draft, SecondaryField + "Text"));
            Assert.Equal("#123456", Control<TextBox>(settings, "SubtitleBackgroundColorEditor").Text);
            Assert.Equal("35", Control<TextBox>(settings, "PrimaryBackgroundOpacityEditor").Text);
            Assert.Equal("70", Control<TextBox>(settings, "SecondaryBackgroundOpacityEditor").Text);
            AssertActual(window, "#123456", 35, 70);
            Control<TextBox>(settings, "SubtitleBackgroundColorEditor").Text = " #a1b2c3 ";
            Control<TextBox>(settings, "PrimaryBackgroundOpacityEditor").Text = "25";
            Control<TextBox>(settings, "SecondaryBackgroundOpacityEditor").Text = "80";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(" #a1b2c3 ", Text(draft, ColorField));
            AssertPreview(settings, "#A1B2C3", 25, 80);
            AssertBrush(Read(draft, "SubtitleBackgroundColorBrush"), "#A1B2C3", 100);
            AssertBrush(Control<Border>(settings, "SubtitleBackgroundColorSwatch").Background, "#A1B2C3", 100);
            foreach (bool inherits in new[] { false, true, false })
            {
                Control<CheckBox>(settings, "SecondarySubtitleInheritsFontEditor").IsChecked = inherits;
                Dispatcher.UIThread.RunJobs();
                AssertPreview(settings, "#A1B2C3", 25, 80);
                Assert.True(Control<TextBox>(settings, "SecondaryBackgroundOpacityEditor").IsEffectivelyEnabled);
            }
            AssertLive(main, "#123456", 35, 70); AssertActual(window, "#123456", 35, 70);
            Assert.Equal(0, writes); Assert.Equal(fileBefore, File.ReadAllText(store.Path)); AssertBackground(prefs, "#123456", 35, 70);
        }
        finally { settings.Close(); CloseMain(window); }
    }

    public static IEnumerable<object[]> InvalidDraftCases()
    {
        foreach (string field in PercentFields)
        foreach (string raw in new[] { "", " ", "1.5", "1,5", "1e1", "1,000", "50%", "NaN", "Infinity", "-1", "101", "2147483648", "99999999999999999999999" })
            yield return [field + "Text", raw];
        foreach (string raw in new[] { "", "black", "#123", "#80123456", "#12345G", "123456", "#１２３４５６" })
            yield return [ColorField, raw];
    }

    [AvaloniaTheory]
    [MemberData(nameof(InvalidDraftCases))]
    public void Invalid_raw_background_input_blocks_the_entire_draft_and_retains_the_raw_text(string field, string raw)
    {
        using TempDir temp = new(); var store = NewStore(temp); var prefs = SavedBackground(); store.Save(prefs);
        string fileBefore = File.ReadAllText(store.Path), prefsBefore = Json(prefs); int writes = 0;
        using var main = NewMain(prefs, _ => writes++); var draft = main.CreateSettings(); SetAllTwentyTwo(draft);
        Set(draft, field, raw); bool closed = false; draft.CloseRequested += () => closed = true;
        draft.SaveCommand.Execute(null);
        Assert.False(closed); Assert.False(string.IsNullOrWhiteSpace(draft.Error)); Assert.Equal(0, writes);
        Assert.Equal(raw, Text(draft, field)); Assert.Equal(fileBefore, File.ReadAllText(store.Path)); Assert.Equal(prefsBefore, Json(prefs));
        AssertLive(main, "#123456", 35, 70); Assert.True(main.IsDarkTheme); Assert.Equal(34, main.SubtitleFontSize);
    }

    [AvaloniaFact]
    public void Bound_opacity_textboxes_retain_invalid_raw_values_and_the_same_open_draft_can_be_corrected()
    {
        using TempDir temp = new(); var store = NewStore(temp); var prefs = SavedBackground(); store.Save(prefs);
        string before = File.ReadAllText(store.Path); int writes = 0;
        using var main = NewMain(prefs, candidate => { store.Save(candidate); writes++; });
        var draft = main.CreateSettings(); var window = ShowSettings(draft);
        try
        {
            draft.Theme = "Light";
            foreach ((string name, string field) in new[] { ("PrimaryBackgroundOpacityEditor", PrimaryField), ("SecondaryBackgroundOpacityEditor", SecondaryField) })
            {
                var editor = Control<TextBox>(window, name);
                foreach (string raw in new[] { "1.5", "101", "", "-1" })
                {
                    editor.Text = raw; Dispatcher.UIThread.RunJobs(); Click(window, Button(window, "SaveButton"));
                    Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Equal(raw, editor.Text); Assert.Equal(raw, Text(draft, field + "Text"));
                    Assert.Equal(0, writes); Assert.Equal(before, File.ReadAllText(store.Path)); Assert.True(main.IsDarkTheme); AssertLive(main, "#123456", 35, 70);
                }
                editor.Text = field == PrimaryField ? "25" : "80"; Dispatcher.UIThread.RunJobs();
            }
            Click(window, Button(window, "SaveButton"));
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Equal(1, writes); Assert.False(main.IsDarkTheme);
            AssertBackground(store.Load(), "#123456", 25, 80); AssertLive(main, "#123456", 25, 80);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en-US", "0", "100", 0, 100)]
    [InlineData("ru-RU", "100", "0", 100, 0)]
    [InlineData("en-US", " 37 ", " 81 ", 37, 81)]
    [InlineData("ru-RU", " 37 ", " 81 ", 37, 81)]
    public void Valid_integer_raw_values_and_hex_are_canonicalized_and_persisted_in_both_cultures(string culture, string primary, string secondary, int expectedPrimary, int expectedSecondary)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using TempDir temp = new(); var store = NewStore(temp); var prefs = new AppPrefs().Normalize();
            using var main = NewMain(prefs, store.Save); var draft = main.CreateSettings();
            Set(draft, ColorField, " #a1b2c3 "); Set(draft, PrimaryField + "Text", primary); Set(draft, SecondaryField + "Text", secondary);
            draft.SaveCommand.Execute(null);
            Assert.Null(draft.Error); AssertBackground(draft, "#A1B2C3", expectedPrimary, expectedSecondary);
            Assert.Equal(expectedPrimary.ToString(CultureInfo.CurrentCulture), Text(draft, PrimaryField + "Text"));
            Assert.Equal(expectedSecondary.ToString(CultureInfo.CurrentCulture), Text(draft, SecondaryField + "Text"));
            AssertBackground(store.Load(), "#A1B2C3", expectedPrimary, expectedSecondary); AssertLive(main, "#A1B2C3", expectedPrimary, expectedSecondary);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [AvaloniaFact]
    public void Save_merges_all_twenty_two_owned_fields_from_fresh_preferences_and_persists_before_live_application_and_restart()
    {
        using TempDir temp = new(); var store = NewStore(temp); var prefs = SavedBackground(); store.Save(prefs);
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary", SecondaryText = "Secondary" };
        var window = new MainWindow { Width = 1100, Height = 700 }; MainWindowViewModel? live = null; int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate =>
        {
            AssertBackground(prefs, "#123456", 35, 70); AssertLive(live!, "#123456", 35, 70); AssertActual(window, "#123456", 35, 70);
            Assert.Equal(34, live!.SubtitleFontSize); Assert.True(live.IsDarkTheme); Assert.True(player.SubtitlesVisible);
            Assert.Equal(3, live.SubtitleOutlineThickness); Assert.Equal(2.5, live.SecondarySubtitleOutlineThickness); Assert.False(live.SidebarVisible);
            Assert.NotSame(prefs, candidate); Assert.NotSame(prefs.RecentFiles, candidate.RecentFiles);
            AssertTwentyTwo(candidate); AssertUnrelated(candidate); store.Save(candidate); writes++;
        }, new FakeTranslator(), window);
        live = main; window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        var draft = main.CreateSettings(); SetAllTwentyTwo(draft); var settings = ShowSettings(draft);
        try
        {
            prefs.Volume = 77; prefs.Mute = true; prefs.LastFolder = "/fresh"; prefs.RecentFiles.Add("fresh.mp4");
            prefs.WordTranslationService = "Off"; prefs.TranslateTargetLanguage = "Russian";
            Click(settings, Button(settings, "SaveButton"));
            Assert.False(settings.IsVisible); Assert.Null(draft.Error); Assert.Equal(1, writes);
            AssertTwentyTwo(prefs); AssertTwentyTwo(store.Load()); AssertUnrelated(prefs); AssertUnrelated(store.Load());
            AssertLive(main, "#654321", 29, 83); AssertActual(window, "#654321", 29, 83);
            Assert.Equal(42, main.SubtitleFontSize); Assert.Equal(50.4, main.SecondaryFontSize); Assert.Equal(64, main.SubtitleBottomOffset);
            Assert.Equal(7.25, main.SubtitleOutlineThickness); Assert.Equal(0, main.SecondarySubtitleOutlineThickness);
            Assert.Equal(FontWeight.Bold, Read(main, "SubtitleFontWeight")); Assert.Equal(FontStyle.Italic, Read(main, "SubtitleFontStyle"));
            Assert.Equal(FontWeight.Normal, Read(main, "SecondarySubtitleFontWeight")); Assert.Equal(FontStyle.Normal, Read(main, "SecondarySubtitleFontStyle"));
            Assert.False(main.IsDarkTheme); Assert.False(player.SubtitlesVisible); Assert.True(main.SidebarVisible); Assert.Equal(460, main.SidebarWidth);
            using var restarted = NewMain(store.Load()); AssertLive(restarted, "#654321", 29, 83); AssertTwentyTwo(restarted.CreateSettings());
        }
        finally { settings.Close(); CloseMain(window); }
    }

    [AvaloniaFact]
    public void Real_atomic_write_failure_keeps_file_live_state_and_all_draft_fields_then_the_same_draft_retries_successfully()
    {
        using TempDir temp = new(); var store = NewStore(temp); var prefs = SavedBackground(); store.Save(prefs);
        string fileBefore = File.ReadAllText(store.Path), prefsBefore = Json(prefs); int writes = 0;
        var player = new FakePlaybackController(); var shell = new FakeShell();
        using var main = new MainWindowViewModel(player, prefs, candidate => { writes++; store.Save(candidate); }, new FakeTranslator(), shell);
        var draft = main.CreateSettings(); SetAllTwentyTwo(draft); var window = ShowSettings(draft);
        Directory.CreateDirectory(store.Path + ".tmp");
        try
        {
            Click(window, Button(window, "SaveButton"));
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Equal(1, writes);
            Assert.False(string.IsNullOrWhiteSpace(Control<TextBlock>(window, "SaveError").Text));
            Assert.Equal(fileBefore, File.ReadAllText(store.Path)); Assert.Equal(prefsBefore, Json(prefs)); Assert.Empty(shell.Themes);
            AssertLive(main, "#123456", 35, 70); Assert.True(main.IsDarkTheme); Assert.True(player.SubtitlesVisible);
            Assert.Equal(34, main.SubtitleFontSize); Assert.Equal(27.2, main.SecondaryFontSize); Assert.Equal(24, main.SubtitleBottomOffset);
            Assert.Equal(3, main.SubtitleOutlineThickness); Assert.Equal(2.5, main.SecondarySubtitleOutlineThickness); Assert.False(main.SidebarVisible);
            AssertTwentyTwo(draft); AssertPreview(window, "#654321", 29, 83);
            Directory.Delete(store.Path + ".tmp"); Click(window, Button(window, "SaveButton"));
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Equal(2, writes);
            AssertTwentyTwo(prefs); AssertTwentyTwo(store.Load()); AssertLive(main, "#654321", 29, 83);
            Assert.False(main.IsDarkTheme); Assert.False(player.SubtitlesVisible); Assert.Equal(["Light"], shell.Themes);
            using var restarted = NewMain(store.Load()); AssertLive(restarted, "#654321", 29, 83);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_and_Escape_discard_background_edits_and_reopening_and_restart_restore_saved_values(bool escape)
    {
        using TempDir temp = new(); var store = NewStore(temp); var prefs = SavedBackground(); store.Save(prefs);
        string fileBefore = File.ReadAllText(store.Path); int writes = 0;
        using var main = NewMain(prefs, _ => writes++); var draft = main.CreateSettings(); var window = ShowSettings(draft);
        try
        {
            Set(draft, ColorField, "#654321"); Set(draft, PrimaryField, 29); Set(draft, SecondaryField, 83); Dispatcher.UIThread.RunJobs();
            AssertPreview(window, "#654321", 29, 83);
            if (escape) window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            else Click(window, Button(window, "CloseButton"));
            Dispatcher.UIThread.RunJobs(); Assert.False(window.IsVisible); Assert.Equal(0, writes); Assert.Equal(fileBefore, File.ReadAllText(store.Path));
            AssertBackground(prefs, "#123456", 35, 70); AssertLive(main, "#123456", 35, 70);
            var reopened = main.CreateSettings(); var second = ShowSettings(reopened);
            try { AssertBackground(reopened, "#123456", 35, 70); AssertPreview(second, "#123456", 35, 70); }
            finally { second.Close(); }
            using var restarted = NewMain(store.Load()); AssertLive(restarted, "#123456", 35, 70);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Reset_Background_resets_only_three_fields_preserving_all_prior_nineteen_and_does_not_commit()
    {
        using TempDir temp = new(); var store = NewStore(temp); var prefs = SavedBackground(); store.Save(prefs); string fileBefore = File.ReadAllText(store.Path); int writes = 0;
        using var main = NewMain(prefs, _ => writes++); var draft = main.CreateSettings(); SetAllTwentyTwo(draft);
        string before = PriorFieldsJson(draft); var window = ShowSettings(draft);
        try
        {
            var reset = Button(window, "ResetSubtitleBackgroundButton");
            Assert.Same(Read(draft, "ResetSubtitleBackgroundCommand"), reset.Command);
            reset.Command!.Execute(null); Dispatcher.UIThread.RunJobs();
            AssertBackground(draft, "#000000", 0, 0); Assert.Equal("0", Text(draft, PrimaryField + "Text")); Assert.Equal("0", Text(draft, SecondaryField + "Text"));
            Assert.Equal(before, PriorFieldsJson(draft)); Assert.Equal(0, writes); Assert.Equal(fileBefore, File.ReadAllText(store.Path));
            AssertPreview(window, "#000000", 0, 0); AssertBrush(Read(draft, "SubtitleBackgroundColorBrush"), "#000000", 100);
            AssertBackground(prefs, "#123456", 35, 70); AssertLive(main, "#123456", 35, 70);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Font_copy_and_reset_commands_preserve_color_and_both_background_opacities()
    {
        using var main = NewMain(SavedBackground()); var draft = main.CreateSettings(); SetAllTwentyTwo(draft);
        foreach (string command in new[] { "CopyPrimaryFontCommand", "ResetSecondaryFontCommand", "ResetSubtitleFontsCommand" })
        {
            Assert.IsAssignableFrom<ICommand>(Read(draft, command)).Execute(null);
            AssertBackground(draft, "#654321", 29, 83);
            AssertBrush(Read(draft, "SubtitleBackgroundBrush"), "#654321", 29); AssertBrush(Read(draft, "SecondarySubtitleBackgroundBrush"), "#654321", 83);
        }
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void New_background_editors_previews_and_keyboard_reset_fit_the_minimum_window_with_scroll_and_fixed_footer(string theme)
    {
        int writes = 0; using var main = NewMain(SavedBackground(), _ => writes++); var draft = main.CreateSettings();
        draft.SelectedSection = 1;
        var window = new SettingsWindow(draft) { Width = 560, Height = 440, RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var scroll = Control<ScrollViewer>(window, "SettingsScroll"); Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            foreach (string name in new[] { "SubtitleBackgroundColorEditor", "PrimaryBackgroundOpacityEditor", "SecondaryBackgroundOpacityEditor", "ResetSubtitleBackgroundButton", "SubtitleFontPreview", "SecondarySubtitleFontPreview" })
            {
                var control = Control<Control>(window, name); control.BringIntoView(); Dispatcher.UIThread.RunJobs(); AssertWithin(scroll, control);
                if (name.EndsWith("Editor", StringComparison.Ordinal))
                {
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)), name);
                    Assert.True(control.Focus(NavigationMethod.Tab), name);
                }
                AssertWithin(window, Button(window, "CloseButton")); AssertWithin(window, Button(window, "SaveButton"));
            }
            // Raw text boxes retain invalid integer input so Save can reject the whole detached draft.
            foreach (string name in new[] { "PrimaryBackgroundOpacityEditor", "SecondaryBackgroundOpacityEditor" })
                Assert.False(Control<TextBox>(window, name).IsReadOnly);
            var reset = Button(window, "ResetSubtitleBackgroundButton"); reset.BringIntoView(); Dispatcher.UIThread.RunJobs(); Assert.True(reset.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs(); AssertBackground(draft, "#000000", 0, 0); Assert.Equal(0, writes);
            Set(draft, PrimaryField + "Text", "1.5"); Click(window, Button(window, "SaveButton"));
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); AssertWithin(window, Control<TextBlock>(window, "SaveError"));
            AssertWithin(window, Button(window, "CloseButton")); AssertWithin(window, Button(window, "SaveButton")); Assert.Equal(0, writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Actual_overlay_background_bindings_do_not_reach_the_realized_sidebar_text()
    {
        var prefs = SavedBackground(); prefs.SidebarVisible = true;
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary overlay", SecondaryText = "Secondary overlay" };
        player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(3), "Sidebar words remain unfilled"));
        var window = new MainWindow { Width = 1100, Height = 700 };
        using var main = new MainWindowViewModel(player, prefs, _ => { }, new FakeTranslator(), window);
        window.Attach(main, null); window.Show(); player.RaiseCuesChanged(0); Dispatcher.UIThread.RunJobs();
        try
        {
            AssertActual(window, "#123456", 35, 70);
            var list = Control<ListBox>(window, "CueList"); Assert.Equal(1, list.ItemCount);
            var row = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0));
            var sidebarText = Assert.Single(row.GetVisualDescendants().OfType<SubtitleText>());
            Assert.Equal("Sidebar words remain unfilled", sidebarText.Text); Assert.Null(Read(sidebarText, "Background"));
            var draft = main.CreateSettings(); Set(draft, ColorField, "#AABBCC"); Set(draft, PrimaryField, 100); Set(draft, SecondaryField, 17); draft.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs(); Assert.Null(draft.Error); AssertActual(window, "#AABBCC", 100, 17);
            Assert.Null(Read(sidebarText, "Background")); Assert.Equal(16, sidebarText.FontSize); Assert.Equal(0, sidebarText.OutlineThickness);
        }
        finally { CloseMain(window); }
    }

    static readonly IReadOnlyDictionary<string, object> PriorValues = new Dictionary<string, object>
    {
        ["Theme"] = "Light", ["SubtitleFontSize"] = 42d, ["SecondaryFontScale"] = 1.2d, ["SubtitleBottomOffset"] = 64d,
        ["SubtitleOutlineThickness"] = 7.25d, ["SecondarySubtitleOutlineThickness"] = 0d,
        ["SubtitleTextColor"] = "#AABBCC", ["SecondarySubtitleTextColor"] = "#DDEEFF", ["SubtitleOutlineColor"] = "#12AB34",
        ["SubtitlesVisible"] = false, ["SidebarVisible"] = true, ["SidebarWidth"] = 460d,
        ["SubtitleFontFamily"] = "LLPlayer missing primary background test A1306", ["SubtitleFontWeight"] = "Bold", ["SubtitleFontStyle"] = "Italic",
        ["SecondarySubtitleFontFamily"] = "LLPlayer missing secondary background test B1492", ["SecondarySubtitleFontWeight"] = "Normal", ["SecondarySubtitleFontStyle"] = "Normal",
        ["SecondarySubtitleInheritsFont"] = false
    };

    static void SetAllTwentyTwo(SettingsViewModel draft)
    {
        Assert.Equal(19, PriorValues.Count);
        foreach (var pair in PriorValues) Set(draft, pair.Key, pair.Value);
        Set(draft, ColorField, "#654321"); Set(draft, PrimaryField, 29); Set(draft, SecondaryField, 83);
    }

    static void AssertTwentyTwo(object value)
    {
        foreach (var pair in PriorValues) Assert.Equal(pair.Value, Read(value, pair.Key));
        AssertBackground(value, "#654321", 29, 83);
    }

    static string PriorFieldsJson(object value) => Json(PriorValues.Keys.ToDictionary(name => name, name => Read(value, name)));
    static void AssertUnrelated(AppPrefs prefs)
    {
        Assert.Equal(77, prefs.Volume); Assert.True(prefs.Mute); Assert.Equal("/fresh", prefs.LastFolder); Assert.Equal(["fresh.mp4"], prefs.RecentFiles);
        Assert.Equal("Off", prefs.WordTranslationService); Assert.Equal("Russian", prefs.TranslateTargetLanguage);
    }

    static AppPrefs SavedBackground()
    {
        var prefs = new AppPrefs().Normalize(); Set(prefs, ColorField, "#123456"); Set(prefs, PrimaryField, 35); Set(prefs, SecondaryField, 70); return prefs;
    }

    static PrefsStore NewStore(TempDir temp) => new(Path.Combine(temp.Path, "preferences.json"));
    static string Json(object value) => JsonSerializer.Serialize(value, PrefsStore.JsonOptions);
    static MainWindowViewModel NewMain(AppPrefs prefs, Action<AppPrefs>? save = null)
        => new(new FakePlaybackController(), prefs, save ?? (_ => { }), new FakeTranslator(), new FakeShell());
    static SettingsWindow ShowSettings(SettingsViewModel draft)
    {
        draft.SelectedSection = 1; var window = new SettingsWindow(draft) { Height = 1000 }; window.Show(); Dispatcher.UIThread.RunJobs(); return window;
    }

    static void AssertBackground(object value, string color, int primary, int secondary)
    {
        Assert.Equal(color, Text(value, ColorField)); Assert.Equal(primary, Assert.IsType<int>(Read(value, PrimaryField))); Assert.Equal(secondary, Assert.IsType<int>(Read(value, SecondaryField)));
        if (value is AppPrefs prefs)
        {
            using var json = JsonDocument.Parse(Json(prefs)); Assert.Equal(color, json.RootElement.GetProperty(ColorField).GetString());
            Assert.Equal(primary, json.RootElement.GetProperty(PrimaryField).GetInt32()); Assert.Equal(secondary, json.RootElement.GetProperty(SecondaryField).GetInt32());
        }
    }

    static void AssertLive(MainWindowViewModel main, string color, int primary, int secondary)
    {
        AssertBrush(Read(main, "SubtitleBackgroundBrush"), color, primary); AssertBrush(Read(main, "SecondarySubtitleBackgroundBrush"), color, secondary);
    }

    static void AssertActual(MainWindow window, string color, int primary, int secondary)
    {
        AssertBrush(Read(Control<SubtitleText>(window, "PrimarySubtitle"), "Background"), color, primary);
        AssertBrush(Read(Control<SubtitleText>(window, "SecondarySubtitle"), "Background"), color, secondary);
    }

    static void AssertPreview(SettingsWindow window, string color, int primary, int secondary)
    {
        foreach ((string name, int percent) in new[] { ("SubtitleFontPreview", primary), ("SecondarySubtitleFontPreview", secondary) })
        {
            var preview = Control<SubtitleText>(window, name); AssertBrush(Read(preview, "Background"), color, percent);
            Assert.False(string.IsNullOrWhiteSpace(preview.Text)); Assert.False(preview.IsHitTestVisible); Assert.False(preview.Focusable);
        }
    }

    static void AssertBrush(object? value, string rgb, int percent)
    {
        if (percent == 0 && value is null) return;
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(value); Color expected = Color.Parse(rgb);
        Assert.Equal(expected.R, brush.Color.R); Assert.Equal(expected.G, brush.Color.G); Assert.Equal(expected.B, brush.Color.B);
        double effectiveAlpha = brush.Color.A / 255d * brush.Opacity;
        Assert.InRange(Math.Abs(effectiveAlpha - percent / 100d), 0, 0.5 / 255d + 1e-9);
    }

    // Reflection keeps the intentional pre-feature RED run compiled; a missing contract property fails explicitly.
    static PropertyInfo RequiredProperty(Type type, string name)
    {
        PropertyInfo? property = type.GetProperty(name); Assert.True(property is not null, $"Required subtitle background property {type.Name}.{name} is missing."); return property!;
    }
    static object? Read(object instance, string name) => RequiredProperty(instance.GetType(), name).GetValue(instance);
    static string Text(object instance, string name) => Assert.IsType<string>(Read(instance, name));
    static void Set(object instance, string name, object value)
    {
        var property = RequiredProperty(instance.GetType(), name); Assert.True(property.CanWrite, name); property.SetValue(instance, value);
    }
    static T Control<T>(Window window, string name) where T : Control => Assert.IsAssignableFrom<T>(window.FindControl<Control>(name));
    static Button Button(Window window, string name)
    {
        var button = Control<Button>(window, name); Assert.IsAssignableFrom<ICommand>(button.Command); return button;
    }
    static void CloseMain(MainWindow window) { window.ShutdownRequested = () => Task.CompletedTask; window.Close(); }
    static void Click(Window window, Control control)
    {
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
    static void AssertWithin(Visual parent, Control control)
    {
        Assert.True(control.IsEffectivelyVisible, control.Name); Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, control.Name);
        Point point = control.TranslatePoint(default, parent)!.Value;
        Assert.True(point.X >= -1 && point.Y >= -1 && point.X + control.Bounds.Width <= parent.Bounds.Width + 1 && point.Y + control.Bounds.Height <= parent.Bounds.Height + 1,
            $"{control.Name}: {point} / {control.Bounds.Size} in {parent.Bounds.Size}");
    }
}
