using System.Globalization;
using System.Text.Json;
using Avalonia.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public class WordActionsPolicyTests
{
    [Fact]
    public void Preferences_file_roundtrip_keeps_order_enums_lowercase_and_explicit_empty_menu()
    {
        string dir = Path.Combine(Path.GetTempPath(), "word-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "prefs.json");
            PrefsStore store = new(path);
            File.WriteAllText(path, "{}");
            AppPrefs absent = store.Load();
            Assert.False(absent.WordCopyOnSelected); Assert.False(absent.WordLastSearchOnSelected); Assert.False(absent.SubsAutoTextCopy);
            Assert.Equal(WordSearchModifier.Control, absent.WordLastSearchModifier);
            Assert.Equal(SubtitleAutoCopyTarget.Primary, absent.SubsAutoTextCopyTarget);
            AppPrefs source = new()
            {
                AnkiDeck = "Keep deck", WordCopyOnSelected = true, WordLastSearchOnSelected = true,
                WordLastSearchModifier = WordSearchModifier.Alt, SubsAutoTextCopy = true, SubsAutoTextCopyTarget = SubtitleAutoCopyTarget.Secondary,
                WordMenuActions =
                [
                    new() { Kind = WordMenuActionKind.Search, Title = "First search", Url = "https://example.test/?q=%lw", IsEnabled = false },
                    new() { Kind = WordMenuActionKind.ClipboardAll, Title = "Second copy", ToLower = true },
                ],
            };
            store.Save(source);
            AppPrefs loaded = store.Load();
            Assert.Null(store.LoadWarning); Assert.Equal("Keep deck", loaded.AnkiDeck);
            Assert.True(loaded.WordCopyOnSelected); Assert.True(loaded.WordLastSearchOnSelected); Assert.True(loaded.SubsAutoTextCopy);
            Assert.Equal(WordSearchModifier.Alt, loaded.WordLastSearchModifier);
            Assert.Equal(SubtitleAutoCopyTarget.Secondary, loaded.SubsAutoTextCopyTarget);
            List<WordMenuActionPref> rows = loaded.WordMenuActions ?? throw new Xunit.Sdk.XunitException("Loaded rows are missing");
            Assert.Equal(new[] { "First search", "Second copy" }, rows.Select(row => row.Title));
            Assert.Equal(WordMenuActionKind.Search, rows[0].Kind); Assert.False(rows[0].IsEnabled);
            Assert.Equal("https://example.test/?q=%lw", rows[0].Url);
            Assert.Equal(WordMenuActionKind.ClipboardAll, rows[1].Kind); Assert.True(rows[1].ToLower);
            rows[0].Title = "Detached load";
            Assert.Equal("First search", store.Load().WordMenuActions![0].Title);
            source.WordMenuActions = []; store.Save(source);
            Assert.Empty(store.Load().WordMenuActions!);
        }
        finally { Directory.Delete(dir, true); }
    }

    static WordSelectionSnapshot Selection(string word = "Café &/海", string text = "One\r\ntwo & three")
        => new(word, text, 1, true, false, "en", "fr", WordSelectionOrigin.Overlay, MouseButton.Left, KeyModifiers.None);

    [Fact]
    public void Search_encodes_each_substitution_as_a_value_without_changing_query_structure()
    {
        Assert.True(WordActions.TryExpandSearch("https://example.test/find?word=%w&lower=%lw&sentence=%s", Selection(), out Uri? uri, out string error), error);
        Assert.Equal("https://example.test/find?word=Caf%C3%A9%20%26%2F%E6%B5%B7&lower=caf%C3%A9%20%26%2F%E6%B5%B7&sentence=One%0D%0Atwo%20%26%20three", uri!.AbsoluteUri);
    }

    [Fact]
    public void Search_does_not_expand_tokens_inside_inserted_values()
    {
        Assert.True(WordActions.TryExpandSearch("https://example.test/?w=%w&lw=%lw&s=%s", Selection("%s/%w/%lw", "%w"), out Uri? uri, out string error), error);
        Assert.Equal("https://example.test/?w=%25s%2F%25w%2F%25lw&lw=%25s%2F%25w%2F%25lw&s=%25w", uri!.AbsoluteUri);
    }

    [Fact]
    public void Lowercase_search_uses_the_current_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.True(WordActions.TryExpandSearch("https://example.test/?q=%lw", Selection("I"), out Uri? uri, out string error), error);
            Assert.Equal("https://example.test/?q=%C4%B1", uri!.AbsoluteUri);
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Theory]
    [InlineData("file:///tmp/word")]
    [InlineData("javascript:alert('%w')")]
    [InlineData("custom://example.test/%w")]
    [InlineData("/relative?q=%w")]
    [InlineData("https://")]
    [InlineData("https://example.test/\nword")]
    [InlineData("https://example.test/%zz")]
    public void Unsafe_or_malformed_search_templates_are_refused(string template)
    {
        Assert.False(WordActions.TryExpandSearch(template, Selection(), out Uri? uri, out string error));
        Assert.Null(uri);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Validation_checks_disabled_searches_and_returns_independent_trimmed_rows()
    {
        WordMenuActionPref invalid = new() { Kind = WordMenuActionKind.Search, Title = "Disabled", IsEnabled = false, Url = "file:///tmp/a" };
        Assert.False(WordActions.TryValidateRows([invalid], out _, out _));
        WordMenuActionPref copy = new() { Kind = WordMenuActionKind.Clipboard, Title = "  Copy item  ", ToLower = true };
        Assert.True(WordActions.TryValidateRows([copy], out List<WordMenuActionPref> rows, out string error), error);
        Assert.Equal("Copy item", Assert.Single(rows).Title);
        Assert.True(rows[0].ToLower);
        rows[0].Title = "Independent";
        Assert.Equal("  Copy item  ", copy.Title);
    }

    [Fact]
    public void Missing_menu_uses_independent_defaults_but_explicit_empty_stays_empty()
    {
        List<WordMenuActionPref> first = WordActions.NormalizeRows(null);
        List<WordMenuActionPref> second = WordActions.NormalizeRows(null);
        Assert.Equal(new[] { "Copy", "Copy All", "Search Google", "Search Wiktionary", "Search Longman" }, first.Select(row => row.Title));
        first[0].Title = "Changed";
        Assert.Equal("Copy", second[0].Title);
        Assert.Empty(WordActions.NormalizeRows([]));
    }

    [Fact]
    public void Loaded_unknown_and_invalid_rows_remain_inert_and_detached()
    {
        WordMenuActionPref source = new() { Kind = (WordMenuActionKind)999, Title = "Unknown", IsEnabled = true };
        WordMenuActionPref unsafeSearch = new() { Kind = WordMenuActionKind.Search, Title = "Unsafe", Url = "file:///tmp/a" };
        List<WordMenuActionPref> rows = WordActions.NormalizeRows([source, unsafeSearch]);
        Assert.All(rows, row => Assert.False(row.IsEnabled));
        Assert.Equal(WordMenuActionKind.Unknown, rows[0].Kind);
        Assert.False(WordActions.IsWordSearch(rows[0]));
        Assert.True(source.IsEnabled);
        Assert.False(WordActions.TryValidateRows(rows, out _, out _));
    }

    [Theory]
    [InlineData("\"Bogus\"")]
    [InlineData("999")]
    [InlineData("1")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Unknown_or_wrong_type_persisted_enums_use_safe_fallbacks(string json)
    {
        Assert.Equal(WordMenuActionKind.Unknown, JsonSerializer.Deserialize<WordMenuActionKind>(json));
        Assert.Equal(WordSearchModifier.Control, JsonSerializer.Deserialize<WordSearchModifier>(json));
        Assert.Equal(SubtitleAutoCopyTarget.Primary, JsonSerializer.Deserialize<SubtitleAutoCopyTarget>(json));
    }

    [Theory]
    [InlineData(WordSearchModifier.Control, KeyModifiers.Control, true)]
    [InlineData(WordSearchModifier.Control, KeyModifiers.Control | KeyModifiers.Shift, false)]
    [InlineData(WordSearchModifier.Control, KeyModifiers.Control | KeyModifiers.Alt, false)]
    [InlineData(WordSearchModifier.Control, KeyModifiers.Meta, false)]
    [InlineData(WordSearchModifier.None, KeyModifiers.None, true)]
    [InlineData(WordSearchModifier.None, KeyModifiers.Control, false)]
    [InlineData(WordSearchModifier.Alt, KeyModifiers.Alt, true)]
    [InlineData(WordSearchModifier.Shift, KeyModifiers.Shift, true)]
    [InlineData((WordSearchModifier)999, KeyModifiers.None, false)]
    public void Last_search_requires_exact_modifier_equality(WordSearchModifier expected, KeyModifiers actual, bool matches)
        => Assert.Equal(matches, WordActions.MatchesModifier(expected, actual));

    [Fact]
    public void Menu_invocation_captures_the_row_and_selection_independently()
    {
        WordMenuActionPref row = new() { Kind = WordMenuActionKind.Search, Title = "Original", Url = "https://example.test/?q=%w" };
        WordMenuInvocation invocation = new(row, Selection("A", "Context A"));
        row.Url = "https://other.test/?q=%w";
        row.Title = "Changed";
        Assert.Equal("Original", invocation.Action.Title);
        Assert.Equal("https://example.test/?q=%w", invocation.Action.Url);
        Assert.Equal("A", invocation.Selection.Word);
        // A consumer must not mutate the stored command by changing the returned mutable row either.
        invocation.Action.Url = "https://third.test/";
        Assert.Equal("https://example.test/?q=%w", invocation.Action.Url);
    }

    [Theory]
    [InlineData(SubtitleAutoCopyTarget.Primary, "one", "two", "one")]
    [InlineData(SubtitleAutoCopyTarget.Secondary, "one", "two", "two")]
    [InlineData(SubtitleAutoCopyTarget.All, "one", "", "one")]
    [InlineData(SubtitleAutoCopyTarget.All, "", "two", "two")]
    [InlineData(SubtitleAutoCopyTarget.All, " ", "\t", "")]
    public void Auto_copy_builds_only_nonempty_current_slots(SubtitleAutoCopyTarget target, string primary, string secondary, string expected)
        => Assert.Equal(expected, WordActions.BuildAutoCopyText(target, primary, secondary));

    [Fact]
    public void All_auto_copy_retains_wpf_dual_slot_format()
        => Assert.Equal("one" + Environment.NewLine + "( two )", WordActions.BuildAutoCopyText(SubtitleAutoCopyTarget.All, "one", "two"));

    [Fact]
    public void Last_word_search_ignores_disabled_and_sentence_only_rows()
    {
        Assert.True(WordActions.IsWordSearch(new() { Kind = WordMenuActionKind.Search, Title = "Word", Url = "https://example.test/?q=%lw" }));
        Assert.False(WordActions.IsWordSearch(new() { Kind = WordMenuActionKind.Search, Title = "Sentence", Url = "https://example.test/?q=%s" }));
        Assert.False(WordActions.IsWordSearch(new() { Kind = WordMenuActionKind.Search, Title = "Disabled", Url = "https://example.test/?q=%w", IsEnabled = false }));
    }
}
