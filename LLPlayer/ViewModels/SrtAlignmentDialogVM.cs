using System.Globalization;
using System.IO;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;
using LLPlayer.Extensions;
using LLPlayer.Services;
using Microsoft.Win32;

namespace LLPlayer.ViewModels;

public sealed class SrtAlignmentDialogVM : Bindable, IDialogAware
{
    public const string SessionKey = "alignmentSession";
    private const string SourceChangedMessage = "The media, audio stream, or subtitle source changed. Close this dialog and start alignment again.";

    private ISrtAlignmentSession? _session;
    private SrtAlignmentDraft? _draft;
    private Operation? _operation;
    private bool _stopping;
    private bool _needsCleanup;
    private bool _closeWhenStopped;
    private bool _sourceStale;

    public FlyleafManager? FL { get; }

    public SrtAlignmentDialogVM(FlyleafManager? fl) => FL = fl;

    public string Title => $"Align SRT to Audio - {App.Name}";
    public double WindowWidth { get; set; } = 780;
    public double WindowHeight { get; set; } = 570;
    public string SourcePath => _session?.SourcePath ?? string.Empty;
    public string AudioLabel => _session?.AudioLabel ?? string.Empty;
    public string StatusText { get; private set => Set(ref field, value); } = string.Empty;

    public string Language
    {
        get => field;
        set
        {
            if (Set(ref field, value))
            {
                if (_draft != null)
                    StatusText = "Prepare a new preview for the selected language.";
                _draft = null;
                ReviewRows = [];
                KeepUnmatched = false;
                UpdateState();
            }
        }
    } = "en";

    public bool KeepUnmatched
    {
        get => field;
        set
        {
            if (Set(ref field, value))
                UpdateState();
        }
    }

    public IReadOnlyList<ReviewRow> ReviewRows { get; private set => Set(ref field, value); } = [];
    public bool HasUnmatched => _draft is { } draft && draft.MatchedCount < draft.TotalCount;
    public bool IsWorking => _operation != null || _stopping;
    public bool CanEditLanguage => !IsWorking && !_needsCleanup && !_sourceStale && _session?.IsCurrent == true;
    public bool CanPrepare => CanEditLanguage && IsLanguageCode(Language);
    public bool CanStop => !_stopping && (_operation != null || _needsCleanup);
    public string StopLabel => _needsCleanup ? "Retry stopping" : "Cancel operation";
    public bool CanSaveOriginal => CanSave && _draft?.CanSaveOriginal == true;
    public bool CanSaveTranslation => CanSave && _draft?.CanSaveTranslated == true;

    private bool CanSave => !IsWorking && !_needsCleanup && !_sourceStale && _session?.IsCurrent == true &&
        (!HasUnmatched || KeepUnmatched);

    public AsyncDelegateCommand CmdPrepare => field ??= new AsyncDelegateCommand(PrepareAsync)
        .ObservesCanExecute(() => CanPrepare);

    public AsyncDelegateCommand CmdStop => field ??= new AsyncDelegateCommand(StopAsync)
        .ObservesCanExecute(() => CanStop);

    public AsyncDelegateCommand CmdSaveOriginal => field ??= new AsyncDelegateCommand(() => SaveAsync(false))
        .ObservesCanExecute(() => CanSaveOriginal);

    public AsyncDelegateCommand CmdSaveTranslation => field ??= new AsyncDelegateCommand(() => SaveAsync(true))
        .ObservesCanExecute(() => CanSaveTranslation);

    public DelegateCommand CmdClose => field ??= new(() => RequestClose.Invoke(ButtonResult.Cancel));

    public async Task PrepareAsync()
    {
        RefreshSource();
        if (!CanPrepare || _session is not { } session)
            return;

        string language = Language.Trim().ToLowerInvariant();
        _draft = null;
        ReviewRows = [];
        KeepUnmatched = false;
        await RunOperationAsync("Matching subtitle text to speech…", async token =>
        {
            SrtAlignmentDraft draft = await session.PrepareAsync(language, SrtAlignmentDevice.Cpu, token);
            token.ThrowIfCancellationRequested();
            RefreshSource();
            if (_sourceStale)
                return;

            _draft = draft;
            ReviewRows = draft.Rows.Select(row => new ReviewRow(row)).ToArray();
            StatusText = draft.CanSaveOriginal
                ? $"Matched {draft.MatchedCount} of {draft.TotalCount} cues."
                : $"Matched {draft.MatchedCount} of {draft.TotalCount} cues. No valid SRT can be saved.";
        });
    }

    private async Task SaveAsync(bool translated)
    {
        RefreshSource();
        if ((translated ? !CanSaveTranslation : !CanSaveOriginal) || _session is not { } session || _draft is not { } draft)
            return;

        string fileName = Path.GetFileNameWithoutExtension(session.SourcePath);
        SaveFileDialog picker = new()
        {
            Title = translated ? "Save aligned translation" : "Save aligned SRT copy",
            Filter = "SubRip subtitle (*.srt)|*.srt",
            DefaultExt = ".srt",
            InitialDirectory = Path.GetDirectoryName(session.SourcePath),
            FileName = fileName + (translated ? ".aligned.translated.srt" : ".aligned.srt"),
            OverwritePrompt = false
        };
        if (picker.ShowDialog() != true)
            return;

        RefreshSource();
        if (_sourceStale)
            return;

        bool keepUnmatched = KeepUnmatched;
        await RunOperationAsync("Saving aligned SRT…", async token =>
        {
            await session.SaveAsync(draft, picker.FileName, translated, keepUnmatched, token);
            RefreshSource();
            if (!_sourceStale)
                StatusText = $"Saved {Path.GetFileName(picker.FileName)}.";
        });
    }

    private async Task RunOperationAsync(string status, Func<CancellationToken, Task> execute)
    {
        using CancellationTokenSource cancellation = new();
        Operation operation = new(cancellation);
        _operation = operation;
        StatusText = status;
        UpdateState();
        try
        {
            await execute(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            RefreshSource();
            if (!_sourceStale)
                StatusText = "Operation canceled.";
        }
        catch (EngineProcessDrainException ex)
        {
            _needsCleanup = true;
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            RefreshSource();
            if (!_sourceStale)
                StatusText = ex.Message;
        }
        finally
        {
            _operation = null;
            operation.Finished.TrySetResult();
            UpdateState();
        }
    }

    public async Task StopAsync()
    {
        if (_stopping || _session is not { } session)
            return;

        _stopping = true;
        StatusText = "Stopping alignment…";
        UpdateState();
        try
        {
            Operation? operation = _operation;
            operation?.Cancellation.Cancel();
            await session.CancelAndDrainAsync();
            if (operation != null)
                await operation.Finished.Task;
            _needsCleanup = false;
            StatusText = _sourceStale ? SourceChangedMessage : "Operation stopped.";
        }
        catch (Exception ex)
        {
            _needsCleanup = true;
            StatusText = ex.Message;
        }
        finally
        {
            _stopping = false;
            UpdateState();
        }

        if (_closeWhenStopped && !_needsCleanup)
        {
            // Prism may have initiated this stop within a synchronous Closing callback.
            await Task.Yield();
            RequestClose.Invoke(ButtonResult.Cancel);
        }
    }

    private void UpdateState()
    {
        OnPropertyChanged(nameof(HasUnmatched));
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(CanEditLanguage));
        OnPropertyChanged(nameof(CanPrepare));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(StopLabel));
        OnPropertyChanged(nameof(CanSaveOriginal));
        OnPropertyChanged(nameof(CanSaveTranslation));
    }

    private static bool IsLanguageCode(string value)
        => WhisperLanguage.LanguageToCode.ContainsValue(value.Trim().ToLowerInvariant());

    public void RefreshSource()
    {
        if (_sourceStale || _session is null || _session.IsCurrent)
            return;

        _sourceStale = true;
        _draft = null;
        ReviewRows = [];
        KeepUnmatched = false;
        StatusText = SourceChangedMessage;
        _operation?.Cancellation.Cancel();
        UpdateState();
    }

    public DialogCloseListener RequestClose { get; }

    public bool CanCloseDialog()
    {
        if (!IsWorking && !_needsCleanup)
            return true;

        _closeWhenStopped = true;
        if (!_stopping)
            _ = StopAsync();
        return false;
    }

    public void OnDialogClosed()
    {
        if (FL != null)
            FL.SrtAlignmentSourceChanged -= RefreshSource;
        _session = null;
    }

    public void OnDialogOpened(IDialogParameters parameters)
    {
        _session = parameters.GetValue<ISrtAlignmentSession>(SessionKey);
        if (FL != null)
            FL.SrtAlignmentSourceChanged += RefreshSource;
        Language = _session.DefaultLanguage;
        StatusText = "Choose the spoken language and prepare an alignment preview.";
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(AudioLabel));
        RefreshSource();
        UpdateState();
    }

    private sealed record Operation(CancellationTokenSource Cancellation)
    {
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed record ReviewRow(SrtAlignmentRow Row)
    {
        public string Text => Row.Text;
        public string OriginalInterval => $"{FormatTime(Row.OriginalStart)} – {FormatTime(Row.OriginalEnd)}";
        public string ProposedInterval => Row.ProposedStart is { } start && Row.ProposedEnd is { } end
            ? $"{FormatTime(start)} – {FormatTime(end)}"
            : "Keep original";
        public string Match => Row.Reason ?? $"Matched {Row.Coverage:P0}";

        private static string FormatTime(TimeSpan time) =>
            string.Create(CultureInfo.InvariantCulture,
                $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }
}
