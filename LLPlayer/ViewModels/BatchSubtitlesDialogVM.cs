using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;
using LLPlayer.Extensions;
using LLPlayer.Services;
using Microsoft.Win32;

namespace LLPlayer.ViewModels;

public class BatchSubtitlesDialogVM : Bindable, IDialogAware
{
    private CancellationTokenSource? _cts;
    private bool _initializing = true;

    public FlyleafManager FL { get; }

    public BatchSubtitlesDialogVM(FlyleafManager fl)
    {
        FL = fl;

        FolderPath = FL.Config.BatchSubtitles.LastFolder;
        Recursive = FL.Config.BatchSubtitles.Recursive;
        OverwriteExisting = FL.Config.BatchSubtitles.OverwriteExisting;
        _initializing = false;

        Jobs.CollectionChanged += JobsOnCollectionChanged;
    }

    public ObservableCollection<BatchSubtitleJob> Jobs { get; } = new();

    public BatchSubtitleJob? SelectedJob
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(CanOpenOutputFolder));
            }
        }
    }

    // The job whose ASR is currently streaming — the live transcript pane binds to its Transcript.
    public BatchSubtitleJob? ActiveJob { get; private set => Set(ref field, value); }

    public string FolderPath
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                FL.Config.BatchSubtitles.LastFolder = value;
                OnPropertyChanged(nameof(CanScan));
                OnPropertyChanged(nameof(CanOpenOutputFolder));
                PersistBatchDefaults();
            }
        }
    } = string.Empty;

    public bool Recursive
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                FL.Config.BatchSubtitles.Recursive = value;
                PersistBatchDefaults();
            }
        }
    }

    public bool OverwriteExisting
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                FL.Config.BatchSubtitles.OverwriteExisting = value;
                PersistBatchDefaults();
            }
        }
    }

    public bool IsRunning
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(CanScan));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanRetryFailed));
            }
        }
    }

    public bool IsScanning
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(CanScan));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanRetryFailed));
            }
        }
    }

    public string SummaryText { get; set => Set(ref field, value); } = string.Empty;

    public bool CanScan => !IsRunning && !IsScanning && Directory.Exists(FolderPath);
    public bool CanStart => IsIdle && Jobs.Any(j => j.Include);
    public bool CanCancel => IsRunning;
    public bool IsIdle => !IsRunning && !IsScanning;
    public bool CanRetryFailed => IsIdle && Jobs.Any(j => j.Status == BatchSubtitleStatus.Failed);
    public bool CanOpenOutputFolder => Directory.Exists(GetOutputFolder());

    // Tri-state "select all / none" bound to the DataGrid header checkbox.
    // null = mixed selection (display-only; user clicks only toggle true/false since IsThreeState=False).
    public bool? AllIncluded
    {
        get
        {
            if (Jobs.Count == 0)
                return false;
            if (Jobs.All(j => j.Include))
                return true;
            return Jobs.All(j => !j.Include) ? false : null;
        }
        set
        {
            bool target = value == true;
            foreach (BatchSubtitleJob job in Jobs)
                job.Include = target;
            // Per-job PropertyChanged (OnJobPropertyChanged) refreshes AllIncluded/CanStart/summary.
        }
    }

    public DelegateCommand CmdBrowseFolder => field ??= new(() =>
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Select video folder",
            InitialDirectory = Directory.Exists(FolderPath) ? FolderPath : string.Empty
        };

        if (dialog.ShowDialog() == true)
        {
            FolderPath = dialog.FolderName;
            PersistBatchDefaults();
        }
    });

    public AsyncDelegateCommand CmdScan => field ??= new AsyncDelegateCommand(async () =>
    {
        if (!Directory.Exists(FolderPath))
        {
            ErrorDialogHelper.ShowKnownErrorPopup("The selected folder does not exist.", "Batch subtitles");
            return;
        }

        PersistBatchDefaults();

        IsScanning = true;
        SummaryText = "Scanning...";

        try
        {
            bool overwrite = OverwriteExisting;
            List<(string Path, bool HasTranslation)> scanned = await Task.Run(() =>
                BatchVideoScanner.Scan(FolderPath, Recursive)
                    .Select(path => (path, HasTranslation: SubtitleOutputPathBuilder.TranslationExists(path)))
                    .ToList());

            UnsubscribeJobs();
            Jobs.Clear();
            foreach ((string mediaPath, bool hasTranslation) in scanned)
            {
                BatchSubtitleJob job = new(mediaPath);
                if (hasTranslation)
                {
                    // Already translated — show it as done. Unless overwriting, drop it from the
                    // default run so Start only processes the not-yet-translated files.
                    job.Status = BatchSubtitleStatus.Completed;
                    job.CompletedAt = DateTimeOffset.Now;
                    if (!overwrite)
                        job.Include = false;
                }

                job.PropertyChanged += OnJobPropertyChanged;
                Jobs.Add(job);
            }

            OnPropertyChanged(nameof(AllIncluded));
            UpdateSummary();
        }
        catch (Exception ex)
        {
            ErrorDialogHelper.ShowUnknownErrorPopup($"Cannot scan folder: {ex.Message}", "Batch subtitles", ex);
            UpdateSummary();
        }
        finally
        {
            IsScanning = false;
        }
    }).ObservesCanExecute(() => CanScan);

    public AsyncDelegateCommand CmdStart => field ??= new AsyncDelegateCommand(async () =>
    {
        await RunAsync(Jobs.Where(job => job.Include).ToList());
    }).ObservesCanExecute(() => CanStart);

    public AsyncDelegateCommand CmdRetryFailed => field ??= new AsyncDelegateCommand(async () =>
    {
        await RunAsync(Jobs.Where(job => job.Status == BatchSubtitleStatus.Failed).ToList(), forceOverwrite: true);
    }).ObservesCanExecute(() => CanRetryFailed);

    public DelegateCommand<BatchSubtitleJob> CmdRetryJob => field ??= new DelegateCommand<BatchSubtitleJob>(job =>
    {
        if (job is null || !IsIdle)
            return;

        // Explicit retry: force re-processing even if a stale/partial output already exists.
        _ = RunAsync([job], forceOverwrite: true);
    }).ObservesCanExecute(() => IsIdle);

    // Shared run path for Start, per-row retry, and "Retry failed". Each call builds its own CTS,
    // processor, and capacity-1 channel, so re-running a subset is safe. Only the jobs in 'toRun'
    // are reset, preserving scan-time Completed/Include marks on the rows left alone.
    private async Task RunAsync(IReadOnlyList<BatchSubtitleJob> toRun, bool forceOverwrite = false)
    {
        if (IsRunning)
            return;

        PersistBatchDefaults();

        if (toRun.Count == 0)
        {
            UpdateSummary();
            return;
        }

        List<BatchSubtitleJob> workerJobs = toRun.Select(job => new BatchSubtitleJob(job.MediaPath)).ToList();
        Dictionary<string, BatchSubtitleJob> uiJobs = toRun
            .GroupBy(job => job.MediaPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (BatchSubtitleJob job in toRun)
        {
            job.Status = BatchSubtitleStatus.Pending;
            job.Error = null;
            job.SubtitleCount = 0;
            job.StartedAt = null;
            job.CompletedAt = null;
            job.ProcessedTime = null;
            job.TotalDuration = null;
            job.Progress = 0;
            job.IsIndeterminateProgress = false;
            job.Throughput = string.Empty;
            job.Transcript.Clear();
        }

        ActiveJob = null;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        UpdateSummary();

        try
        {
            Progress<BatchSubtitleProgress> progress = new(update =>
            {
                if (!uiJobs.TryGetValue(update.Job.MediaPath, out BatchSubtitleJob? uiJob))
                    return;

                // Streaming per-segment ASR update (carries recognized text): live feedback only.
                if (update.AsrSegmentText != null)
                {
                    ApplyAsrSegment(uiJob, update);
                    UpdateSummary();
                    return;
                }

                // New file started transcribing: reset its live feedback and focus the transcript pane.
                if (update.Status == BatchSubtitleStatus.RunningASR)
                {
                    uiJob.Transcript.Clear();
                    uiJob.Progress = 0;
                    uiJob.ProcessedTime = null;
                    uiJob.Throughput = string.Empty;
                    uiJob.IsIndeterminateProgress = true;
                    ActiveJob = uiJob;
                }

                uiJob.Status = update.Status;
                uiJob.Error = update.Error;

                if (update.SubtitleCount.HasValue)
                    uiJob.SubtitleCount = update.SubtitleCount.Value;
                if (update.StartedAt.HasValue)
                    uiJob.StartedAt = update.StartedAt;
                if (update.CompletedAt.HasValue)
                    uiJob.CompletedAt = update.CompletedAt;

                UpdateSummary();
            });

            BatchSubtitleProcessor processor = new(
                new BatchAsrTranscriber(FL.PlayerConfig),
                new BatchSubtitleTranslator(FL.PlayerConfig.Subtitles),
                new SrtSubtitleWriter(new UTF8Encoding(FL.Config.Subs.SubsExportUTF8WithBom)),
                new BatchSubtitleOptions
                {
                    Recursive = Recursive,
                    OverwriteExisting = OverwriteExisting || forceOverwrite,
                    Utf8Bom = FL.Config.Subs.SubsExportUTF8WithBom
                },
                progress);

            await processor.ProcessAsync(workerJobs, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            MarkPendingCanceled(toRun);
            UpdateSummary();
        }
        catch (Exception ex)
        {
            ErrorDialogHelper.ShowUnknownErrorPopup($"Batch subtitles failed: {ex.Message}", "Batch subtitles", ex);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
            UpdateSummary();
        }
    }

    public DelegateCommand CmdCancel => field ??= new DelegateCommand(() =>
    {
        _cts?.Cancel();
    }).ObservesCanExecute(() => CanCancel);

    public DelegateCommand CmdOpenOutputFolder => field ??= new DelegateCommand(() =>
    {
        string? folder = GetOutputFolder();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }).ObservesCanExecute(() => CanOpenOutputFolder);

    public DelegateCommand CmdCloseDialog => field ??= new(() =>
    {
        RequestClose.Invoke(ButtonResult.Cancel);
    });

    private string? GetOutputFolder()
    {
        if (SelectedJob != null)
            return Path.GetDirectoryName(SelectedJob.OutputPath);

        return FolderPath;
    }

    private void PersistBatchDefaults()
    {
        if (_initializing)
            return;

        try
        {
            AppConfig persisted = File.Exists(App.AppConfigPath)
                ? AppConfig.Load(App.AppConfigPath)
                : new AppConfig();

            persisted.BatchSubtitles.LastFolder = FL.Config.BatchSubtitles.LastFolder;
            persisted.BatchSubtitles.Recursive = FL.Config.BatchSubtitles.Recursive;
            persisted.BatchSubtitles.OverwriteExisting = FL.Config.BatchSubtitles.OverwriteExisting;
            persisted.Save(App.AppConfigPath);
        }
        catch (Exception ex)
        {
            ErrorDialogHelper.ShowUnknownErrorPopup(
                $"Cannot save batch subtitle defaults: {ex.Message}",
                "Batch subtitles",
                ex);
        }
    }

    private void JobsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanOpenOutputFolder));
    }

    private static void MarkPendingCanceled(IEnumerable<BatchSubtitleJob> scope)
    {
        foreach (BatchSubtitleJob job in scope.Where(job => job.Status == BatchSubtitleStatus.Pending))
        {
            job.Status = BatchSubtitleStatus.Canceled;
            job.CompletedAt = DateTimeOffset.Now;
        }
    }

    private void OnJobPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BatchSubtitleJob.Include))
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(AllIncluded));
            UpdateSummary();
        }
    }

    private void UnsubscribeJobs()
    {
        foreach (BatchSubtitleJob job in Jobs)
        {
            job.PropertyChanged -= OnJobPropertyChanged;
        }
    }

    private void ApplyAsrSegment(BatchSubtitleJob uiJob, BatchSubtitleProgress update)
    {
        ActiveJob = uiJob;

        if (update.SubtitleCount.HasValue)
            uiJob.SubtitleCount = update.SubtitleCount.Value;
        if (update.ProcessedTime.HasValue)
            uiJob.ProcessedTime = update.ProcessedTime;
        if (update.TotalDuration.HasValue)
            uiJob.TotalDuration = update.TotalDuration;

        if (uiJob.TotalDuration is { Ticks: > 0 } total && uiJob.ProcessedTime is { } processed)
        {
            uiJob.Progress = Math.Clamp(processed.TotalSeconds / total.TotalSeconds, 0, 1);
            uiJob.IsIndeterminateProgress = false;

            if (uiJob.StartedAt is { } startedAt)
            {
                double elapsed = (DateTimeOffset.Now - startedAt).TotalSeconds;
                if (elapsed >= 1.0)
                    uiJob.Throughput = $"x{processed.TotalSeconds / elapsed:0.0} realtime";
            }
        }

        string text = update.AsrSegmentText!.Trim();
        if (text.Length > 0)
        {
            uiJob.Transcript.Add(text);
            while (uiJob.Transcript.Count > 200)
                uiJob.Transcript.RemoveAt(0);
        }
    }

    private void UpdateSummary()
    {
        if (Jobs.Count == 0)
        {
            SummaryText = "0 files";
            return;
        }

        int completed = Jobs.Count(job => job.Status == BatchSubtitleStatus.Completed);
        int skipped = Jobs.Count(job => job.Status == BatchSubtitleStatus.Skipped);
        int failed = Jobs.Count(job => job.Status == BatchSubtitleStatus.Failed);
        int canceled = Jobs.Count(job => job.Status == BatchSubtitleStatus.Canceled);
        int running = Jobs.Count(job =>
            job.Status is BatchSubtitleStatus.RunningASR
                or BatchSubtitleStatus.QueuedForTranslation
                or BatchSubtitleStatus.Translating
                or BatchSubtitleStatus.Saving);

        int included = Jobs.Count(job => job.Include);
        SummaryText = $"{Jobs.Count} files ({included} selected) | {running} running | {completed} completed | {skipped} skipped | {failed} failed | {canceled} canceled";

        OnPropertyChanged(nameof(CanRetryFailed));
    }

    #region IDialogAware
    public string Title { get; set => Set(ref field, value); } = $"Batch Subtitles - {App.Name}";
    public double WindowWidth { get; set => Set(ref field, value); } = 1000;
    public double WindowHeight { get; set => Set(ref field, value); } = 620;

    public DialogCloseListener RequestClose { get; }

    public bool CanCloseDialog()
    {
        if (!IsRunning)
            return true;

        MessageBoxResult confirm = MessageBox.Show(
            "Batch subtitles are still running. Cancel and close this window?",
            "Batch subtitles",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return false;

        _cts?.Cancel();
        return false;
    }

    public void OnDialogClosed()
    {
        _cts?.Cancel();
        UnsubscribeJobs();
    }

    public void OnDialogOpened(IDialogParameters parameters)
    {
    }
    #endregion IDialogAware
}
