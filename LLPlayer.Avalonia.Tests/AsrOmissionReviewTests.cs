using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public sealed class AsrOmissionReviewTests
{
    [AvaloniaFact]
    public async Task Detached_review_uses_selected_slot_and_invalidates_stale_rows()
    {
        var player = new ReviewPlayer();
        var shell = new ReviewShell();
        using var vm = new MainWindowViewModel(player, new AppPrefs(), _ => { }, new FakeTranslator(), shell, null);
        Assert.False(vm.ShowAsrOmissionReviewCommand.CanExecute(null));

        vm.Sidebar.SelectedSlot = 1;
        Assert.True(vm.ShowAsrOmissionReviewCommand.CanExecute(null));
        await vm.ShowAsrOmissionReviewCommand.ExecuteAsync(null);
        var review = Assert.IsType<AsrOmissionReviewViewModel>(shell.Model);
        var row = Assert.Single(review.Rows);
        Assert.Equal(1, player.RequestedSlot);
        Assert.Equal("Secondary subtitle track", review.TrackLabel);
        Assert.Equal("Omission search: On", review.ModeLabel);
        Assert.Equal("00:00:01.234 – 00:00:02.750", row.Timing);
        Assert.Equal("proposed words", row.Text);
        Assert.True(row.IsReviewOnly);
        Assert.Contains("part of a recognized line", row.ReviewLabel);
        Assert.Empty(player.GetCues(1));
        Assert.Empty(vm.Sidebar.Cues);

        var window = new AsrOmissionReviewWindow(review);
        Assert.NotNull(window.FindControl<ListBox>("OmissionRows"));
        Assert.NotNull(window.FindControl<TextBlock>("OmissionStatus"));
        player.Current = false;
        player.RaiseCuesChanged(1);
        Assert.Empty(review.Rows);
        Assert.Contains("changed", review.StatusMessage);
        Assert.False(vm.ShowAsrOmissionReviewCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Late_report_for_old_slot_is_not_shown()
    {
        var response = new TaskCompletionSource<AsrOmissionReviewSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new ReviewPlayer { Response = response.Task };
        var shell = new ReviewShell();
        using var vm = new MainWindowViewModel(player, new AppPrefs(), _ => { }, new FakeTranslator(), shell, null);
        vm.Sidebar.SelectedSlot = 1;

        Task read = vm.ShowAsrOmissionReviewCommand.ExecuteAsync(null);
        Assert.Equal(1, player.RequestedSlot);
        vm.Sidebar.SelectedSlot = 0;
        response.SetResult(player.Snapshot);
        await read;

        Assert.Null(shell.Model);
        Assert.Empty(player.GetCues(0));
        Assert.Empty(player.GetCues(1));
    }

    sealed class ReviewPlayer : FakePlaybackController, IAsrOmissionReviewController
    {
        public bool Current { get; set; } = true;
        public int? RequestedSlot { get; private set; }
        public Task<AsrOmissionReviewSnapshot?>? Response { get; init; }
        public AsrOmissionReviewSnapshot Snapshot { get; } = new(1, true, "Analyzed",
            [new("proposed words", 1234, 2750, true, "partialDonorCue")], new object());

        public bool CanReviewAsrOmissions(int slot) => Current && slot == Snapshot.Slot;
        public bool IsAsrOmissionReviewCurrent(AsrOmissionReviewSnapshot snapshot)
            => Current && ReferenceEquals(Snapshot, snapshot);
        public Task<AsrOmissionReviewSnapshot?> ReadAsrOmissionsAsync(int slot, CancellationToken cancellationToken)
        {
            RequestedSlot = slot;
            return Response ?? Task.FromResult<AsrOmissionReviewSnapshot?>(Snapshot);
        }
    }

    sealed class ReviewShell : IAppShell
    {
        public AsrOmissionReviewViewModel? Model { get; private set; }
        public bool IsFullScreen { get; set; }
        public void ShowAsrOmissionReview(AsrOmissionReviewViewModel model) => Model = model;
        public Task<string?> PickMediaFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public Task<string?> PickSubtitlesFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public void ShowCheatSheet() { }
        public void SetClipboardText(string text) { }
        public void ApplyTheme(string theme) { }
        public void Exit() { }
    }
}
