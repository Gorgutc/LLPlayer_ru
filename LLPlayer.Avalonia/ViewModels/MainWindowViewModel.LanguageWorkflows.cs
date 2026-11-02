using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer.Dubbing;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public Func<AiInsightsViewModel>? AiInsightsDialog { get; set; }
    public Func<RecognitionViewModel>? RecognitionDialog { get; set; }
    public Func<DubbingViewModel>? DubbingDialog { get; set; }
    public Func<BatchSubtitlesViewModel>? BatchDialog { get; set; }
    public Func<IReadOnlyList<TtsVoice>>? DubbingVoices { get; set; }
    public Func<CueItemViewModel, VoicePickerViewModel?>? VoicePickerDialog { get; set; }
    AiInsightsViewModel? insights;

    [RelayCommand]
    void ShowAiInsights()
    {
        if (disposed || AiInsightsDialog == null) return;
        insights ??= AiInsightsDialog();
        insights.Refresh();
        shell.ShowAiInsights(insights);
    }

    public void AiInsightsClosed() => insights = null;
    public Task DrainAiInsightsAsync() => insights?.DisposeAsync().AsTask() ?? Task.CompletedTask;

    [RelayCommand]
    void ShowDubbingSettings()
    {
        if (!disposed && DubbingDialog != null) shell.ShowDubbing(DubbingDialog());
    }

    [RelayCommand]
    void ShowBatchSubtitles()
    {
        if (!disposed && BatchDialog != null) shell.ShowBatch(BatchDialog());
    }

    [RelayCommand]
    void AssignCueVoice(CueItemViewModel? row)
    {
        if (disposed || row == null || !Sidebar.IsCurrentRow(row)) return;
        if (VoicePickerDialog?.Invoke(row) is { } model) shell.ShowVoicePicker(model);
    }
}
