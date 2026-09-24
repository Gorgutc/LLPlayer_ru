using AwesomeAssertions;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class SidebarAndPopupTests
{
    static FakePlaybackController PlayerWithCues()
    {
        FakePlaybackController player = new();
        player.Cues[0].AddRange(
        [
            new CueInfo(0, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3), "Hello world, this is a test."),
            new CueInfo(1, TimeSpan.FromSeconds(3.5), TimeSpan.FromSeconds(6.5), "The quick brown fox jumps over the lazy dog."),
            new CueInfo(2, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(11), "Language learning with subtitles."),
        ]);
        return player;
    }

    [Fact]
    public void Sidebar_ListsCuesWithTimes_AndSeeksToCueStart()
    {
        FakePlaybackController player = PlayerWithCues();
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.Reload();

        sidebar.Cues.Select(c => c.StartText).Should().Equal("00:00.5", "00:03.5", "00:07.0");
        sidebar.CountText.Should().Be("3 cues");

        sidebar.SeekCommand.Execute(sidebar.Cues[1]);
        player.Seeks.Should().Equal(TimeSpan.FromSeconds(3.5));
    }

    [Fact]
    public void Sidebar_TracksCurrentCue()
    {
        FakePlaybackController player = PlayerWithCues();
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.Reload();

        sidebar.UpdateCurrent(2);
        sidebar.CurrentCue!.Index.Should().Be(2);
        sidebar.Cues.Count(c => c.IsCurrent).Should().Be(1);

        sidebar.UpdateCurrent(-1);
        sidebar.CurrentCue.Should().BeNull();
        sidebar.Cues.Should().OnlyContain(c => !c.IsCurrent);
    }

    [Fact]
    public void Sidebar_FilterIsCaseInsensitive_AndClears()
    {
        SubtitlesSidebarViewModel sidebar = new(PlayerWithCues());
        sidebar.Reload();

        sidebar.FilterText = "FOX";
        sidebar.Cues.Should().ContainSingle().Which.Index.Should().Be(1);
        sidebar.CountText.Should().Be("1 / 3");

        sidebar.ClearFilterCommand.Execute(null);
        sidebar.Cues.Should().HaveCount(3);
    }

    [Fact]
    public void Sidebar_ReloadAppendsNewCues_AndRebuildsOnReplace()
    {
        FakePlaybackController player = PlayerWithCues();
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.Reload();
        CueItemViewModel first = sidebar.Cues[0];

        player.Cues[0].Add(new CueInfo(3, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(13), "ASR line"));
        sidebar.Reload();
        sidebar.Cues.Should().HaveCount(4);
        sidebar.Cues[0].Should().BeSameAs(first, "appending (ASR / OCR) keeps the existing rows");

        player.Cues[0].Clear();
        player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "other file"));
        sidebar.Reload();
        sidebar.Cues.Should().ContainSingle().Which.Text.Should().Be("other file");
    }

    [Fact]
    public async Task WordPopup_ShowsTranslation()
    {
        FakeTranslator translator = new();
        List<string> copied = [];
        WordPopupViewModel popup = new(translator, copied.Add);

        await popup.OpenAsync("fox", "The quick brown fox", 0, 1, 2);
        popup.IsOpen.Should().BeTrue();
        popup.Translation.Should().Be("[fox]");
        popup.IsBusy.Should().BeFalse();
        popup.Status.Should().BeEmpty();

        popup.CopyCommand.Execute(null);
        copied.Should().Equal("fox");
    }

    [Fact]
    public async Task WordPopup_NotConfigured_IsExplained()
    {
        FakeTranslator translator = new((_, _, _) => Task.FromResult(new WordTranslationResult(WordTranslationStatus.NotConfigured, null, "Word translation is off")));
        WordPopupViewModel popup = new(translator, _ => { });

        await popup.OpenAsync("fox", "", 0, 0, 0);
        popup.Translation.Should().BeNull();
        popup.IsNotConfigured.Should().BeTrue();
        popup.Status.Should().Be("Translation not configured: Word translation is off");
    }

    [Fact]
    public async Task WordPopup_NewClickSupersedesPendingLookup()
    {
        TaskCompletionSource<WordTranslationResult> slow = new();
        FakeTranslator translator = new((word, _, _) => word == "slow"
            ? slow.Task
            : Task.FromResult(new WordTranslationResult(WordTranslationStatus.Translated, "fast!")));
        WordPopupViewModel popup = new(translator, _ => { });

        Task first = popup.OpenAsync("slow", "", 0, 0, 0);
        await popup.OpenAsync("quick", "", 0, 0, 0);
        slow.SetResult(new WordTranslationResult(WordTranslationStatus.Translated, "stale"));
        await first;

        popup.Word.Should().Be("quick");
        popup.Translation.Should().Be("fast!", "a superseded lookup must not overwrite the newer popup");
    }
}
