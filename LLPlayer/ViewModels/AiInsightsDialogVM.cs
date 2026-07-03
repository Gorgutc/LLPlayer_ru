using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Extensions;
using LLPlayer.Services;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace LLPlayer.ViewModels;

/// <summary>
/// AI Insights (F-07): summarize the current transcript and/or extract key vocabulary via the configured LLM.
/// Pure transcript/prompt/parse/orchestration logic lives in FlyleafLib (<see cref="AiInsightService"/> and
/// friends); this VM only handles slot/source selection, busy/cancel, result display and error mapping.
/// </summary>
public class AiInsightsDialogVM : Bindable, IDialogAware
{
    public FlyleafManager FL { get; }

    private readonly WordListStore _wordListStore;

    public AiInsightsDialogVM(FlyleafManager fl, WordListStore wordListStore)
    {
        FL = fl;
        _wordListStore = wordListStore;
    }

    public ObservableCollection<VocabularyEntry> Vocabulary { get; } = new();

    public IReadOnlyList<AiInsightMode> ModeList { get; } =
        [AiInsightMode.Summary, AiInsightMode.Vocabulary, AiInsightMode.Both];

    public AiInsightMode SelectedMode { get; set => Set(ref field, value); } = AiInsightMode.Both;

    public List<int> SubIndexList { get; } = [0, 1];

    public int SelectedSubIndex
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                RefreshHasText();
                OnPropertyChanged(nameof(SubManager));
                OnPropertyChanged(nameof(CanGenerate));
            }
        }
    }

    /// <summary>Analyze the translated text instead of the original (default = original source text).</summary>
    public bool UseTranslated { get; set => Set(ref field, value); }

    public bool IsBusy
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(CanGenerate));
            }
        }
    }

    public string ProgressText { get; set => Set(ref field, value); } = string.Empty;

    public string SummaryText
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(HasSummary));
            }
        }
    } = string.Empty;

    public string LlmIndicator { get; set => Set(ref field, value); } = string.Empty;

    public string RawVocabularyReply { get; set => Set(ref field, value); } = string.Empty;

    public bool ShowRawVocabulary { get; set => Set(ref field, value); }

    public bool PartialCoverageWarning { get; set => Set(ref field, value); }

    public bool HasSummary => !string.IsNullOrEmpty(SummaryText);

    public bool CanGenerate => !IsBusy && _hasText && _llmAvailable;

    private bool _hasText;
    private bool _llmAvailable;
    private CancellationTokenSource? _cts;

    public SubManager SubManager => FL.Player.SubtitlesManager[SelectedSubIndex];

    public AsyncDelegateCommand CmdGenerate => field ??= new AsyncDelegateCommand(async () =>
    {
        Config.SubtitlesConfig subs = FL.PlayerConfig.Subtitles;

        OpenAIBaseTranslateSettings? settings = AiInsightLlmResolver.Resolve(subs);
        if (settings == null)
        {
            ErrorDialogHelper.ShowKnownErrorPopup(
                "AI Insights needs an LLM (Ollama, LM Studio, OpenAI, Claude, ...). " +
                "Set an LLM service in Settings > Subtitles > Translate.", "AI Insights");
            return;
        }

        // HC-18: snapshot under _subsLocker before enumerating — Subs may be mutated by a background ASR/OCR
        // Add/Clear, which would throw InvalidOperationException on a raw enumeration here.
        List<string?> cues = SubManager.SnapshotSubs()
            .Where(s => s.IsText)
            .Select(s => UseTranslated ? s.DisplayText : s.Text)
            .ToList();

        if (cues.Count == 0)
        {
            ErrorDialogHelper.ShowKnownErrorPopup("There was no transcript to analyze.", "AI Insights");
            return;
        }

        string? sourceLang = SubManager.Language?.TopEnglishName;
        string targetLang = subs.TranslateTargetLanguage.DisplayName();

        // Reset previous output.
        SummaryText = string.Empty;
        Vocabulary.Clear();
        RawVocabularyReply = string.Empty;
        ShowRawVocabulary = false;
        PartialCoverageWarning = false;
        OnPropertyChanged(nameof(HasVocabulary));

        using CancellationTokenSource cts = new();
        _cts = cts;
        IsBusy = true;

        // Progress<T> captures this (UI) SynchronizationContext, so reports marshal back to the UI thread.
        Progress<AiInsightProgress> progress = new(p => ProgressText = $"{p.Phase} ({p.Current}/{p.Total})...");

        try
        {
            AiInsightService service = AiInsightService.ForSettings(settings);
            AiInsightResult result =
                await service.GenerateAsync(SelectedMode, cues, sourceLang, targetLang, progress, cts.Token);

            if (result.SummaryText != null)
            {
                SummaryText = result.SummaryText;
            }

            foreach (VocabularyEntry e in result.Vocabulary)
            {
                Vocabulary.Add(e);
            }

            // The model replied but the structured parse found nothing: show the raw reply rather than an
            // empty result, so the user still gets the model's output.
            if (SelectedMode is AiInsightMode.Vocabulary or AiInsightMode.Both
                && result.Vocabulary.Count == 0
                && !string.IsNullOrWhiteSpace(result.RawVocabularyReply))
            {
                RawVocabularyReply = result.RawVocabularyReply!;
                ShowRawVocabulary = true;
            }

            PartialCoverageWarning = result.PartialCoverage;
            OnPropertyChanged(nameof(HasVocabulary));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // User cancelled — leave whatever (none) was produced, no error popup.
        }
        catch (TranslationConfigException ex)
        {
            ErrorDialogHelper.ShowKnownErrorPopup($"No usable AI model is configured: {ex.Message}", "AI Insights");
        }
        catch (TranslationException ex) when (ex.Kind == TranslationFailureKind.Truncated)
        {
            ErrorDialogHelper.ShowKnownErrorPopup(
                "The model's reply was cut off (token limit). Try a shorter range, fewer items, " +
                "or raise Max Tokens in Settings > Subtitles > Translate.", "AI Insights");
        }
        catch (TranslationException ex)
        {
            ErrorDialogHelper.ShowKnownErrorPopup($"AI request failed: {ex.Message}", "AI Insights");
        }
        catch (Exception ex)
        {
            ErrorDialogHelper.ShowUnknownErrorPopup($"AI Insights failed: {ex.Message}", "AI Insights", ex);
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            _cts = null;
        }
    }).ObservesCanExecute(() => CanGenerate);

    public DelegateCommand CmdCancel => field ??= new(() => _cts?.Cancel());

    public DelegateCommand CmdCopy => field ??= new(() =>
    {
        string content = BuildExportText();
        if (content.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(content);
        }
        catch
        {
            // Clipboard can be transiently locked by another process; copying is best-effort.
        }
    });

    public DelegateCommand CmdSave => field ??= new(() =>
    {
        string content = BuildExportText();
        if (content.Length == 0)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Save AI insights",
            Filter = "Text file (*.txt)|*.txt|TSV (*.tsv)|*.tsv|All files (*.*)|*.*",
            FileName = "transcript-insights.txt",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                ErrorDialogHelper.ShowKnownErrorPopup($"Could not save the file: {ex.Message}", "AI Insights");
            }
        }
    });

    /// <summary>F-10: persist the extracted vocabulary into the global word list (dedup by Term).</summary>
    public DelegateCommand CmdAddToWordList => field ??= new DelegateCommand(() =>
    {
        if (Vocabulary.Count == 0)
        {
            return;
        }

        string sourceLang = SubManager.Language?.ISO6391 ?? string.Empty;
        string targetLang = FL.PlayerConfig.Subtitles.TranslateTargetLanguage.ToISO6391();

        int added = _wordListStore.MergeVocabulary(
            Vocabulary.ToList(), sourceLang, targetLang, DateTime.UtcNow.ToString("O"));
        _wordListStore.Save();

        int skipped = Vocabulary.Count - added;
        string msg = added > 0
            ? $"Added {added} word(s) to the word list" + (skipped > 0 ? $" ({skipped} already there)" : "")
            : "All words are already in the word list";
        FL.MessageQueue.Enqueue(msg);
    }, () => HasVocabulary).ObservesProperty(() => HasVocabulary);

    public bool HasVocabulary => Vocabulary.Count > 0;

    // Combined plain-text export: the summary, then the vocabulary as TSV (Anki-importable), then the raw
    // vocabulary reply if that is all the parser could surface.
    private string BuildExportText()
    {
        StringBuilder sb = new();

        if (!string.IsNullOrEmpty(SummaryText))
        {
            sb.Append(SummaryText);
        }

        if (Vocabulary.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.Append("\r\n\r\n");
            }
            sb.Append(VocabularyParser.ToTsv(Vocabulary.ToList()));
        }
        else if (ShowRawVocabulary && RawVocabularyReply.Length > 0)
        {
            if (sb.Length > 0)
            {
                sb.Append("\r\n\r\n");
            }
            sb.Append(RawVocabularyReply);
        }

        return sb.ToString();
    }

    private void RefreshHasText()
    {
        // HC-18: snapshot under _subsLocker (Subs may be mutated by a background ASR/OCR run while the dialog is open).
        _hasText = FL.Player.SubtitlesManager[SelectedSubIndex].SnapshotSubs().Any(s => s.IsText);
    }

    private void RefreshLlm()
    {
        OpenAIBaseTranslateSettings? settings = AiInsightLlmResolver.Resolve(FL.PlayerConfig.Subtitles);
        _llmAvailable = settings != null;
        LlmIndicator = settings == null
            ? "No LLM configured"
            : settings.ServiceType.ToString() + (string.IsNullOrWhiteSpace(settings.Model) ? "" : $" - {settings.Model}");
    }

    #region IDialogAware
    public string Title { get; set => Set(ref field, value); }
        = $"AI Insights - {App.Name}";
    public double WindowWidth { get; set => Set(ref field, value); } = 620;
    public double WindowHeight { get; set => Set(ref field, value); } = 620;

    public DialogCloseListener RequestClose { get; }

    public bool CanCloseDialog()
    {
        return true;
    }

    public void OnDialogClosed()
    {
        // Cancel any in-flight generation so closing the dialog does not leak the request.
        _cts?.Cancel();
    }

    public void OnDialogOpened(IDialogParameters parameters)
    {
        RefreshHasText();
        RefreshLlm();
        OnPropertyChanged(nameof(CanGenerate));
    }
    #endregion
}
