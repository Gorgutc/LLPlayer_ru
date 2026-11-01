using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using Avalonia.Headless.XUnit;

namespace LLPlayer.Avalonia.Tests;

public class SidebarSlotTests
{
    [Fact]
    public void Seek_rejects_an_unrelated_row_even_when_index_and_timing_match()
    {
        FakePlaybackController player = new();
        CueInfo cue = new(0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Current cue");
        player.Cues[0].Add(cue);
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.Reload();
        CueItemViewModel unrelated = new(cue);

        sidebar.SeekCommand.Execute(unrelated);

        Assert.Empty(player.Seeks);
    }

    static FakePlaybackController PlayerWithBothSlots()
    {
        FakePlaybackController player = new();
        player.Cues[0].Add(new CueInfo(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Primary", false, "en", "ru", "Primary"));
        player.Cues[1].AddRange([
            new CueInfo(0, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "Translated cat", true, "fr", "de", "Original dog"),
            new CueInfo(1, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(8), "Second cat", false, "fr", "de", "Second cat"),
        ]);
        return player;
    }

    [Fact]
    public void Secondary_selection_uses_its_cues_current_index_and_seek_slot()
    {
        FakePlaybackController player = PlayerWithBothSlots();
        player.PrimaryCueIndex = -1;
        player.SecondaryCueIndex = 1;
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.Reload();
        Assert.Equal("Primary", Assert.Single(sidebar.Cues).Text);
        sidebar.SelectedSlot = 1;
        Assert.Equal(new[] { "Translated cat", "Second cat" }, sidebar.Cues.Select(row => row.Text));
        Assert.All(sidebar.Cues, row => Assert.Equal(1, row.Slot));
        Assert.Equal(1, sidebar.CurrentCue!.Index);
        Assert.Equal("2 cues", sidebar.CountText);
        sidebar.SeekCommand.Execute(sidebar.Cues[0]);
        Assert.Equal((TimeSpan.FromSeconds(5), 1), Assert.Single(player.SubtitleSeeks));
    }

    [AvaloniaFact]
    public void Secondary_search_original_text_and_mask_share_selected_slot_state()
    {
        FakePlaybackController player = PlayerWithBothSlots();
        player.PrimaryCueIndex = 1;
        player.SecondaryCueIndex = -1;
        SubtitlesSidebarViewModel sidebar = new(player) { SelectedSlot = 1 };
        sidebar.ApplyAppearance(SidebarTextAppearance.Default with { TextMask = true });
        sidebar.UseRegex = true;
        sidebar.WholeWord = true;
        sidebar.FilterText = "cat";
        Assert.Equal(2, sidebar.Cues.Count);
        Assert.All(sidebar.Cues, row => Assert.True(row.IsMasked));
        sidebar.ApplyAppearance(SidebarTextAppearance.Default with { TextMask = true, ShowOriginalText = true });
        Assert.Equal(1, Assert.Single(sidebar.Cues).Index);
        sidebar.FilterText = "dog";
        CueItemViewModel first = Assert.Single(sidebar.Cues);
        Assert.Equal(new string('_', "Original dog".Length), first.DisplayText);
        Assert.False(first.IsDisplayTranslated);
        sidebar.UpdateCurrent(0);
        Assert.Equal("Original dog", first.DisplayText);
        Assert.False(first.IsMasked);
        Assert.Equal("fr", first.SourceLanguage);
        Assert.Equal("de", first.TargetLanguage);
    }

    [Fact]
    public void Slot_switch_keeps_query_options_but_rebuilds_rows_and_navigation()
    {
        FakePlaybackController player = PlayerWithBothSlots();
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.Reload();
        CueItemViewModel primary = Assert.Single(sidebar.Cues);
        sidebar.MatchCase = true;
        sidebar.WholeWord = true;
        sidebar.UseRegex = true;
        sidebar.FilterText = "cat";
        sidebar.SelectedSlot = 1;
        sidebar.NextMatchCommand.Execute(null);
        sidebar.NextMatchCommand.Execute(null);
        sidebar.NextMatchCommand.Execute(null);
        Assert.Equal(new[] { 5d, 7d, 5d }, player.SubtitleSeeks.Select(seek => seek.Position.TotalSeconds));
        Assert.All(player.SubtitleSeeks, seek => Assert.Equal(1, seek.Slot));
        Assert.Equal("cat", sidebar.FilterText);
        Assert.True(sidebar.MatchCase && sidebar.WholeWord && sidebar.UseRegex);
        Assert.False(sidebar.IsCurrentRow(primary));
        sidebar.SelectedSlot = 0;
        sidebar.FilterText = "";
        Assert.NotSame(primary, Assert.Single(sidebar.Cues));
        sidebar.SeekCommand.Execute(primary);
        Assert.Equal(3, player.Seeks.Count);
    }

    [Fact]
    public async Task Old_slot_filtered_and_replaced_source_rows_cannot_seek_or_sync()
    {
        FakePlaybackController player = PlayerWithBothSlots();
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.Reload();
        CueItemViewModel primary = Assert.Single(sidebar.Cues);
        sidebar.SelectedSlot = 1;
        sidebar.SeekCommand.Execute(primary);
        await sidebar.SyncCommand.ExecuteAsync(primary);
        CueItemViewModel secondary = sidebar.Cues[0];
        sidebar.FilterText = "unmatched";
        sidebar.SeekCommand.Execute(secondary);
        await sidebar.SyncCommand.ExecuteAsync(secondary);
        sidebar.FilterText = "";
        player.ReplaceCueSource(1);
        Assert.False(sidebar.IsCurrentRow(secondary), "the source changed before its coalesced notification");
        sidebar.SeekCommand.Execute(secondary);
        await sidebar.SyncCommand.ExecuteAsync(secondary);
        sidebar.Reload();
        Assert.NotSame(secondary, sidebar.Cues[0]);
        Assert.Empty(player.Seeks);
        Assert.Empty(player.SynchronizedCues);
        await sidebar.SyncCommand.ExecuteAsync(sidebar.Cues[0]);
        Assert.Equal(1, Assert.Single(player.SynchronizedCues).Slot);
    }

    [Fact]
    public void Empty_selected_slot_clears_current_without_falling_back()
    {
        FakePlaybackController player = PlayerWithBothSlots();
        player.PrimaryCueIndex = 0;
        SubtitlesSidebarViewModel sidebar = new(player);
        sidebar.Reload();
        Assert.NotNull(sidebar.CurrentCue);
        player.Cues[1].Clear();
        sidebar.SelectedSlot = 1;
        Assert.Empty(sidebar.Cues);
        Assert.Null(sidebar.CurrentCue);
        Assert.Equal(1, sidebar.SelectedSlot);
        sidebar.NextMatchCommand.Execute(null);
        sidebar.PreviousMatchCommand.Execute(null);
        Assert.Empty(player.Seeks);
    }

    [Fact]
    public void Invalid_slot_indices_keep_the_last_valid_selection()
    {
        SubtitlesSidebarViewModel sidebar = new(PlayerWithBothSlots()) { SelectedSlot = 1 };
        sidebar.SelectedSlot = -1;
        sidebar.SelectedSlot = 2;
        Assert.Equal(1, sidebar.SelectedSlot);
        Assert.All(sidebar.Cues, row => Assert.Equal(1, row.Slot));
    }

    [AvaloniaFact]
    public void Secondary_events_update_only_the_selected_current_and_cue_list()
    {
        var (main, player, _, _, _) = TestVm.Create();
        using var owned = main;
        player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Primary"));
        player.Cues[1].Add(new CueInfo(0, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "Secondary"));
        main.Sidebar.SelectedSlot = 1;
        player.SecondaryCueIndex = 0;
        CueItemViewModel row = Assert.Single(main.Sidebar.Cues);
        Assert.Same(row, main.Sidebar.CurrentCue);
        player.PrimaryCueIndex = 0;
        player.Cues[0].Add(new CueInfo(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Primary append"));
        player.RaiseCuesChanged(0);
        Assert.Same(row, Assert.Single(main.Sidebar.Cues));
        Assert.Same(row, main.Sidebar.CurrentCue);
        player.Cues[1].Add(new CueInfo(1, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(8), "Secondary append"));
        player.RaiseCuesChanged(1);
        Assert.Equal(2, main.Sidebar.Cues.Count);
        Assert.Same(row, main.Sidebar.Cues[0]);
        player.SecondaryCueIndex = 1;
        Assert.Equal(1, main.Sidebar.CurrentCue!.Index);
        player.SecondaryCueIndex = -1;
        Assert.Null(main.Sidebar.CurrentCue);
    }

    [AvaloniaFact]
    public async Task Sync_failure_is_reported_without_seeking_or_changing_selection()
    {
        var (main, player, _, _, _) = TestVm.Create();
        using var owned = main;
        player.Cues[1].Add(new CueInfo(0, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "Secondary"));
        main.Sidebar.SelectedSlot = 1;
        player.SynchronizationError = new IOException("Cannot synchronize");
        await main.Sidebar.SyncCommand.ExecuteAsync(Assert.Single(main.Sidebar.Cues));
        Assert.Contains(main.Toasts, toast => toast.Message == "Cannot synchronize");
        Assert.Equal(1, main.Sidebar.SelectedSlot);
        Assert.Empty(player.Seeks);
    }

    [AvaloniaFact]
    public void Media_opening_retires_sidebar_rows_before_replacement_completes()
    {
        var (main, player, _, _, _) = TestVm.Create();
        using var owned = main;
        player.Cues[0].Add(new CueInfo(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Primary"));
        main.Sidebar.Reload();
        CueItemViewModel old = Assert.Single(main.Sidebar.Cues);
        player.IsOpening = true;
        Assert.Empty(main.Sidebar.Cues);
        Assert.Null(main.Sidebar.CurrentCue);
        main.Sidebar.SeekCommand.Execute(old);
        Assert.Empty(player.Seeks);
    }

    [Theory]
    [InlineData("slot")]
    [InlineData("source")]
    [InlineData("clear")]
    [InlineData("filter")]
    [InlineData("dispose")]
    public async Task Retiring_a_row_cancels_queued_sync_before_delay_mutation(string retirement)
    {
        FakePlaybackController player = PlayerWithBothSlots();
        using SubtitlesSidebarViewModel sidebar = new(player) { SelectedSlot = 1 };
        TaskCompletionSource queued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        long[] delays = [17, -23];
        player.SynchronizationAction = async (cue, slot, token) =>
        {
            queued.SetResult();
            await release.Task;
            token.ThrowIfCancellationRequested();
            delays[slot] = TimeSpan.FromSeconds(20).Ticks - cue.Start.Ticks;
        };
        List<string> errors = [];
        sidebar.SynchronizationFailed += errors.Add;
        Task sync = sidebar.SyncCommand.ExecuteAsync(sidebar.Cues[0]);
        await queued.Task;
        switch (retirement)
        {
            case "slot": sidebar.SelectedSlot = 0; break;
            case "source": player.ReplaceCueSource(1); sidebar.Reload(); break;
            case "clear": sidebar.Clear(); break;
            case "filter": sidebar.FilterText = "no match"; break;
            case "dispose": sidebar.Dispose(); break;
        }
        release.SetResult();
        await sync;
        Assert.Equal(new long[] { 17, -23 }, delays);
        Assert.Empty(errors);
        Assert.Empty(player.Seeks);
    }

    [Fact]
    public async Task Appending_cues_preserves_the_queued_current_source_sync()
    {
        FakePlaybackController player = PlayerWithBothSlots();
        using SubtitlesSidebarViewModel sidebar = new(player) { SelectedSlot = 1 };
        CueItemViewModel row = sidebar.Cues[0];
        TaskCompletionSource queued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool performed = false;
        player.SynchronizationAction = async (_, _, token) =>
        {
            queued.SetResult();
            await release.Task;
            token.ThrowIfCancellationRequested();
            performed = true;
        };
        Task sync = sidebar.SyncCommand.ExecuteAsync(row);
        await queued.Task;
        player.Cues[1].Add(new CueInfo(2, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10), "Appended"));
        sidebar.Reload();
        Assert.Same(row, sidebar.Cues[0]);
        release.SetResult();
        await sync;
        Assert.True(performed);
    }
}
