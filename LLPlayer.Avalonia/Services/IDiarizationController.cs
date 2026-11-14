using System.ComponentModel;

namespace LLPlayer.Avalonia.Services;

public interface IDiarizationController : INotifyPropertyChanged
{
    bool IsDiarizing { get; }
    bool CanCancelDiarization { get; }
    Task<int> AnalyzeSpeakersAsync(int slot);
    Task CancelDiarizationAsync();
}
