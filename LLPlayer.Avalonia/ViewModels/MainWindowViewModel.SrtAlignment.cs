using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    SrtAlignmentViewModel? srtAlignment;

    bool CanShowSrtAlignment() => !disposed && (srtAlignment != null
        || !player.IsOpening && player is ISrtAlignmentController alignment && alignment.CanStartSrtAlignment(Sidebar.SelectedSlot));

    [RelayCommand(CanExecute = nameof(CanShowSrtAlignment))]
    void ShowSrtAlignment()
    {
        if (!CanShowSrtAlignment() || player is not ISrtAlignmentController alignment)
            return;

        try
        {
            srtAlignment ??= new SrtAlignmentViewModel(alignment.CaptureSrtAlignment(Sidebar.SelectedSlot), Sidebar.SelectedSlot);
            shell.ShowSrtAlignment(srtAlignment);
        }
        catch (Exception error)
        {
            ShowToast("Could not open subtitle alignment", error.Message, ToastVariant.Destructive);
        }
    }

    internal void SrtAlignmentClosed(SrtAlignmentViewModel closed)
    {
        if (!ReferenceEquals(srtAlignment, closed))
            return;

        srtAlignment = null;
        ShowSrtAlignmentCommand.NotifyCanExecuteChanged();
    }

    void RefreshSrtAlignmentState()
    {
        srtAlignment?.RefreshSource();
        ShowSrtAlignmentCommand.NotifyCanExecuteChanged();
    }
}
