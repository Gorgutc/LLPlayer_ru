using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string? DiarizationStatus => player switch
    {
        IDiarizationController { IsDiarizing: true } => "Analyzing speaker labels…",
        IDiarizationController { CanCancelDiarization: true } => "Speaker analysis cleanup is incomplete. Choose Cancel speaker analysis to retry.",
        _ => null
    };

    void RefreshDiarizationCommandState()
    {
        OnPropertyChanged(nameof(DiarizationStatus));
        AnalyzeSpeakersCommand.NotifyCanExecuteChanged();
        CancelDiarizationCommand.NotifyCanExecuteChanged();
    }

    bool CanAnalyzeSpeakers(string? slot) => !disposed && (slot is "0" or "1") && player.IsOpened && !player.IsOpening
        && player is IDiarizationController { IsDiarizing: false, CanCancelDiarization: false };

    [RelayCommand(CanExecute = nameof(CanAnalyzeSpeakers))]
    async Task AnalyzeSpeakersAsync(string? slot)
    {
        if (!CanAnalyzeSpeakers(slot) || player is not IDiarizationController controller) return;
        int index = slot == "0" ? 0 : 1;
        string track = index == 0 ? "primary" : "secondary";
        try
        {
            int count = await controller.AnalyzeSpeakersAsync(index);
            if (disposed) return;
            string message = count switch
            {
                0 => $"No speaker labels matched the {track} subtitle cues.",
                1 => $"Labeled one {track} subtitle cue.",
                _ => $"Labeled {count} {track} subtitle cues."
            };
            ShowToast("Speaker analysis complete", message, ToastVariant.Success);
        }
        catch (OperationCanceledException)
        {
            if (!disposed) ShowToast("Speaker analysis cancelled");
        }
        catch (Exception error)
        {
            if (!disposed) ShowToast("Speaker analysis failed", error.Message, ToastVariant.Destructive);
        }
    }

    bool CanCancelDiarization() => !disposed && player is IDiarizationController { CanCancelDiarization: true };

    [RelayCommand(CanExecute = nameof(CanCancelDiarization))]
    async Task CancelDiarizationAsync()
    {
        if (!CanCancelDiarization() || player is not IDiarizationController controller) return;
        try { await controller.CancelDiarizationAsync(); }
        catch (Exception error)
        {
            if (!disposed) ShowToast("Could not stop speaker analysis", error.Message, ToastVariant.Destructive);
        }
    }
}
