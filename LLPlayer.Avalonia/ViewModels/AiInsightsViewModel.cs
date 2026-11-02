using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>A resolved existing LLM; resolving this binding never submits a request.</summary>
public sealed record AiInsightConnection(string Indicator, AiInsightService Service);

/// <summary>One shared desktop workflow over Flyleaf's existing bounded insight service.</summary>
public sealed partial class AiInsightsViewModel : ObservableObject, IAsyncDisposable
{
    const string SourceUnavailableMessage = "The subtitle source is changing or unavailable. Try again when it is ready.";
    readonly Func<int, bool, InsightsTranscriptSnapshot> capture;
    readonly Func<AiInsightConnection?> resolve;
    readonly Func<InsightsTranscriptSnapshot, bool> isCurrent;
    readonly WordLibrary library;
    CancellationTokenSource? operation;
    Task? active;
    bool stopping, hasText;
    long requestVersion;
    InsightsTranscriptSnapshot? input, resultSource;
    public AiInsightsViewModel(Func<int, bool, InsightsTranscriptSnapshot> capture,
        Func<AiInsightConnection?> resolve, WordLibrary library,
        Func<InsightsTranscriptSnapshot, bool>? isCurrent = null)
    {
        this.capture = capture; this.resolve = resolve; this.library = library;
        this.isCurrent = isCurrent ?? (snapshot => capture(snapshot.Slot, snapshot.UseTranslated).MediaGeneration == snapshot.MediaGeneration);
        Refresh();
    }
    public Func<string, Task>? CopyTextAsync { get; set; }
    /// <summary>Explicit picker/write adapter; returns the actual saved path or null for cancellation.</summary>
    public Func<string, Task<string?>>? SaveTextAsync { get; set; }
    public IReadOnlyList<AiInsightMode> ModeList { get; } = [AiInsightMode.Summary, AiInsightMode.Vocabulary, AiInsightMode.Both];
    public IReadOnlyList<string> SlotNames { get; } = ["Primary", "Secondary"];
    public ObservableCollection<VocabularyEntry> Vocabulary { get; } = [];
    [ObservableProperty] AiInsightMode selectedMode = AiInsightMode.Both;
    [ObservableProperty] int selectedSubIndex;
    [ObservableProperty] bool useTranslated;
    [ObservableProperty] bool busy;
    [ObservableProperty] string progressText = "";
    [ObservableProperty] string summaryText = "";
    [ObservableProperty] string rawVocabularyReply = "";
    [ObservableProperty] bool showRawVocabulary;
    [ObservableProperty] bool partialCoverageWarning;
    [ObservableProperty] string llmIndicator = "No LLM configured";
    [ObservableProperty] string message = "";
    [ObservableProperty] string inputSourceText = "";
    [ObservableProperty] string resultSourceText = "";
    [ObservableProperty] string budgetText = "";
    [ObservableProperty] bool incompleteTranscriptWarning;
    public bool HasSummary => SummaryText.Length > 0;
    public bool HasVocabulary => Vocabulary.Count > 0;
    public bool HasResults => HasSummary || HasVocabulary || ShowRawVocabulary;
    public bool CanGenerate => !stopping && !Busy && hasText && LlmIndicator != "No LLM configured";
    public bool CanUseResults => !stopping && !Busy && HasResults;
    public bool CanAddVocabulary => !stopping && !Busy && HasVocabulary;
    public bool CanEditInputs => !Busy && !stopping;
    public bool IsClosing => stopping;
    partial void OnSelectedSubIndexChanged(int value) => Refresh();
    partial void OnUseTranslatedChanged(bool value) => Refresh();
    partial void OnSummaryTextChanged(string value) { OnPropertyChanged(nameof(HasSummary)); NotifyActions(); }
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanEditInputs)); NotifyActions(); }
    void NotifyActions()
    {
        OnPropertyChanged(nameof(CanGenerate)); OnPropertyChanged(nameof(HasVocabulary)); OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(CanUseResults)); OnPropertyChanged(nameof(CanAddVocabulary));
        GenerateCommand.NotifyCanExecuteChanged(); CopyCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged(); AddToWordListCommand.NotifyCanExecuteChanged();
    }
    /// <summary>Called by the app's UI-thread player update stream, including ASR/OCR cue arrival.</summary>
    public void Refresh()
    {
        if (stopping) return;
        var fresh = CaptureSafely(false);
        if (fresh == null) return;
        if (input != null && input.MediaGeneration != fresh.MediaGeneration) Invalidate();
        input = fresh;
        string transcript = AiTranscript.Build(fresh.CueTexts);
        var chunks = AiTranscript.Chunk(transcript, AiInsightBudget.MaxInputCharsPerChunk, out bool partial);
        hasText = transcript.Length > 0;
        InputSourceText = Describe(fresh);
        IncompleteTranscriptWarning = fresh.IsGenerating;
        BudgetText = $"{transcript.Length:N0} characters · {chunks.Count} chunk(s) · {AiInsightBudget.MaxInputCharsPerChunk:N0} characters per chunk, up to {AiInsightBudget.MaxChunks} chunks"
            + (partial ? " · Partial transcript coverage" : "");
        LlmIndicator = resolve()?.Indicator ?? "No LLM configured";
        if (Message == SourceUnavailableMessage) Message = "";
        NotifyActions();
    }
    InsightsTranscriptSnapshot? CaptureSafely(bool detach)
    {
        try
        {
            var snapshot = capture(Math.Clamp(SelectedSubIndex, 0, 1), UseTranslated);
            return detach ? snapshot with { CueTexts = snapshot.CueTexts.ToArray() } : snapshot;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            // The native owner may retire the media/track between reading its identity and its cues.
            // Fail closed before a provider request, and allow the next refresh/action to retry.
            Invalidate(); input = null; hasText = false;
            InputSourceText = "Transcript unavailable"; BudgetText = ""; IncompleteTranscriptWarning = false;
            Message = SourceUnavailableMessage; NotifyActions();
            return null;
        }
    }
    static string Describe(InsightsTranscriptSnapshot source) =>
        $"{source.MediaCaption} · {(source.Slot == 0 ? "Primary" : "Secondary")} · {(source.UseTranslated ? "translated" : "original")} · {source.CapturedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
    void ClearResults()
    {
        SummaryText = ""; Vocabulary.Clear(); RawVocabularyReply = ""; ShowRawVocabulary = false;
        PartialCoverageWarning = false; resultSource = null; ResultSourceText = ""; NotifyActions();
    }
    /// <summary>Retire requests/results when their media owner changes; late providers cannot republish them.</summary>
    public void Invalidate()
    {
        requestVersion++; operation?.Cancel(); ClearResults();
    }
    [RelayCommand(CanExecute = nameof(CanGenerate))] Task GenerateAsync() => StartGenerationAsync();
    public Task StartGenerationAsync()
    {
        if (Busy || stopping) return Task.CompletedTask;
        var connection = resolve();
        if (connection == null) { Message = "AI Insights needs an LLM. Configure an LLM service in Settings > Translation."; return Task.CompletedTask; }
        var snapshot = CaptureSafely(true);
        if (snapshot == null) return Task.CompletedTask;
        if (AiTranscript.Build(snapshot.CueTexts).Length == 0) { Message = "There was no transcript to analyze."; return Task.CompletedTask; }
        ClearResults(); Message = ""; Busy = true; operation = new();
        return active = RunAsync(connection.Service, snapshot, SelectedMode, ++requestVersion, operation);
    }
    async Task RunAsync(AiInsightService service, InsightsTranscriptSnapshot snapshot, AiInsightMode mode, long version, CancellationTokenSource mine)
    {
        bool Current() => !stopping && ReferenceEquals(operation, mine) && version == requestVersion && !mine.IsCancellationRequested &&
            IsCurrentSafely(snapshot);
        var progress = new Progress<AiInsightProgress>(p =>
        {
            if (Current()) ProgressText = $"{p.Phase} ({p.Current}/{p.Total})...";
        });
        try
        {
            var result = await service.GenerateAsync(mode, snapshot.CueTexts,
                snapshot.UseTranslated ? snapshot.TargetLanguageName : snapshot.SourceLanguageName,
                snapshot.TargetLanguageName, progress, mine.Token);
            if (!Current()) return;
            resultSource = snapshot;
            ResultSourceText = Describe(snapshot);
            SummaryText = result.SummaryText ?? "";
            foreach (var word in result.Vocabulary) Vocabulary.Add(word);
            RawVocabularyReply = result.RawVocabularyReply ?? "";
            ShowRawVocabulary = mode is AiInsightMode.Vocabulary or AiInsightMode.Both && !HasVocabulary && RawVocabularyReply.Length > 0;
            PartialCoverageWarning = result.PartialCoverage;
            Message = "Generation complete.";
        }
        catch (OperationCanceledException) when (mine.IsCancellationRequested) { if (!stopping && version == requestVersion) Message = "Generation cancelled."; }
        catch (TranslationConfigException e) { if (Current()) Message = "No usable AI model is configured: " + e.Message; }
        catch (TranslationException e) when (e.Kind == TranslationFailureKind.Truncated)
        { if (Current()) Message = "The model's reply was cut off (token limit). Try a shorter transcript or raise Max Tokens in Translation settings."; }
        catch (TranslationException e) { if (Current()) Message = "AI request failed: " + e.Message; }
        catch (Exception e) { if (Current()) Message = "AI Insights failed: " + e.Message; }
        finally
        {
            if (mine.IsCancellationRequested && !stopping && version == requestVersion) Message = "Generation cancelled.";
            operation = null; Busy = false; ProgressText = ""; mine.Dispose(); NotifyActions();
        }
    }
    bool IsCurrentSafely(InsightsTranscriptSnapshot snapshot)
    {
        try { return isCurrent(snapshot); }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            // Media retirement can make a host snapshot unavailable. A failed identity probe retires the
            // callback too; never turn a progress/error callback into a second desktop exception.
            return false;
        }
    }
    [RelayCommand] void Cancel() { operation?.Cancel(); Message = "Cancelling generation..."; }
    public string BuildExportText()
    {
        var text = new StringBuilder(SummaryText);
        string vocabulary = HasVocabulary ? VocabularyParser.ToTsv(Vocabulary.ToArray()) : ShowRawVocabulary ? RawVocabularyReply : "";
        if (vocabulary.Length > 0) { if (text.Length > 0) text.Append("\r\n\r\n"); text.Append(vocabulary); }
        return text.ToString();
    }
    [RelayCommand(CanExecute = nameof(CanUseResults))] Task CopyAsync() => StartExport(false);
    [RelayCommand(CanExecute = nameof(CanUseResults))] Task SaveAsync() => StartExport(true);
    Task StartExport(bool save)
    {
        if (!CanUseResults) return Task.CompletedTask;
        string content = BuildExportText();
        Busy = true; operation = new();
        return active = ExportAsync(save, content, operation);
    }
    async Task ExportAsync(bool save, string content, CancellationTokenSource mine)
    {
        try
        {
            if (save)
            {
                if (SaveTextAsync == null) throw new IOException("Save picker is unavailable.");
                string? path = await SaveTextAsync(content);
                if (!stopping && !mine.IsCancellationRequested) Message = path == null ? "Save cancelled." : "Saved AI insights: " + path;
            }
            else
            {
                if (CopyTextAsync == null) throw new IOException("Clipboard is unavailable.");
                await CopyTextAsync(content);
                if (!stopping && !mine.IsCancellationRequested) Message = "Copied AI insights.";
            }
        }
        catch (Exception e) { if (!stopping && !mine.IsCancellationRequested) Message = (save ? "Could not save AI insights: " : "Copy failed: ") + e.Message; }
        finally { operation = null; Busy = false; mine.Dispose(); }
    }
    [RelayCommand(CanExecute = nameof(CanAddVocabulary))] void AddToWordList()
    {
        if (!CanAddVocabulary || resultSource is not { } source) return;
        try
        {
            int added = library.MergeVocabulary(Vocabulary.ToArray(), source.UseTranslated ? source.TargetLanguageCode : source.SourceLanguageCode,
                source.TargetLanguageCode, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Message = added == 0 ? "All words are already in the word list." : $"Added {added} word(s) to the word list ({Vocabulary.Count - added} already there).";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Message = "Vocabulary was not saved: " + e.Message; }
    }
    public async ValueTask DisposeAsync()
    {
        if (stopping) { if (active != null) await active; return; }
        stopping = true; requestVersion++; operation?.Cancel(); NotifyActions();
        if (active != null) await active;
    }
}
