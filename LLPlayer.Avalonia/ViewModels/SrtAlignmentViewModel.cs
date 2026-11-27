using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed record SrtAlignmentPreviewRow(SrtAlignmentRow Cue)
{
    public string Original => Format(Cue.OriginalStart, Cue.OriginalEnd);
    public string Proposed => Cue.ProposedStart is TimeSpan start && Cue.ProposedEnd is TimeSpan end
        ? Format(start, end) : "Keep original";
    public string Text => Cue.Text;
    public string Match => Cue.Reason ?? $"Matched {Cue.Coverage:P0}";

    static string Format(TimeSpan start, TimeSpan end) => $"{FormatTime(start)} – {FormatTime(end)}";
    static string FormatTime(TimeSpan t) => $"{(long)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
}

public sealed partial class SrtAlignmentViewModel : ObservableObject
{
    readonly ISrtAlignmentSession session;
    CancellationTokenSource? operation;
    Task? active;
    Task? activeDrain;
    bool closing;
    bool stale;
    bool cleanupRequired;

    [ObservableProperty] string language;
    [ObservableProperty] bool useCpu;
    [ObservableProperty] bool timingAcknowledged;
    [ObservableProperty] bool busy;
    [ObservableProperty] bool draining;
    [ObservableProperty] string message = "";
    [ObservableProperty] SrtAlignmentDraft? draft;
    [ObservableProperty] IReadOnlyList<SrtAlignmentPreviewRow> rows = Array.Empty<SrtAlignmentPreviewRow>();

    public SrtAlignmentViewModel(ISrtAlignmentSession session, int slot)
    {
        this.session = session;
        TrackLabel = slot == 0 ? "Primary" : "Secondary";
        language = session.DefaultLanguage;
        useCpu = !session.CanUseCuda || session.DefaultDevice == SrtAlignmentDevice.Cpu;
    }

    public string TrackLabel { get; }
    public string SubtitleName => Path.GetFileName(session.SourcePath);
    public string AudioLabel => session.AudioLabel;
    public bool CanUseCuda => session.CanUseCuda;
    public bool NeedsAcknowledgment => Draft?.NeedsTimingAcknowledgment == true;
    public string TimingAcknowledgmentText => Draft?.ClockCorrection != null
        ? "Accept inferred timings for this subtitle track"
        : "Keep original timings for unmatched cues";
    public bool HasPreview => Draft != null;
    public bool LacksLinkedTranslation => Draft is { CanSaveOriginal: true, CanSaveTranslated: false };
    public string CoverageText => Draft switch
    {
        { ClockCorrection: { } correction } result => $"Speech anchors: {correction.AnchorCount} of {result.TotalCount} cues",
        { } result => $"{result.MatchedCount} of {result.TotalCount} cues matched",
        _ => ""
    };
    public bool CanEditOptions => !Busy && !Draining && !closing && !stale && !cleanupRequired;
    public Func<string, string?, Task<string?>>? PickOutputPath { get; set; }

    public void RefreshSource()
    {
        if (stale || session.IsCurrent) return;
        stale = true;
        operation?.Cancel();
        ClearDraft();
        Message = "The media, audio stream, or subtitle source changed. Close this dialog and start alignment again.";
        RefreshCommands();
    }

    bool CanPrepare() => CanEditOptions && WhisperLanguage.LanguageToCode.ContainsValue(Language.Trim().ToLowerInvariant());
    bool CanSaveOriginal() => CanEditOptions && Draft is { CanSaveOriginal: true } && (!NeedsAcknowledgment || TimingAcknowledged);
    bool CanSaveTranslation() => CanEditOptions && Draft is { CanSaveTranslated: true } && (!NeedsAcknowledgment || TimingAcknowledged);
    bool CanCancel() => !Draining && (Busy || cleanupRequired);

    [RelayCommand(CanExecute = nameof(CanPrepare))]
    Task PrepareAsync()
    {
        if (!CanPrepare()) return Task.CompletedTask;
        string languageCode = Language.Trim().ToLowerInvariant();
        SrtAlignmentDevice device = UseCpu || !CanUseCuda ? SrtAlignmentDevice.Cpu : SrtAlignmentDevice.Cuda;
        ClearDraft();
        Message = device == SrtAlignmentDevice.Cuda ? "Analyzing selected audio on CUDA…" : "Analyzing selected audio on CPU…";
        return RunAsync(async token =>
        {
            SrtAlignmentDraft result = await session.PrepareAsync(languageCode, device, token);
            token.ThrowIfCancellationRequested();
            RefreshSource();
            if (stale) return;

            Rows = result.Rows.Select(row => new SrtAlignmentPreviewRow(row)).ToArray();
            Draft = result;
            Message = result switch
            {
                { ClockCorrection: { } correction } => "Whole-track correction uses speech anchors and assumes the same cut of the video. " +
                    "Review the proposed intervals before saving." +
                    (correction.ClippedBoundaryCount > 0 ? " Timing at a playback edge was trimmed." : ""),
                { HasTimingConflicts: true } => "Export is blocked because proposed and retained timings overlap or are out of order. Review the intervals, then reload a corrected SRT and try again.",
                { CanSaveOriginal: true } => "Review the proposed times on the media clock before saving a separate SRT.",
                _ => "The proposals could not be saved. Review the timing and match details below."
            };
        }, "Subtitle alignment");
    }

    [RelayCommand(CanExecute = nameof(CanSaveOriginal))]
    Task SaveOriginalAsync() => SaveAsync(translated: false);

    [RelayCommand(CanExecute = nameof(CanSaveTranslation))]
    Task SaveTranslationAsync() => SaveAsync(translated: true);

    Task SaveAsync(bool translated)
    {
        RefreshSource();
        if (!(translated ? CanSaveTranslation() : CanSaveOriginal()) || Draft is not { } result)
            return Task.CompletedTask;

        bool acknowledged = TimingAcknowledged;
        string name = Path.GetFileNameWithoutExtension(session.SourcePath) + (translated ? ".aligned.translated.srt" : ".aligned.srt");
        return RunAsync(async token =>
        {
            if (PickOutputPath == null)
                throw new IOException("A save dialog is not available.");

            string? path = await PickOutputPath(name, Path.GetDirectoryName(session.SourcePath));
            token.ThrowIfCancellationRequested();
            if (path == null) return;

            RefreshSource();
            if (stale) return;

            await session.SaveAsync(result, path, translated, acknowledged, token);
            Message = $"Saved {Path.GetFileName(path)}.";
        }, "Subtitle save");
    }

    Task RunAsync(Func<CancellationToken, Task> action, string label)
    {
        var cancellation = new CancellationTokenSource();
        operation = cancellation;
        Busy = true;
        return active = RunOperationAsync(action, label, cancellation);
    }

    async Task RunOperationAsync(Func<CancellationToken, Task> action, string label, CancellationTokenSource mine)
    {
        try
        {
            await action(mine.Token);
        }
        catch (OperationCanceledException error)
        {
            bool requested = mine.IsCancellationRequested;
            RefreshSource();
            if (!closing)
            {
                if (!requested) Message = error.Message;
                else if (!stale) Message = $"{label} cancelled.";
            }
        }
        catch (EngineProcessDrainException error)
        {
            cleanupRequired = true;
            Message = "The recognition process may still be running. Retry Cancel. " + error.Message;
        }
        catch (Exception error)
        {
            RefreshSource();
            if (!stale && !closing) Message = $"{label} failed. {error.Message}";
        }
        finally
        {
            operation = null;
            mine.Dispose();
            Busy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    async Task CancelAsync()
    {
        bool retry = cleanupRequired;
        try
        {
            await CancelAndDrainAsync();
            if (retry && !stale) Message = "Subtitle alignment stopped.";
        }
        catch (Exception error) { Message = "Could not stop subtitle alignment. Retry Cancel. " + error.Message; }
    }

    internal async Task<bool> TryCloseAsync()
    {
        closing = true;
        RefreshCommands();
        try
        {
            await CancelAndDrainAsync();
            return true;
        }
        catch (Exception error)
        {
            closing = false;
            Message = "Could not stop subtitle alignment. Retry Cancel. " + error.Message;
            RefreshCommands();
            return false;
        }
    }

    Task CancelAndDrainAsync()
    {
        if (activeDrain is { IsCompleted: false }) return activeDrain;
        return activeDrain = DrainAsync();
    }

    async Task DrainAsync()
    {
        operation?.Cancel();
        Draining = true;
        try
        {
            await Task.WhenAll(active ?? Task.CompletedTask, session.CancelAndDrainAsync());
            cleanupRequired = false;
        }
        catch
        {
            cleanupRequired = true;
            throw;
        }
        finally { Draining = false; }
    }

    void ClearDraft()
    {
        Draft = null;
        Rows = Array.Empty<SrtAlignmentPreviewRow>();
        TimingAcknowledged = false;
    }

    partial void OnLanguageChanged(string value) { ClearDraft(); RefreshCommands(); }
    partial void OnUseCpuChanged(bool value) => ClearDraft();
    partial void OnTimingAcknowledgedChanged(bool value) => RefreshCommands();
    partial void OnBusyChanged(bool value) => RefreshCommands();
    partial void OnDrainingChanged(bool value) => RefreshCommands();
    partial void OnDraftChanged(SrtAlignmentDraft? value)
    {
        OnPropertyChanged(nameof(NeedsAcknowledgment));
        OnPropertyChanged(nameof(TimingAcknowledgmentText));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(LacksLinkedTranslation));
        OnPropertyChanged(nameof(CoverageText));
        RefreshCommands();
    }

    void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanEditOptions));
        PrepareCommand.NotifyCanExecuteChanged();
        SaveOriginalCommand.NotifyCanExecuteChanged();
        SaveTranslationCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
