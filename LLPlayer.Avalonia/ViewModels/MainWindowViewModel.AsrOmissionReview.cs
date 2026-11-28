using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    AsrOmissionReviewViewModel? asrOmissionReview;

    bool CanShowAsrOmissionReview() => !disposed && !player.IsOpening
        && player is IAsrOmissionReviewController review && review.CanReviewAsrOmissions(Sidebar.SelectedSlot);

    [RelayCommand(CanExecute = nameof(CanShowAsrOmissionReview))]
    async Task ShowAsrOmissionReviewAsync(CancellationToken cancellationToken)
    {
        if (!CanShowAsrOmissionReview() || player is not IAsrOmissionReviewController controller) return;
        if (asrOmissionReview != null)
        {
            shell.ShowAsrOmissionReview(asrOmissionReview);
            return;
        }
        int slot = Sidebar.SelectedSlot;
        try
        {
            AsrOmissionReviewSnapshot? snapshot = await controller.ReadAsrOmissionsAsync(slot, cancellationToken);
            if (!disposed && !cancellationToken.IsCancellationRequested && snapshot != null && snapshot.Slot == slot
                && slot == Sidebar.SelectedSlot && controller.IsAsrOmissionReviewCurrent(snapshot))
            {
                asrOmissionReview = new(snapshot);
                shell.ShowAsrOmissionReview(asrOmissionReview);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!disposed) ShowToast("Could not read ASR suggestions", error.Message, ToastVariant.Destructive);
        }
    }

    internal void AsrOmissionReviewClosed(AsrOmissionReviewViewModel closed)
    {
        if (ReferenceEquals(asrOmissionReview, closed)) asrOmissionReview = null;
    }

    void RefreshAsrOmissionReviewState()
    {
        if (asrOmissionReview is { } review && player is IAsrOmissionReviewController controller
            && !controller.IsAsrOmissionReviewCurrent(review.Snapshot))
            review.Invalidate();
        ShowAsrOmissionReviewCommand.NotifyCanExecuteChanged();
    }
}
