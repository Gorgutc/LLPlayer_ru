using System.Globalization;
using Avalonia.Headless.XUnit;
using FlyleafLib;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class SubtitleSearchTests
{
    static FakePlaybackController Player(params string[] texts)
    {
        FakePlaybackController player = new();
        for (int i = 0; i < texts.Length; i++)
            player.Cues[0].Add(new CueInfo(i, TimeSpan.FromSeconds(i * 2), TimeSpan.FromSeconds(i * 2 + 1), texts[i]));
        return player;
    }

    static SubtitlesSidebarViewModel Sidebar(FakePlaybackController player)
    {
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.Reload();
        return sidebar;
    }

    [Fact]
    public void Query_is_trimmed_and_whitespace_restores_all_cues()
    {
        SubtitlesSidebarViewModel sidebar = Sidebar(Player("cat", "dog", ""));
        sidebar.FilterText = "  CAT  ";
        Assert.Equal(new[] { 0 }, sidebar.Cues.Select(cue => cue.Index));
        Assert.Equal("1 / 3", sidebar.CountText);

        sidebar.FilterText = " \t ";
        Assert.Equal(new[] { 0, 1, 2 }, sidebar.Cues.Select(cue => cue.Index));
        Assert.Equal("3 cues", sidebar.CountText);
    }

    [Theory]
    [InlineData(false, false, false, "0,1,2")]
    [InlineData(true, false, false, "1,2")]
    [InlineData(false, true, false, "0,2")]
    [InlineData(true, true, false, "2")]
    [InlineData(false, false, true, "0,1,2")]
    [InlineData(true, false, true, "1,2")]
    [InlineData(false, true, true, "0,2")]
    [InlineData(true, true, true, "2")]
    public void Search_modes_combine_without_changing_query(bool matchCase, bool wholeWord, bool regex, string expected)
    {
        SubtitlesSidebarViewModel sidebar = Sidebar(Player("CAT", "category", "cat", "dog"));
        sidebar.MatchCase = matchCase;
        sidebar.WholeWord = wholeWord;
        sidebar.UseRegex = regex;
        sidebar.FilterText = "cat";
        Assert.Equal(expected, string.Join(',', sidebar.Cues.Select(cue => cue.Index)));
        Assert.Equal("cat", sidebar.FilterText);
    }

    [Fact]
    public void Changing_case_and_whole_word_options_refilters_active_query()
    {
        SubtitlesSidebarViewModel sidebar = Sidebar(Player("CAT", "category", "cat"));
        sidebar.FilterText = "cat";
        sidebar.MatchCase = true;
        Assert.Equal(new[] { 1, 2 }, sidebar.Cues.Select(cue => cue.Index));
        sidebar.WholeWord = true;
        Assert.Equal(new[] { 2 }, sidebar.Cues.Select(cue => cue.Index));
        sidebar.MatchCase = false;
        Assert.Equal(new[] { 0, 2 }, sidebar.Cues.Select(cue => cue.Index));
        sidebar.WholeWord = false;
        Assert.Equal(new[] { 0, 1, 2 }, sidebar.Cues.Select(cue => cue.Index));
    }

    [Fact]
    public void Changing_regex_option_refilters_literal_query_as_pattern()
    {
        SubtitlesSidebarViewModel sidebar = Sidebar(Player("cat", "cot", "ca."));
        sidebar.FilterText = "c.t";
        Assert.Empty(sidebar.Cues);
        sidebar.UseRegex = true;
        Assert.Equal(new[] { 0, 1 }, sidebar.Cues.Select(cue => cue.Index));
        sidebar.UseRegex = false;
        Assert.Empty(sidebar.Cues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Whole_word_escapes_literal_metacharacters_and_uses_Unicode_boundaries(bool matchCase)
    {
        SubtitlesSidebarViewModel sidebar = Sidebar(Player("an a.b token", "an axb token", "кот спит", "котёнок"));
        sidebar.WholeWord = true;
        sidebar.MatchCase = matchCase;
        sidebar.FilterText = "a.b";
        Assert.Equal(new[] { 0 }, sidebar.Cues.Select(cue => cue.Index));
        sidebar.FilterText = "кот";
        Assert.Equal(new[] { 2 }, sidebar.Cues.Select(cue => cue.Index));
    }

    [Fact]
    public void Whole_word_regex_groups_alternation_and_honors_case()
    {
        SubtitlesSidebarViewModel sidebar = Sidebar(Player("a dog", "doghouse", "zbc", "BC"));
        sidebar.UseRegex = true;
        sidebar.WholeWord = true;
        sidebar.FilterText = "dog|bc";
        Assert.Equal(new[] { 0, 3 }, sidebar.Cues.Select(cue => cue.Index));
        sidebar.MatchCase = true;
        Assert.Equal(new[] { 0 }, sidebar.Cues.Select(cue => cue.Index));
    }

    [Fact]
    public void Plain_matching_uses_Wpf_ordinal_semantics_under_Turkish_culture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            SubtitlesSidebarViewModel sidebar = Sidebar(Player("FILE", "file", "fıle"));
            sidebar.FilterText = "file";
            Assert.Equal(new[] { 0, 1 }, sidebar.Cues.Select(cue => cue.Index));
            sidebar.MatchCase = true;
            Assert.Equal(new[] { 1 }, sidebar.Cues.Select(cue => cue.Index));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Invalid_regex_has_zero_results_and_recovers_when_pattern_or_mode_changes()
    {
        SubtitlesSidebarViewModel sidebar = Sidebar(Player("a ( bracket", "cat"));
        sidebar.FilterText = "(";
        Assert.Single(sidebar.Cues);
        Assert.False(sidebar.HasSearchError);
        sidebar.UseRegex = true;
        Assert.Empty(sidebar.Cues);
        Assert.True(sidebar.HasSearchError);
        Assert.Equal("Invalid regex", sidebar.SearchError);
        Assert.Equal("Invalid regex", sidebar.CountText);

        sidebar.FilterText = "^cat$";
        Assert.Equal(new[] { 1 }, sidebar.Cues.Select(cue => cue.Index));
        Assert.False(sidebar.HasSearchError);
        Assert.Empty(sidebar.SearchError);
        sidebar.FilterText = "(";
        sidebar.UseRegex = false;
        Assert.Equal(new[] { 0 }, sidebar.Cues.Select(cue => cue.Index));
        Assert.False(sidebar.HasSearchError);
    }

    [Fact]
    public void Clearing_invalid_query_restores_all_cues_and_preserves_search_options()
    {
        SubtitlesSidebarViewModel sidebar = Sidebar(Player("cat", ""));
        sidebar.MatchCase = true;
        sidebar.WholeWord = true;
        sidebar.UseRegex = true;
        sidebar.FilterText = "(";
        Assert.True(sidebar.HasSearchError);
        sidebar.ClearFilterCommand.Execute(null);
        Assert.Empty(sidebar.FilterText);
        Assert.Equal(2, sidebar.Cues.Count);
        Assert.False(sidebar.HasSearchError);
        Assert.True(sidebar.MatchCase && sidebar.WholeWord && sidebar.UseRegex);
        sidebar.FilterText = "(";
        sidebar.FilterText = " \t ";
        Assert.Equal(2, sidebar.Cues.Count);
        Assert.False(sidebar.HasSearchError);
    }

    [AvaloniaFact]
    public void Search_follows_original_display_choice_and_searches_through_visual_mask()
    {
        FakePlaybackController player = Player("placeholder", "unrelated");
        player.Cues[0][0] = new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Translated cat", true, "en", "ru", "Original dog");
        SubtitlesSidebarViewModel sidebar = Sidebar(player);
        sidebar.ApplyAppearance(SidebarTextAppearance.Default with { TextMask = true });
        sidebar.UseRegex = true;
        sidebar.WholeWord = true;
        sidebar.FilterText = "cat";
        Assert.True(Assert.Single(sidebar.Cues).IsMasked);
        Assert.Equal("______________", sidebar.Cues[0].DisplayText);
        sidebar.ApplyAppearance(SidebarTextAppearance.Default with { TextMask = true, ShowOriginalText = true });
        Assert.Empty(sidebar.Cues);
        sidebar.FilterText = "dog";
        Assert.True(Assert.Single(sidebar.Cues).IsMasked);
        Assert.Equal("____________", sidebar.Cues[0].DisplayText);
        Assert.False(sidebar.Cues[0].IsDisplayTranslated);
    }

    [Fact]
    public void Reload_refilters_translation_updates_and_appends_while_preserving_existing_rows()
    {
        FakePlaybackController player = Player("cat", "dog");
        SubtitlesSidebarViewModel sidebar = Sidebar(player);
        CueItemViewModel first = sidebar.Cues[0];
        sidebar.UseRegex = true;
        sidebar.FilterText = "^cat$";
        Assert.Same(first, Assert.Single(sidebar.Cues));
        player.Cues[0][1] = player.Cues[0][1] with { Text = "cat", IsTranslated = true, OriginalText = "dog" };
        player.Cues[0].Add(new CueInfo(2, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), "cat"));
        sidebar.Reload();
        Assert.Equal(new[] { 0, 1, 2 }, sidebar.Cues.Select(cue => cue.Index));
        Assert.Same(first, sidebar.Cues[0]);
        player.Cues[0][0] = player.Cues[0][0] with { Text = "CAT" };
        sidebar.MatchCase = true;
        sidebar.Reload();
        Assert.Equal(new[] { 1, 2 }, sidebar.Cues.Select(cue => cue.Index));
    }

    [Fact]
    public void Search_keeps_primary_only_slot_policy()
    {
        FakePlaybackController player = Player("dog");
        player.Cues[1].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "cat"));
        SubtitlesSidebarViewModel sidebar = Sidebar(player);
        sidebar.UseRegex = true;
        sidebar.FilterText = "cat";
        Assert.Empty(sidebar.Cues);
        Assert.Equal(1, sidebar.TotalCount);
        Assert.Equal("0 / 1", sidebar.CountText);
    }

    [Fact]
    public void Next_previous_match_wrap_repeated_requests_and_never_request_keyboard_focus()
    {
        FakePlaybackController player = Player("cat", "dog", "cat", "dog", "cat");
        player.PrimaryCueIndex = 1;
        SubtitlesSidebarViewModel sidebar = Sidebar(player);
        int focusRequests = 0;
        sidebar.FocusFilterRequested += () => focusRequests++;
        sidebar.FilterText = "cat";
        sidebar.NextMatchCommand.Execute(null);
        sidebar.NextMatchCommand.Execute(null);
        sidebar.NextMatchCommand.Execute(null);
        sidebar.PreviousMatchCommand.Execute(null);
        Assert.Equal(new[] { TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.Zero, TimeSpan.FromSeconds(8) }, player.Seeks);
        Assert.Equal(0, focusRequests);

        sidebar.UpdateCurrent(2);
        sidebar.PreviousMatchCommand.Execute(null);
        Assert.Equal(TimeSpan.Zero, player.Seeks[^1]);
    }

    [Fact]
    public void Navigation_without_current_cue_starts_at_first_or_last_match()
    {
        FakePlaybackController player = Player("dog", "cat", "cat");
        SubtitlesSidebarViewModel sidebar = Sidebar(player);
        sidebar.FilterText = "cat";
        sidebar.NextMatchCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(player.Seeks));

        sidebar.FilterText = "dog";
        sidebar.FilterText = "cat";
        sidebar.PreviousMatchCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(4), player.Seeks[^1]);
    }

    [Fact]
    public void Reload_and_query_changes_reset_navigation_to_playback_anchor()
    {
        FakePlaybackController player = Player("cat", "dog", "cat", "dog", "cat");
        player.PrimaryCueIndex = 1;
        SubtitlesSidebarViewModel sidebar = Sidebar(player);
        sidebar.FilterText = "cat";
        sidebar.NextMatchCommand.Execute(null);
        sidebar.NextMatchCommand.Execute(null);
        sidebar.Reload();
        sidebar.NextMatchCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(4), player.Seeks[^1]);
        sidebar.NextMatchCommand.Execute(null);
        sidebar.FilterText = "ca";
        sidebar.NextMatchCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(4), player.Seeks[^1]);
    }

    [Fact]
    public void Empty_and_invalid_search_results_do_not_seek()
    {
        FakePlaybackController player = Player("cat");
        SubtitlesSidebarViewModel sidebar = Sidebar(player);
        sidebar.FilterText = "no match";
        sidebar.NextMatchCommand.Execute(null);
        sidebar.PreviousMatchCommand.Execute(null);
        sidebar.UseRegex = true;
        sidebar.FilterText = "(";
        sidebar.NextMatchCommand.Execute(null);
        sidebar.PreviousMatchCommand.Execute(null);
        Assert.Empty(player.Seeks);
    }

    [Fact]
    public void Timeout_overload_reports_timeout_without_changing_existing_match_contract()
    {
        SubtitleSearcher matcher = SubtitleSearcher.TryCreate("(a+)+$", false, false, true)!;
        Assert.True(matcher.IsMatch("aaa", out bool normalTimeout));
        Assert.False(normalTimeout);
        string pathological = new('a', 20000);
        Assert.False(matcher.IsMatch(pathological + "!", out bool timedOut));
        Assert.True(timedOut);
        Assert.False(matcher.IsMatch(pathological + "!"));
    }

    [Fact]
    public void Regex_timeout_discards_partial_results_and_recovers_on_next_query()
    {
        FakePlaybackController player = Player("aaa", new string('a', 20000) + "!", "aaa");
        SubtitlesSidebarViewModel sidebar = Sidebar(player);
        sidebar.UseRegex = true;
        sidebar.FilterText = "(a+)+$";
        Assert.Empty(sidebar.Cues);
        Assert.True(sidebar.HasSearchError);
        Assert.Equal("Search timed out", sidebar.SearchError);
        Assert.Equal("Search timed out", sidebar.CountText);
        sidebar.NextMatchCommand.Execute(null);
        Assert.Empty(player.Seeks);

        sidebar.FilterText = "^aaa$";
        Assert.Equal(new[] { 0, 2 }, sidebar.Cues.Select(cue => cue.Index));
        Assert.False(sidebar.HasSearchError);
        Assert.Empty(sidebar.SearchError);
    }
}
