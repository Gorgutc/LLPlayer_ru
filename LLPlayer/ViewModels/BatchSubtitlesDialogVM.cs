using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
            }
        }
    }

    public string SummaryText { get; set => Set(ref field, value); } = string.Empty;

    public bool CanScan => !IsRunning && !IsScanning && Directory.Exists(FolderPath);
    public bool CanStart => !IsRunning && !IsScanning && Jobs.Count > 0;
    public bool CanCancel => IsRunning;
    public bool CanOpenOutputFolder => Directory.Exists(GetOutputFolder());

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
            List<string> mediaPaths = await Task.Run(() =>
                BatchVideoScanner.Scan(FolderPath, Recursive).ToList());

            Jobs.Clear();
            foreach (string mediaPath in mediaPaths)
            {
                Jobs.Add(new BatchSubtitleJob(mediaPath));
            }

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
        if (IsRunning)
            return;

        PersistBatchDefaults();

        List<BatchSubtitleJob> workerJobs = Jobs.Select(job => new BatchSubtitleJob(job.MediaPath)).ToList();
        Dictionary<string, BatchSubtitleJob> uiJobs = Jobs
            .GroupBy(job => job.MediaPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (BatchSubtitleJob job in Jobs)
        {
            job.Status = BatchSubtitleStatus.Pending;
            job.Error = null;
            job.SubtitleCount = 0;
            job.StartedAt = null;
            job.CompletedAt = null;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        UpdateSummary();

        try
        {
            Progress<BatchSubtitleProgress> progress = new(update =>
            {
                if (!uiJobs.TryGetValue(update.Job.MediaPath, out BatchSubtitleJob? uiJob))
                    return;

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
                    OverwriteExisting = OverwriteExisting,
                    Utf8Bom = FL.Config.Subs.SubsExportUTF8WithBom
                },
                progress);

            await processor.ProcessAsync(workerJobs, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            MarkPendingCanceled();
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
    }).ObservesCanExecute(() => CanStart);

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

    private void MarkPendingCanceled()
    {
        foreach (BatchSubtitleJob job in Jobs.Where(job => job.Status == BatchSubtitleStatus.Pending))
        {
            job.Status = BatchSubtitleStatus.Canceled;
            job.CompletedAt = DateTimeOffset.Now;
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

        SummaryText = $"{Jobs.Count} files | {running} running | {completed} completed | {skipped} skipped | {failed} failed | {canceled} canceled";
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
    }

    public void OnDialogOpened(IDialogParameters parameters)
    {
    }
    #endregion IDialogAware
}
