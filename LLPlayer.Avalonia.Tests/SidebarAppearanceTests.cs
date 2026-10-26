using Avalonia;
using Avalonia.Media;
using AwesomeAssertions;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Sidebar cue rows: display text selection, spoiler mask, search source, typography and live updates.</summary>
public class SidebarAppearanceTests
{
    static FakePlaybackController PlayerWithCues()
    {
        FakePlaybackController player = new();
        player.Cues[0].AddRange(
        [
            new CueInfo(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Hello there", false, "en", "ru", "Hello there"),
            new CueInfo(1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "Second line", false, "en", "ru", "Second line"),
            new CueInfo(2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "Third spoiler", false, "en", "ru", "Third spoiler"),
        ]);
        return player;
    }

    static SidebarTextAppearance Appearance(bool mask = false, bool original = false, int padding = 5,
        string family = "Segoe UI", string weight = "Normal", double size = 16)
        => new(mask, original, padding, family, weight, size);

    [Fact]
    public void Mask_hides_only_future_cues_and_uses_displayed_text_length()
    {
        FakePlaybackController player = PlayerWithCues();
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.ApplyAppearance(Appearance(mask: true));
        sidebar.Reload();
        sidebar.UpdateCurrent(1);

        sidebar.Cues[0].DisplayText.Should().Be("Hello there", "past cues stay readable");
        sidebar.Cues[1].DisplayText.Should().Be("Second line", "the current cue stays readable");
        sidebar.Cues[1].IsMasked.Should().BeFalse();
        sidebar.Cues[2].IsMasked.Should().BeTrue();
        sidebar.Cues[2].DisplayText.Should().Be(new string('_', "Third spoiler".Length));
        sidebar.Cues[2].Cue.Text.Should().Be("Third spoiler", "the source data is never overwritten");

        sidebar.UpdateCurrent(-1);
        sidebar.Cues.Should().OnlyContain(c => c.IsMasked && c.DisplayText.Length == c.Cue.Text.Length);

        sidebar.UpdateCurrent(2);
        sidebar.Cues[2].IsMasked.Should().BeFalse();
    }

    [Fact]
    public void Mask_uses_original_length_when_original_text_is_shown()
    {
        FakePlaybackController player = PlayerWithCues();
        player.Cues[0][2] = new CueInfo(2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "Перевод", true, "en", "ru", "Third spoiler");
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.ApplyAppearance(Appearance(mask: true, original: true));
        sidebar.Reload();
        sidebar.UpdateCurrent(1);
        sidebar.Cues[2].DisplayText.Should().Be(new string('_', "Third spoiler".Length));
    }

    [Fact]
    public void ShowOriginalText_replaces_translated_display_text_and_lookup_flag()
    {
        FakePlaybackController player = PlayerWithCues();
        player.Cues[0][1] = new CueInfo(1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "Переведённая строка", true, "en", "ru", "Second line");
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.ApplyAppearance(Appearance(original: false));
        sidebar.Reload();
        sidebar.Cues[1].DisplayText.Should().Be("Переведённая строка");
        sidebar.Cues[1].IsDisplayTranslated.Should().BeTrue();
        sidebar.Cues[1].OriginalText.Should().Be("Second line");

        sidebar.ApplyAppearance(Appearance(original: true));
        sidebar.Cues[1].DisplayText.Should().Be("Second line");
        sidebar.Cues[1].IsDisplayTranslated.Should().BeFalse("the displayed original text must be looked up as source text");

        player.Cues[0][1] = new CueInfo(1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "Still original", false, "en", "ru", "Still original");
        sidebar.Reload();
        sidebar.Cues[1].DisplayText.Should().Be("Still original");
        sidebar.Cues[1].IsDisplayTranslated.Should().BeFalse();
    }

    [Fact]
    public void Search_uses_the_selected_original_or_display_text_and_refilters_when_the_selection_changes()
    {
        FakePlaybackController player = PlayerWithCues();
        player.Cues[0][1] = new CueInfo(1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "Переведённая строка", true, "en", "ru", "Second line");
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.ApplyAppearance(Appearance(original: false));
        sidebar.Reload();

        sidebar.FilterText = "Переведённая";
        sidebar.Cues.Should().ContainSingle().Which.Index.Should().Be(1);

        sidebar.ApplyAppearance(Appearance(original: true));
        sidebar.FilterText.Should().Be("Переведённая");
        sidebar.Cues.Should().BeEmpty("the active filter must be re-evaluated against the original text");
        sidebar.FilterText = "second";
        sidebar.Cues.Should().ContainSingle().Which.Index.Should().Be(1);

        sidebar.FilterText = "";
        sidebar.UpdateCurrent(0);
        sidebar.Cues.Should().HaveCount(3);
    }

    [Fact]
    public void Masked_rows_hide_their_source_text_from_their_word_lookup_context()
    {
        FakePlaybackController player = PlayerWithCues();
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.ApplyAppearance(Appearance(mask: true));
        sidebar.Reload();
        sidebar.UpdateCurrent(1);

        sidebar.Cues[2].DisplayText.Should().Be(new string('_', "Third spoiler".Length));
        sidebar.Cues[2].LookupText.Should().Be(sidebar.Cues[2].DisplayText,
            "word lookup must receive what is rendered, not the hidden source text");
        sidebar.Cues[2].IsDisplayTranslated.Should().BeFalse();
    }

    [Fact]
    public void Typography_applies_only_to_cue_rows_and_keeps_the_overlay_fonts()
    {
        FakePlaybackController player = PlayerWithCues();
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.ApplyAppearance(Appearance(padding: 12, family: "Noto Sans", weight: "Bold", size: 22));
        sidebar.Reload();

        SubtitleFontSelection expected = new SubtitleFontCatalog().ResolveOrRequested("Noto Sans", "Bold", "Normal");
        sidebar.Cues.Should().OnlyContain(c =>
            c.RowFontSize == 22 && c.RowFontWeight == FontWeight.Bold &&
            c.RowFontFamily.Name == expected.Family.Name && c.RowPadding == new Thickness(12));

        var (main, _, _, _, _) = TestVm.Create();
        using var owned = main;
        main.SubtitleFontSize.Should().Be(34, "the video overlay font is independent from the sidebar");
        main.SidebarListFontSize.Should().Be(16);
        main.SidebarRowPadding.Should().Be(new Thickness(5));
    }

    [Fact]
    public void Translation_and_append_updates_refresh_rows_without_losing_row_identity()
    {
        FakePlaybackController player = PlayerWithCues();
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.ApplyAppearance(Appearance());
        sidebar.Reload();
        CueItemViewModel second = sidebar.Cues[1];
        sidebar.UpdateCurrent(1);

        player.Cues[0][1] = new CueInfo(1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "Переведённая строка", true, "en", "ru", "Second line");
        player.PrimaryCueIndex = 1;
        sidebar.Reload();
        sidebar.Cues[1].Should().BeSameAs(second, "a translated text flip must keep the row object (scroll/selection)");
        sidebar.Cues[1].DisplayText.Should().Be("Переведённая строка");
        sidebar.Cues[1].IsDisplayTranslated.Should().BeTrue();
        sidebar.CurrentCue.Should().BeSameAs(second);

        player.Cues[0].Add(new CueInfo(3, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(8), "ASR line", false, "en", "ru", "ASR line"));
        sidebar.Reload();
        sidebar.Cues.Should().HaveCount(4);
        sidebar.Cues[1].Should().BeSameAs(second);
        sidebar.CurrentCue.Should().BeSameAs(second);

        player.Cues[0].Clear();
        player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(9), "replacement", false, "en", "ru", "replacement"));
        sidebar.Reload();
        sidebar.Cues.Should().ContainSingle().Which.DisplayText.Should().Be("replacement");
    }
}
