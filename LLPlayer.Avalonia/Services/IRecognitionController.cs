using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Services;

/// <summary>Typed recognition seam shared by the real controller and headless UI fakes.</summary>
public interface IRecognitionController
{
    event Action<string, string?, string?>? RecognitionError;
    RecognitionActions Recognition { get; }
    RecognitionSettingsStore? RecognitionStore { get; }
    string? RecognitionWarning { get; }
    RecognitionSettings RecognitionSettings { get; }
    string RecognitionStatus { get; }
    Task StartRecognitionAsync(int slot);
    Task CancelRecognitionAsync(int slot);
    Task OcrRecognitionAsync(int slot);
    Task ToggleRecognitionPauseAsync();
}
