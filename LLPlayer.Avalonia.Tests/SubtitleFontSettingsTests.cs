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
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class SubtitleFontSettingsTests
{
    const string MissingPrimary = "LLPlayer unavailable primary 8B98A073";
    const string MissingSecondary = "LLPlayer unavailable secondary F276DA17";
    static readonly string[] FontStrings = ["SubtitleFontFamily", "SubtitleFontWeight", "SubtitleFontStyle",
        "SecondarySubtitleFontFamily", "SecondarySubtitleFontWeight", "SecondarySubtitleFontStyle"];

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Volume\":42,\"Theme\":\"Light\",\"SubtitleFontSize\":48,\"SecondaryFontScale\":1.2,\"SubtitleTextColor\":\"#123456\",\"SecondarySubtitleTextColor\":\"#ABCDEF\"}")]
    public void Legacy_preferences_add_default_font_tuples_without_changing_existing_settings(string json)
    {
        var prefs = JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();
        AssertFonts(prefs, "", "SemiBold", "Normal", "", "SemiBold", "Normal", true);
        using var before = JsonDocument.Parse(json);
        using var after = JsonDocument.Parse(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
        foreach (var property in before.RootElement.EnumerateObject())
            Assert.Equal(property.Value.GetRawText(), after.RootElement.GetProperty(property.Name).GetRawText());
    }

    public static IEnumerable<object?[]> SavedStringCases()
    {
        foreach (string prefix in new[] { "Subtitle", "SecondarySubtitle" })
        {
            foreach ((string? raw, string expected) in new (string?, string)[]
            {
                (null, ""), ("", ""), ("   ", ""), ("  Noto Sans CJK JP  ", "Noto Sans CJK JP"),
                (MissingPrimary, MissingPrimary), ("avares://fonts/face#Face", ""), ("file:///tmp/font.ttf#Face", ""),
                ("/usr/share/fonts/face.ttf", ""), (@"C:\Fonts\Face.ttf", ""), ("Face\nInjected", ""),
                ("Face\0Injected", ""), ("Face\tInjected", "")
            }) yield return [prefix + "FontFamily", raw, expected];
            foreach ((string? raw, string expected) in new (string?, string)[]
            {
                (null, "SemiBold"), ("", "SemiBold"), (" normal ", "Normal"), ("sEmIbOlD", "SemiBold"),
                (" BOLD ", "Bold"), ("Light", "SemiBold"), ("700", "SemiBold"), ("Bold\nInjected", "SemiBold")
            }) yield return [prefix + "FontWeight", raw, expected];
            foreach ((string? raw, string expected) in new (string?, string)[]
            {
                (null, "Normal"), ("", "Normal"), (" normal ", "Normal"), (" iTaLiC ", "Italic"),
                ("Oblique", "Normal"), ("Italic\nInjected", "Normal")
            }) yield return [prefix + "FontStyle", raw, expected];
        }
    }

    [Theory]
    [MemberData(nameof(SavedStringCases))]
    public void Saved_font_strings_normalize_per_field_without_rewriting_the_file_or_reporting_corruption(string field, string? raw, string expected)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        string json = JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = raw, ["Volume"] = 42 });
        File.WriteAllText(store.Path, json);
        var prefs = store.Load();
        Assert.Null(store.LoadWarning); Assert.False(File.Exists(store.Path + ".bad"));
        Assert.Equal(json, File.ReadAllText(store.Path)); Assert.Equal(42, prefs.Volume);
        Assert.Equal(expected, String(prefs, field));
        Assert.Equal(expected, String(prefs.Normalize(), field));
    }

    [Theory]
    [InlineData("{\"Volume\":42,\"SubtitleFontFamily\":42}")]
    [InlineData("{\"Volume\":42,\"SubtitleFontWeight\":true}")]
    [InlineData("{\"Volume\":42,\"SubtitleFontStyle\":[]}")]
    [InlineData("{\"Volume\":42,\"SecondarySubtitleFontFamily\":{}}")]
    [InlineData("{\"Volume\":42,\"SecondarySubtitleFontWeight\":42}")]
    [InlineData("{\"Volume\":42,\"SecondarySubtitleFontStyle\":false}")]
    [InlineData("{\"Volume\":42,\"SecondarySubtitleInheritsFont\":\"true\"}")]
    [InlineData("{\"Volume\":42,\"SecondarySubtitleInheritsFont\":null}")]
    [InlineData("{\"Volume\":42,\"SecondarySubtitleInheritsFont\":0}")]
    [InlineData("{ not json")]
    [InlineData("{\"Volume\":42,\"SubtitleFontFamily\":\"Arial\"")]
    public void Wrong_font_JSON_types_keep_the_existing_whole_file_failure_policy(string json)
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json")); File.WriteAllText(store.Path, json);
        var prefs = store.Load();
        Assert.NotNull(store.LoadWarning); Assert.Equal(100, prefs.Volume);
        Assert.Equal(json, File.ReadAllText(store.Path)); Assert.Equal(json, File.ReadAllText(store.Path + ".bad"));
        AssertFonts(prefs, "", "SemiBold", "Normal", "", "SemiBold", "Normal", true);
    }

    [AvaloniaFact]
    public void Missing_requested_families_remain_visible_and_saved_while_both_live_overlays_and_previews_use_Inter()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = SavedFonts(); store.Save(prefs); int writes = 0;
        using var main = NewMain(prefs, candidate => { store.Save(candidate); writes++; });
        using var defaults = NewMain(new AppPrefs().Normalize());
        FontFamily appDefault = LiveFamily(defaults, "SubtitleFontFamily");
        Assert.Equal(appDefault, LiveFamily(main, "SubtitleFontFamily"));
        Assert.Equal(appDefault, LiveFamily(main, "SecondarySubtitleFontFamily"));
        var draft = main.CreateSettings(); var window = ShowSettings(draft);
        try
        {
            foreach (string prefix in new[] { "Subtitle", "SecondarySubtitle" })
            {
                string requested = prefix == "Subtitle" ? MissingPrimary : MissingSecondary;
                var editor = Editor(window, prefix + "FontFamily");
                Assert.Contains("", editor.Items.OfType<string>()); Assert.Contains(requested, editor.Items.OfType<string>());
                Assert.Equal(requested, editor.SelectedItem);
                string status = String(draft, prefix + "FontStatus");
                Assert.Contains(requested, status, StringComparison.Ordinal);
                Assert.Contains("Inter", status, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("fallback", status, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(appDefault, Preview(window, prefix).FontFamily);
            }
            // An unrelated save must not replace the requested family with the fallback.
            draft.Theme = "Light"; draft.SaveCommand.Execute(null);
            Assert.Null(draft.Error); Assert.Equal(1, writes);
            AssertFonts(store.Load(), MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
            AssertFonts(prefs, MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Selecting_an_available_system_family_reaches_both_previews_and_live_overlays_and_survives_save_and_restart()
    {
        var available = FontManager.Current.SystemFonts
            .OrderBy(family => family.Name, StringComparer.Ordinal)
            .FirstOrDefault(family => !family.Name.Contains("Inter", StringComparison.OrdinalIgnoreCase)
                && FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, FontWeight.Normal), out _));
        Assert.True(available is not null, "This regression needs one installed non-Inter system family with a normal face; no fonts are installed by the test.");
        string requested = available!.Name;
        var expected = new FontFamily(requested);
        using TempDir temp = new(); var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs); int writes = 0;
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary preview", SecondaryText = "Secondary preview" };
        var window = new MainWindow { Width = 1000, Height = 700 };
        using var main = new MainWindowViewModel(player, prefs, candidate => { store.Save(candidate); writes++; }, new FakeTranslator(), window);
        FontFamily appDefault = LiveFamily(main, "SubtitleFontFamily");
        Assert.NotEqual(appDefault, expected);
        window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        var draft = main.CreateSettings(); Set(draft, "SecondarySubtitleInheritsFont", false);
        Set(draft, "SubtitleFontWeight", "Normal"); Set(draft, "SecondarySubtitleFontWeight", "Normal");
        var settings = ShowSettings(draft);
        try
        {
            foreach (string prefix in new[] { "Subtitle", "SecondarySubtitle" })
            {
                Select(settings, prefix + "FontFamily", requested);
                Assert.Equal(requested, String(draft, prefix + "FontFamily"));
                Assert.Equal(expected, Preview(settings, prefix).FontFamily);
                Assert.NotEqual(appDefault, Preview(settings, prefix).FontFamily);
                Assert.Equal(appDefault, LiveFamily(main, prefix + "FontFamily"));
            }
            Assert.Equal(0, writes); Assert.Equal("", String(store.Load(), "SubtitleFontFamily"));
            Click(settings, Button(settings, "SaveButton"));
            Assert.False(settings.IsVisible); Assert.Null(draft.Error); Assert.Equal(1, writes);
            foreach (string prefix in new[] { "Subtitle", "SecondarySubtitle" })
            {
                Assert.Equal(expected, LiveFamily(main, prefix + "FontFamily"));
                Assert.Equal(requested, String(prefs, prefix + "FontFamily"));
                Assert.Equal(requested, String(store.Load(), prefix + "FontFamily"));
            }
            AssertRendered(window, main);
            using var restarted = NewMain(store.Load());
            Assert.Equal(expected, LiveFamily(restarted, "SubtitleFontFamily"));
            Assert.Equal(expected, LiveFamily(restarted, "SecondarySubtitleFontFamily"));
        }
        finally { settings.Close(); window.ShutdownRequested = () => Task.CompletedTask; window.Close(); }
    }

    [AvaloniaFact]
    public void Bound_editors_update_detached_previews_and_secondary_inheritance_retains_its_requested_overrides()
    {
        var prefs = SavedFonts();
        using var main = NewMain(prefs); var draft = main.CreateSettings();
        var window = ShowSettings(draft);
        try
        {
            Select(window, "SubtitleFontWeight", "Normal"); Select(window, "SubtitleFontStyle", "Normal");
            Select(window, "SecondarySubtitleFontWeight", "Bold"); Select(window, "SecondarySubtitleFontStyle", "Italic");
            Assert.Equal("Normal", String(draft, "SubtitleFontWeight")); Assert.Equal("Normal", String(draft, "SubtitleFontStyle"));
            Assert.Equal("Bold", String(draft, "SecondarySubtitleFontWeight")); Assert.Equal("Italic", String(draft, "SecondarySubtitleFontStyle"));
            AssertPreview(window, "Subtitle", FontWeight.Normal, FontStyle.Normal);
            AssertPreview(window, "SecondarySubtitle", FontWeight.Bold, FontStyle.Italic);
            AssertLive(main, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal);
            AssertFonts(prefs, MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
            var inherits = Assert.IsType<CheckBox>(window.FindControl<Control>("SecondarySubtitleInheritsFontEditor"));
            inherits.IsChecked = true; Dispatcher.UIThread.RunJobs();
            Assert.True(Bool(draft, "SecondarySubtitleInheritsFont"));
            AssertPreview(window, "SecondarySubtitle", FontWeight.Normal, FontStyle.Normal);
            foreach (string suffix in new[] { "Family", "Weight", "Style" }) Assert.False(Editor(window, "SecondarySubtitleFont" + suffix).IsEffectivelyEnabled);
            Select(window, "SubtitleFontWeight", "SemiBold"); Select(window, "SubtitleFontStyle", "Italic");
            AssertPreview(window, "SecondarySubtitle", FontWeight.SemiBold, FontStyle.Italic);
            Assert.Equal(MissingSecondary, String(draft, "SecondarySubtitleFontFamily"));
            Assert.Equal("Bold", String(draft, "SecondarySubtitleFontWeight"));
            inherits.IsChecked = false; Dispatcher.UIThread.RunJobs();
            AssertPreview(window, "SecondarySubtitle", FontWeight.Bold, FontStyle.Italic);
            foreach (string suffix in new[] { "Family", "Weight", "Style" }) Assert.True(Editor(window, "SecondarySubtitleFont" + suffix).IsEffectivelyEnabled);
            Select(window, "SubtitleFontFamily", ""); Select(window, "SecondarySubtitleFontFamily", "");
            Assert.Equal("", String(draft, "SubtitleFontFamily")); Assert.Equal("", String(draft, "SecondarySubtitleFontFamily"));
            Assert.Equal(34, draft.SubtitleFontSize); Assert.Equal(0.8, draft.SecondaryFontScale);
            Assert.Equal("#FFFFFF", draft.SubtitleTextColor); Assert.Equal("#E8E8E8", draft.SecondarySubtitleTextColor);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Copy_Primary_snapshots_the_requested_tuple_and_Reset_Secondary_enables_inheritance_without_erasing_overrides()
    {
        var prefs = SavedFonts(); using var main = NewMain(prefs); var draft = main.CreateSettings();
        SetNonFontDraft(draft); string before = NonFontJson(draft);
        var window = ShowSettings(draft);
        try
        {
            Button(window, "CopyPrimaryFontButton").Command!.Execute(null); Dispatcher.UIThread.RunJobs();
            AssertFonts(draft, MissingPrimary, "Bold", "Italic", MissingPrimary, "Bold", "Italic", false);
            Assert.Equal(before, NonFontJson(draft));
            Set(draft, "SubtitleFontFamily", MissingSecondary); Set(draft, "SubtitleFontWeight", "Normal"); Set(draft, "SubtitleFontStyle", "Normal");
            Dispatcher.UIThread.RunJobs(); AssertPreview(window, "SecondarySubtitle", FontWeight.Bold, FontStyle.Italic);
            Assert.Equal(MissingPrimary, String(draft, "SecondarySubtitleFontFamily"));
            Button(window, "ResetSecondaryFontButton").Command!.Execute(null); Dispatcher.UIThread.RunJobs();
            AssertFonts(draft, MissingSecondary, "Normal", "Normal", MissingPrimary, "Bold", "Italic", true);
            AssertPreview(window, "SecondarySubtitle", FontWeight.Normal, FontStyle.Normal);
            Set(draft, "SecondarySubtitleInheritsFont", false); Dispatcher.UIThread.RunJobs();
            AssertPreview(window, "SecondarySubtitle", FontWeight.Bold, FontStyle.Italic);
            Assert.Equal(before, NonFontJson(draft));
            AssertLive(main, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Reset_Fonts_changes_only_the_seven_font_fields_and_does_not_commit_the_draft()
    {
        var prefs = SavedFonts(); int writes = 0; using var main = NewMain(prefs, _ => writes++);
        var draft = main.CreateSettings(); SetNonFontDraft(draft); string before = NonFontJson(draft);
        var window = ShowSettings(draft);
        try
        {
            Button(window, "ResetSubtitleFontsButton").Command!.Execute(null); Dispatcher.UIThread.RunJobs();
            AssertFonts(draft, "", "SemiBold", "Normal", "", "SemiBold", "Normal", true);
            Assert.Equal(before, NonFontJson(draft)); Assert.Equal(0, writes);
            AssertPreview(window, "Subtitle", FontWeight.SemiBold, FontStyle.Normal);
            AssertPreview(window, "SecondarySubtitle", FontWeight.SemiBold, FontStyle.Normal);
            AssertFonts(prefs, MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inheritance_survives_restart_and_switching_it_off_restores_the_retained_secondary_tuple_without_changing_size_or_colors()
    {
        using TempDir temp = new(); var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = SavedFonts(); Set(prefs, "SecondarySubtitleInheritsFont", true);
        prefs.SubtitleFontSize = 48; prefs.SecondaryFontScale = 1.25;
        prefs.SubtitleTextColor = "#123456"; prefs.SecondarySubtitleTextColor = "#ABCDEF";
        prefs.SubtitleOutlineThickness = 7.25; prefs.SecondarySubtitleOutlineThickness = 0;
        store.Save(prefs); using var main = NewMain(store.Load(), store.Save);
        AssertLive(main, FontWeight.Bold, FontStyle.Italic, FontWeight.Bold, FontStyle.Italic);
        Assert.Equal(48, main.SubtitleFontSize); Assert.Equal(60, main.SecondaryFontSize);
        AssertBrush(main.SubtitleTextBrush, "#123456"); AssertBrush(main.SecondarySubtitleTextBrush, "#ABCDEF");
        Assert.Equal(7.25, main.SubtitleOutlineThickness); Assert.Equal(0, main.SecondarySubtitleOutlineThickness);
        var draft = main.CreateSettings(); Set(draft, "SecondarySubtitleInheritsFont", false); draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        AssertFonts(store.Load(), MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
        using var restarted = NewMain(store.Load()); AssertLive(restarted, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal);
        Assert.Equal(60, restarted.SecondaryFontSize); AssertBrush(restarted.SecondarySubtitleTextBrush, "#ABCDEF");
        Assert.Equal(0, restarted.SecondarySubtitleOutlineThickness);
    }

    [AvaloniaFact]
    public void Save_canonicalizes_raw_font_casing_and_whitespace_without_substituting_unavailable_requested_names()
    {
        AppPrefs? saved = null; using var main = NewMain(new AppPrefs().Normalize(), candidate => saved = candidate);
        var draft = main.CreateSettings();
        Set(draft, "SubtitleFontFamily", "  " + MissingPrimary + "  "); Set(draft, "SubtitleFontWeight", " bOlD "); Set(draft, "SubtitleFontStyle", " iTaLiC ");
        Set(draft, "SecondarySubtitleFontFamily", " " + MissingSecondary + " "); Set(draft, "SecondarySubtitleFontWeight", " nOrMaL ");
        Set(draft, "SecondarySubtitleFontStyle", " nOrMaL "); Set(draft, "SecondarySubtitleInheritsFont", false);
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error); Assert.NotNull(saved);
        AssertFonts(saved, MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
        AssertFonts(draft, MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
        AssertLive(main, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal);
    }

    [AvaloniaFact]
    public void Save_persists_all_nineteen_owned_fields_from_a_fresh_detached_candidate_before_applying_either_overlay()
    {
        using TempDir temp = new(); var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = AtomicBaselinePrefs(); store.Save(prefs);
        var player = new FakePlaybackController { IsOpened = true, PrimaryText = "Primary", SecondaryText = "Secondary" };
        var window = new MainWindow { Width = 1000, Height = 700 }; MainWindowViewModel? live = null; int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate =>
        {
            AssertFonts(prefs, MissingSecondary, "SemiBold", "Normal", MissingPrimary, "SemiBold", "Italic", true);
            AssertLive(live!, FontWeight.SemiBold, FontStyle.Normal, FontWeight.SemiBold, FontStyle.Normal);
            AssertRendered(window, live!); Assert.Equal(34, live!.SubtitleFontSize); Assert.True(live.IsDarkTheme);
            Assert.Equal(3, live.SubtitleOutlineThickness); Assert.Equal(2.5, live.SecondarySubtitleOutlineThickness);
            Assert.False(live.SidebarVisible); Assert.True(player.SubtitlesVisible);
            Assert.NotSame(prefs, candidate); Assert.NotSame(prefs.RecentFiles, candidate.RecentFiles);
            AssertNineteen(candidate); AssertUnrelated(candidate); store.Save(candidate); writes++;
        }, new FakeTranslator(), window);
        live = main; window.Attach(main, null); window.Show(); Dispatcher.UIThread.RunJobs();
        var draft = main.CreateSettings(); SetAllNineteen(draft); var settings = ShowSettings(draft);
        try
        {
            // These updates happen after opening the draft; a clone captured at dialog creation would lose them.
            prefs.Volume = 77; prefs.Mute = true; prefs.LastFolder = "/fresh"; prefs.RecentFiles.Add("fresh.mp4");
            prefs.WordTranslationService = "Off"; prefs.TranslateTargetLanguage = "Russian";
            Click(settings, Button(settings, "SaveButton"));
            Assert.False(settings.IsVisible); Assert.Null(draft.Error); Assert.Equal(1, writes);
            AssertNineteen(prefs); AssertNineteen(store.Load()); AssertUnrelated(store.Load());
            AssertLive(main, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal); AssertRendered(window, main);
            Assert.Equal(42, main.SubtitleFontSize); Assert.Equal(50.4, main.SecondaryFontSize); Assert.Equal(64, main.SubtitleBottomOffset);
            Assert.Equal(7.25, main.SubtitleOutlineThickness); Assert.Equal(0, main.SecondarySubtitleOutlineThickness);
            AssertBrush(main.SubtitleTextBrush, "#AABBCC"); AssertBrush(main.SecondarySubtitleTextBrush, "#DDEEFF"); AssertBrush(main.SubtitleOutlineBrush, "#12AB34");
            Assert.False(main.IsDarkTheme); Assert.False(player.SubtitlesVisible); Assert.True(main.SidebarVisible); Assert.Equal(460, main.SidebarWidth);
            using var restarted = NewMain(store.Load()); AssertLive(restarted, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal);
        }
        finally { settings.Close(); window.ShutdownRequested = () => Task.CompletedTask; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_and_Escape_discard_all_font_edits_and_reopening_and_restart_restore_saved_tuples(bool escape)
    {
        using TempDir temp = new(); var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = SavedFonts(); store.Save(prefs); string fileBefore = File.ReadAllText(store.Path); int writes = 0;
        using var main = NewMain(prefs, _ => writes++); var draft = main.CreateSettings(); var window = ShowSettings(draft);
        try
        {
            Set(draft, "SubtitleFontFamily", ""); Set(draft, "SubtitleFontWeight", "Normal"); Set(draft, "SubtitleFontStyle", "Normal");
            Set(draft, "SecondarySubtitleFontFamily", ""); Set(draft, "SecondarySubtitleFontWeight", "Bold"); Set(draft, "SecondarySubtitleFontStyle", "Italic");
            Set(draft, "SecondarySubtitleInheritsFont", true); Dispatcher.UIThread.RunJobs();
            if (escape) window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            else Click(window, Button(window, "CloseButton"));
            Dispatcher.UIThread.RunJobs(); Assert.False(window.IsVisible); Assert.Equal(0, writes);
            Assert.Equal(fileBefore, File.ReadAllText(store.Path));
            AssertFonts(prefs, MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
            AssertLive(main, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal);
            var reopened = main.CreateSettings(); var second = ShowSettings(reopened);
            try
            {
                AssertFonts(reopened, MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
                Assert.Equal(MissingPrimary, Editor(second, "SubtitleFontFamily").SelectedItem);
                Assert.Equal(MissingSecondary, Editor(second, "SecondarySubtitleFontFamily").SelectedItem);
            }
            finally { second.Close(); }
            using var restarted = NewMain(store.Load()); AssertLive(restarted, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_real_atomic_write_failure_preserves_all_live_state_and_file_until_the_same_font_draft_is_retried()
    {
        using TempDir temp = new(); var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = AtomicBaselinePrefs(); store.Save(prefs);
        string fileBefore = File.ReadAllText(store.Path), prefsBefore = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        var player = new FakePlaybackController(); var shell = new FakeShell(); int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate => { writes++; store.Save(candidate); }, new FakeTranslator(), shell);
        var draft = main.CreateSettings(); SetAllNineteen(draft); var window = ShowSettings(draft);
        Directory.CreateDirectory(store.Path + ".tmp");
        try
        {
            Click(window, Button(window, "SaveButton"));
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error); Assert.Equal(1, writes);
            Assert.False(string.IsNullOrWhiteSpace(window.FindControl<TextBlock>("SaveError")!.Text));
            Assert.Equal(fileBefore, File.ReadAllText(store.Path)); Assert.Equal(prefsBefore, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            AssertLive(main, FontWeight.SemiBold, FontStyle.Normal, FontWeight.SemiBold, FontStyle.Normal);
            Assert.Equal(34, main.SubtitleFontSize); Assert.Equal(27.2, main.SecondaryFontSize); Assert.Equal(24, main.SubtitleBottomOffset);
            Assert.Equal(3, main.SubtitleOutlineThickness); Assert.Equal(2.5, main.SecondarySubtitleOutlineThickness);
            AssertBrush(main.SubtitleTextBrush, "#FFFFFF"); AssertBrush(main.SecondarySubtitleTextBrush, "#E8E8E8"); AssertBrush(main.SubtitleOutlineBrush, "#000000");
            Assert.True(main.IsDarkTheme); Assert.True(player.SubtitlesVisible); Assert.False(main.SidebarVisible); Assert.Empty(shell.Themes);
            AssertNineteen(draft); AssertPreview(window, "Subtitle", FontWeight.Bold, FontStyle.Italic);
            Directory.Delete(store.Path + ".tmp"); Click(window, Button(window, "SaveButton"));
            Assert.False(window.IsVisible); Assert.Null(draft.Error); Assert.Equal(2, writes);
            AssertNineteen(prefs); AssertNineteen(store.Load());
            AssertLive(main, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal);
            Assert.False(main.IsDarkTheme); Assert.False(player.SubtitlesVisible); Assert.Equal(["Light"], shell.Themes);
            using var restarted = NewMain(store.Load()); AssertLive(restarted, FontWeight.Bold, FontStyle.Italic, FontWeight.Normal, FontStyle.Normal);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("SubtitleFontFamily", "file:///tmp/face.ttf#Face")]
    [InlineData("SubtitleFontFamily", "Face\nInjected")]
    [InlineData("SecondarySubtitleFontFamily", @"C:\Fonts\Face.ttf")]
    [InlineData("SubtitleFontWeight", "Light")]
    [InlineData("SecondarySubtitleFontWeight", "700")]
    [InlineData("SubtitleFontStyle", "Oblique")]
    [InlineData("SecondarySubtitleFontStyle", "Italic\nInjected")]
    public void Invalid_raw_font_input_blocks_the_entire_draft_without_losing_it_or_applying_unrelated_edits(string field, string value)
    {
        using TempDir temp = new(); var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        var prefs = new AppPrefs().Normalize(); store.Save(prefs); string before = File.ReadAllText(store.Path); int writes = 0;
        using var main = NewMain(prefs, _ => writes++); var draft = main.CreateSettings();
        SetNonFontDraft(draft); Set(draft, "SecondarySubtitleInheritsFont", false); Set(draft, field, value);
        bool closed = false; draft.CloseRequested += () => closed = true;
        draft.SaveCommand.Execute(null);
        Assert.False(closed); Assert.NotNull(draft.Error); Assert.Equal(0, writes);
        Assert.Equal(value, String(draft, field)); Assert.Equal(before, File.ReadAllText(store.Path));
        AssertFonts(prefs, "", "SemiBold", "Normal", "", "SemiBold", "Normal", true);
        AssertLive(main, FontWeight.SemiBold, FontStyle.Normal, FontWeight.SemiBold, FontStyle.Normal); Assert.True(main.IsDarkTheme);
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void All_font_controls_and_previews_fit_the_minimum_settings_window_and_keyboard_actions_leave_the_footer_reachable(string theme)
    {
        var prefs = SavedFonts(); int writes = 0; using var main = NewMain(prefs, _ => writes++); var draft = main.CreateSettings(); draft.SelectedSection = 1;
        var window = new SettingsWindow(draft) { Width = 560, Height = 440, RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            foreach (string name in FontStrings.Select(field => field + "Editor").Concat(new[]
            {
                "SecondarySubtitleInheritsFontEditor", "CopyPrimaryFontButton", "ResetSecondaryFontButton", "ResetSubtitleFontsButton",
                "SubtitleFontPreview", "SecondarySubtitleFontPreview"
            }))
            {
                var control = window.FindControl<Control>(name); Assert.NotNull(control);
                control.BringIntoView(); Dispatcher.UIThread.RunJobs(); AssertWithin(scroll, control);
                if (name.EndsWith("Editor", StringComparison.Ordinal))
                {
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)), name);
                    Assert.True(control.Focus(NavigationMethod.Tab), name);
                }
            }
            var weight = Editor(window, "SubtitleFontWeight");
            weight.BringIntoView(); Dispatcher.UIThread.RunJobs(); Assert.True(weight.Focus(NavigationMethod.Tab));
            weight.SelectedItem = "Normal"; Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual("Normal", String(draft, "SubtitleFontWeight"));
            var reset = Button(window, "ResetSubtitleFontsButton"); reset.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Assert.True(reset.Focus(NavigationMethod.Tab)); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            // Headless KeyPress sends only key-down; the default Button activates Space on key-up.
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs(); AssertFonts(draft, "", "SemiBold", "Normal", "", "SemiBold", "Normal", true);
            Assert.Equal(0, writes);
            // A validation error must not push the fixed Close/Save footer out of the small window.
            draft.SubtitleFontSizeText = "invalid"; Click(window, Button(window, "SaveButton"));
            Assert.True(window.IsVisible); Assert.NotNull(draft.Error);
            AssertWithin(window, window.FindControl<TextBlock>("SaveError")!);
            foreach (string name in new[] { "CloseButton", "SaveButton" })
            {
                var button = Button(window, name); Assert.True(button.IsEnabled); AssertWithin(window, button);
            }
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible); Assert.Equal(0, writes);
        }
        finally { window.Close(); }
    }

    static AppPrefs SavedFonts()
        => JsonSerializer.Deserialize<AppPrefs>(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["SubtitleFontFamily"] = MissingPrimary, ["SubtitleFontWeight"] = "Bold", ["SubtitleFontStyle"] = "Italic",
            ["SecondarySubtitleFontFamily"] = MissingSecondary, ["SecondarySubtitleFontWeight"] = "Normal", ["SecondarySubtitleFontStyle"] = "Normal",
            ["SecondarySubtitleInheritsFont"] = false
        }), PrefsStore.JsonOptions)!.Normalize();

    static AppPrefs AtomicBaselinePrefs()
    {
        var prefs = new AppPrefs().Normalize();
        // Both names are already selectable; the draft swaps their roles while changing all seven font fields.
        Set(prefs, "SubtitleFontFamily", MissingSecondary); Set(prefs, "SecondarySubtitleFontFamily", MissingPrimary);
        Set(prefs, "SecondarySubtitleFontStyle", "Italic");
        return prefs;
    }

    static MainWindowViewModel NewMain(AppPrefs prefs, Action<AppPrefs>? save = null)
        => new(new FakePlaybackController(), prefs, save ?? (_ => { }), new FakeTranslator(), new FakeShell());

    static SettingsWindow ShowSettings(SettingsViewModel draft)
    {
        draft.SelectedSection = 1; var window = new SettingsWindow(draft) { Height = 1000 };
        window.Show(); Dispatcher.UIThread.RunJobs(); return window;
    }

    static void SetNonFontDraft(SettingsViewModel draft)
    {
        draft.Theme = "Light"; draft.SubtitleFontSize = 42; draft.SecondaryFontScale = 1.2; draft.SubtitleBottomOffset = 64;
        draft.SubtitleOutlineThickness = 7.25; draft.SecondarySubtitleOutlineThickness = 0;
        draft.SubtitleTextColor = "#AABBCC"; draft.SecondarySubtitleTextColor = "#DDEEFF"; draft.SubtitleOutlineColor = "#12AB34";
        draft.SubtitlesVisible = false; draft.SidebarVisible = true; draft.SidebarWidth = 460;
    }

    static void SetAllNineteen(SettingsViewModel draft)
    {
        SetNonFontDraft(draft);
        Set(draft, "SubtitleFontFamily", MissingPrimary); Set(draft, "SubtitleFontWeight", "Bold"); Set(draft, "SubtitleFontStyle", "Italic");
        Set(draft, "SecondarySubtitleFontFamily", MissingSecondary); Set(draft, "SecondarySubtitleFontWeight", "Normal");
        Set(draft, "SecondarySubtitleFontStyle", "Normal"); Set(draft, "SecondarySubtitleInheritsFont", false);
    }

    static void AssertNineteen(object prefs)
    {
        AssertFonts(prefs, MissingPrimary, "Bold", "Italic", MissingSecondary, "Normal", "Normal", false);
        Assert.Equal("Light", String(prefs, "Theme")); Assert.Equal(42, Assert.IsType<double>(Read(prefs, "SubtitleFontSize")));
        Assert.Equal(1.2, Assert.IsType<double>(Read(prefs, "SecondaryFontScale"))); Assert.Equal(64, Assert.IsType<double>(Read(prefs, "SubtitleBottomOffset")));
        Assert.Equal(7.25, Assert.IsType<double>(Read(prefs, "SubtitleOutlineThickness"))); Assert.Equal(0, Assert.IsType<double>(Read(prefs, "SecondarySubtitleOutlineThickness")));
        Assert.Equal("#AABBCC", String(prefs, "SubtitleTextColor")); Assert.Equal("#DDEEFF", String(prefs, "SecondarySubtitleTextColor"));
        Assert.Equal("#12AB34", String(prefs, "SubtitleOutlineColor")); Assert.False(Bool(prefs, "SubtitlesVisible"));
        Assert.True(Bool(prefs, "SidebarVisible")); Assert.Equal(460, Assert.IsType<double>(Read(prefs, "SidebarWidth")));
    }

    static void AssertUnrelated(AppPrefs prefs)
    {
        Assert.Equal(77, prefs.Volume); Assert.True(prefs.Mute); Assert.Equal("/fresh", prefs.LastFolder);
        Assert.Equal(["fresh.mp4"], prefs.RecentFiles); Assert.Equal("Off", prefs.WordTranslationService); Assert.Equal("Russian", prefs.TranslateTargetLanguage);
    }

    static string NonFontJson(SettingsViewModel draft) => JsonSerializer.Serialize(new object[]
    {
        draft.Theme, draft.SubtitleFontSize, draft.SecondaryFontScale, draft.SubtitleBottomOffset, draft.SubtitleOutlineThickness,
        draft.SecondarySubtitleOutlineThickness, draft.SubtitleTextColor, draft.SecondarySubtitleTextColor, draft.SubtitleOutlineColor,
        draft.SubtitlesVisible, draft.SidebarVisible, draft.SidebarWidth
    });

    static void AssertFonts(object value, string primaryFamily, string primaryWeight, string primaryStyle,
        string secondaryFamily, string secondaryWeight, string secondaryStyle, bool inherits)
    {
        string[] expected = [primaryFamily, primaryWeight, primaryStyle, secondaryFamily, secondaryWeight, secondaryStyle];
        for (int i = 0; i < FontStrings.Length; i++) Assert.Equal(expected[i], String(value, FontStrings[i]));
        Assert.Equal(inherits, Bool(value, "SecondarySubtitleInheritsFont"));
        if (value is AppPrefs prefs)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
            for (int i = 0; i < FontStrings.Length; i++) Assert.Equal(expected[i], json.RootElement.GetProperty(FontStrings[i]).GetString());
            Assert.Equal(inherits, json.RootElement.GetProperty("SecondarySubtitleInheritsFont").GetBoolean());
        }
    }

    static void AssertLive(MainWindowViewModel main, FontWeight primaryWeight, FontStyle primaryStyle, FontWeight secondaryWeight, FontStyle secondaryStyle)
    {
        Assert.IsType<FontFamily>(Read(main, "SubtitleFontFamily")); Assert.IsType<FontFamily>(Read(main, "SecondarySubtitleFontFamily"));
        Assert.Equal(primaryWeight, Assert.IsType<FontWeight>(Read(main, "SubtitleFontWeight")));
        Assert.Equal(primaryStyle, Assert.IsType<FontStyle>(Read(main, "SubtitleFontStyle")));
        Assert.Equal(secondaryWeight, Assert.IsType<FontWeight>(Read(main, "SecondarySubtitleFontWeight")));
        Assert.Equal(secondaryStyle, Assert.IsType<FontStyle>(Read(main, "SecondarySubtitleFontStyle")));
    }

    static FontFamily LiveFamily(object value, string name) => Assert.IsType<FontFamily>(Read(value, name));
    static SubtitleText Preview(SettingsWindow window, string prefix) => Assert.IsType<SubtitleText>(window.FindControl<Control>(prefix + "FontPreview"));
    static void AssertPreview(SettingsWindow window, string prefix, FontWeight weight, FontStyle style)
    {
        var preview = Preview(window, prefix); Assert.Equal(weight, preview.FontWeight); Assert.Equal(style, Assert.IsType<FontStyle>(Read(preview, "FontStyle")));
        Assert.False(string.IsNullOrWhiteSpace(preview.Text)); Assert.False(preview.IsHitTestVisible); Assert.False(preview.Focusable);
    }
    static void AssertRendered(MainWindow window, MainWindowViewModel main)
    {
        foreach ((string controlName, string prefix) in new[] { ("PrimarySubtitle", "Subtitle"), ("SecondarySubtitle", "SecondarySubtitle") })
        {
            var subtitle = window.FindControl<SubtitleText>(controlName)!;
            Assert.Equal(LiveFamily(main, prefix + "FontFamily"), subtitle.FontFamily);
            Assert.Equal(Assert.IsType<FontWeight>(Read(main, prefix + "FontWeight")), subtitle.FontWeight);
            Assert.Equal(Assert.IsType<FontStyle>(Read(main, prefix + "FontStyle")), Assert.IsType<FontStyle>(Read(subtitle, "FontStyle")));
        }
    }
    static void AssertBrush(object brush, string color) => Assert.Equal(Color.Parse(color), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
    static ComboBox Editor(SettingsWindow window, string property) => Assert.IsType<ComboBox>(window.FindControl<Control>(property + "Editor"));
    static Button Button(SettingsWindow window, string name)
    {
        var button = Assert.IsType<Button>(window.FindControl<Control>(name)); Assert.IsAssignableFrom<ICommand>(button.Command); return button;
    }
    static void Select(SettingsWindow window, string property, string selected)
    {
        var editor = Editor(window, property); Assert.Contains(selected, editor.Items.OfType<string>());
        editor.SelectedItem = selected; Dispatcher.UIThread.RunJobs(); Assert.Equal(selected, editor.SelectedItem);
    }

    // Reflection keeps the tests compileable against the pre-feature baseline for the intentional RED run.
    static object? Read(object instance, string name)
    {
        PropertyInfo? property = instance.GetType().GetProperty(name); Assert.NotNull(property); return property.GetValue(instance);
    }
    static string String(object instance, string name) => Assert.IsType<string>(Read(instance, name));
    static bool Bool(object instance, string name) => Assert.IsType<bool>(Read(instance, name));
    static void Set(object instance, string name, object value)
    {
        PropertyInfo? property = instance.GetType().GetProperty(name); Assert.NotNull(property); Assert.True(property.CanWrite, name); property.SetValue(instance, value);
    }
    static void Click(Window window, Control control)
    {
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
    static void AssertWithin(Visual parent, Control control)
    {
        Assert.True(control.IsEffectivelyVisible, control.Name); Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, control.Name);
        Point point = control.TranslatePoint(default, parent)!.Value;
        Assert.True(point.X >= -1 && point.Y >= -1 && point.X + control.Bounds.Width <= parent.Bounds.Width + 1
            && point.Y + control.Bounds.Height <= parent.Bounds.Height + 1, $"{control.Name}: {point} / {control.Bounds.Size} in {parent.Bounds.Size}");
    }
}
