using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Shell;
using System.Windows.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Extensions;
using LLPlayer.Services;
using Microsoft.Win32;

namespace LLPlayer.ViewModels;

public class BatchSubtitlesDialogVM : Bindable, IDialogAware
{
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _probeAudioCts;
    // Watch-folder watcher (F-09). Dialog-scoped (created on toggle/open, disposed on close), like _cts.
    private BatchFolderWatcher? _watcher;
    // Media paths queued by the folder watcher and awaiting an auto-run. Only these are auto-started/drained, so a
    // deliberately-not-started manual Scan backlog is never swept up and a job left Pending after an explicit
    // Cancel is not resurrected by the next arrival.
    private readonly HashSet<string> _watchQueued = new(StringComparer.OrdinalIgnoreCase);
    private bool _initializing = true;
    private readonly BatchActivityService _activity;
    private readonly AppTrayService _tray;
    // The set of jobs in the current run — used to compute the overall progress shown on the taskbar + tray.
    private IReadOnlyList<BatchSubtitleJob>? _runScope;
    // Rough overall "~MM:SS left" estimate, recomputed each heartbeat; embedded in the summary + tray text.
    private string _overallEtaText = string.Empty;

    public FlyleafManager FL { get; }

    public BatchSubtitlesDialogVM(FlyleafManager fl, BatchActivityService activity, AppTrayService tray)
    {
        FL = fl;
        _activity = activity;
        _tray = tray;

        FolderPath = FL.Config.BatchSubtitles.LastFolder;
        Recursive = FL.Config.BatchSubtitles.Recursive;
        OverwriteExisting = FL.Config.BatchSubtitles.OverwriteExisting;
        SerializeAsrAndTranslate = FL.Config.BatchSubtitles.SerializeAsrAndTranslate;
        RunOnCpuWhenActive = FL.Config.BatchSubtitles.RunOnCpuWhenActive;
        IdleThresholdSeconds = FL.Config.BatchSubtitles.ActiveIdleThresholdSeconds;
        GenerateDubbing = FL.Config.BatchSubtitles.GenerateDubbing;
        PreferRussianAudio = FL.Config.BatchSubtitles.PreferRussianAudio;
        WatchFolder = FL.Config.BatchSubtitles.WatchFolder;
        RefreshVoices();
        FL.PlayerConfig.Subtitles.DubbingConfig.PropertyChanged += OnDubbingConfigPropertyChanged;
        _initializing = false;

        // NOTE: subscription to _activity.CancelRequested is done in OnDialogOpened (paired with the -= in
        // OnDialogClosed) so subscribe/unsubscribe stay symmetric across the actual open/close lifecycle.

        Jobs.CollectionChanged += JobsOnCollectionChanged;

        // Group the batch list by sub-folder so a recursive scan visibly runs folder-by-folder
        // (folder header + per-folder file count). Grouping is presentational only — scan order and
        // processing behaviour are unchanged. Use a dedicated view (NOT CollectionViewSource.GetDefaultView,
        // which would mutate the process-wide default view of Jobs) so grouping never leaks to other
        // consumers. Group contiguity follows the scanner's natural path sort (BatchVideoScanner ->
        // Utils.GetMoviesSorted).
        JobsView = new CollectionViewSource { Source = Jobs }.View;
        JobsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BatchSubtitleJob.FolderDisplay)));
    }

    public ObservableCollection<BatchSubtitleJob> Jobs { get; } = new();

    // Grouped (by FolderDisplay) view over Jobs; the DataGrid binds to this so files appear under their
    // sub-folder with a header + count when a recursive scan spans multiple folders.
    public ICollectionView JobsView { get; }

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
                if (!_initializing)
                    UpdateWatcher(); // re-point the watcher at the new folder when watching
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
                if (!_initializing)
                    UpdateWatcher(); // recursive scope changed → rebuild the watcher when watching
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

    // Background-friendliness: never run ASR + translation at once (so a GPU ASR engine and a local-LLM
    // translator don't both saturate the GPU). Applied to the next run, not a run already in progress.
    public bool SerializeAsrAndTranslate
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                FL.Config.BatchSubtitles.SerializeAsrAndTranslate = value;
                PersistBatchDefaults();
            }
        }
    }

    // While you actively use the PC, transcribe the next chunk on CPU (faster-whisper) instead of the GPU.
    public bool RunOnCpuWhenActive
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                FL.Config.BatchSubtitles.RunOnCpuWhenActive = value;
                PersistBatchDefaults();
            }
        }
    }

    // AI dubbing: after each .ru.srt, also render a Russian dub audio track (video.ru.dub.flac) via the
    // local TTS sidecar. Opt-in; when on, the run is serialized so the GPU TTS render never overlaps ASR.
    public bool GenerateDubbing
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                FL.Config.BatchSubtitles.GenerateDubbing = value;
                PersistBatchDefaults();
            }
        }
    }

    // Built-in dub voice bank for the picker next to "Generate Russian dub". GPU-free (never starts the
    // sidecar); includes the current DefaultVoiceId even if it is not a known preset so the ComboBox
    // never blanks. The selected value writes through to DubbingConfig.DefaultVoiceId, which the renderer
    // reads live at run start (BatchSubtitlesDialogVM builds DubbingRenderer from the live config).
    public ObservableCollection<TtsVoice> Voices { get; } = new();

    private void OnDubbingConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DubbingConfig.DefaultVoiceId) or nameof(DubbingConfig.CustomVoiceIds))
            RefreshVoices();
    }

    public void RefreshVoices()
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(RefreshVoices);
            return;
        }

        IReadOnlyList<TtsVoice> refreshed = VoiceBankResolver.ForConfig(
            FL.PlayerConfig.Subtitles.DubbingConfig.DefaultVoiceId,
            FL.PlayerConfig.Subtitles.DubbingConfig.CustomVoiceIds);

        for (int i = Voices.Count - 1; i >= 0; i--)
            if (!VoiceBankResolver.ContainsVoiceId(refreshed, Voices[i].Id))
                Voices.RemoveAt(i);

        foreach (TtsVoice voice in refreshed)
            if (!VoiceBankResolver.ContainsVoiceId(Voices, voice.Id))
                Voices.Add(voice);
    }

    // When on, a file with a Russian-tagged audio track has that track transcribed (Russian subtitles, no
    // translation); files without a Russian track are transcribed + translated as before. A per-file override
    // in the list always wins. Applied to the next run.
    public bool PreferRussianAudio
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                FL.Config.BatchSubtitles.PreferRussianAudio = value;
                PersistBatchDefaults();
            }
        }
    }

    // Watch-folder auto-batch (F-09): watch FolderPath for new videos and auto-process them. Toggling rebuilds
    // the watcher; the watcher itself lives while the dialog VM does (so it keeps running while minimized to tray).
    private bool _watchFolder;
    public bool WatchFolder
    {
        get => _watchFolder;
        set => SetWatchFolder(value, persist: true);
    }

    // A user toggle persists the choice; an internal turn-off (e.g. the watched folder vanished) updates the UI +
    // watcher WITHOUT touching the persisted config, so a transient failure does not silently disable the opt-in
    // across restarts. IMPORTANT: when persist is false we must NOT mutate FL.Config.BatchSubtitles.WatchFolder,
    // because the next unrelated PersistBatchDefaults() (a run, a scan, another setting) would write that live value
    // to disk. The in-session _watchFolder field is therefore authoritative for behaviour; the live config only
    // carries the user's persisted intent.
    private void SetWatchFolder(bool value, bool persist)
    {
        if (_watchFolder == value)
            return;

        _watchFolder = value;
        OnPropertyChanged(nameof(WatchFolder));
        if (persist)
        {
            FL.Config.BatchSubtitles.WatchFolder = value;
            PersistBatchDefaults();
        }
        if (!_initializing)
            UpdateWatcher();
        UpdateSummary();
    }

    // How long the keyboard/mouse must be idle before ASR switches back to the GPU.
    public int IdleThresholdSeconds
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, 5, 600);
            if (Set(ref field, clamped))
            {
                FL.Config.BatchSubtitles.ActiveIdleThresholdSeconds = field;
                PersistBatchDefaults();
            }
            else if (clamped != value)
            {
                // Out-of-range input coerced to the value already stored — re-push so the TextBox doesn't
                // keep showing the stale out-of-range text.
                OnPropertyChanged(nameof(IdleThresholdSeconds));
            }
        }
    }

    // True while the batch is running ASR on CPU because the user is active (drives the summary + tray status).
    public bool IsRunningOnCpu { get; private set => Set(ref field, value); }

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

    // Overall progress of the current run (0..1) + taskbar state, shown on the batch window's taskbar button
    // and mirrored to the tray icon so the user can watch progress with the player window closed.
    public double OverallProgress { get; private set => Set(ref field, value); }
    public TaskbarItemProgressState TaskbarProgressState { get; private set => Set(ref field, value); } = TaskbarItemProgressState.None;

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
            bool generateDubbing = GenerateDubbing;
            List<(string Path, bool HasTranslation, bool HasDub)> scanned = await Task.Run(() =>
                BatchVideoScanner.Scan(FolderPath, Recursive)
                    .Select(path => (
                        path,
                        HasTranslation: SubtitleOutputPathBuilder.TranslationExists(path),
                        HasDub: DubbingOutputPathBuilder.DubExistsAnyFormat(path)))
                    .ToList());

            UnsubscribeJobs();
            Jobs.Clear();
            _watchQueued.Clear(); // a manual rescan rebuilds the list; drop stale watch-queue markers
            foreach ((string mediaPath, bool hasTranslation, bool hasDub) in scanned)
            {
                BatchSubtitleJob job = new(mediaPath, FolderPath);
                BatchSubtitleScanPolicy.ApplyOutputState(job, hasTranslation, hasDub, overwrite, generateDubbing);

                job.PropertyChanged += OnJobPropertyChanged;
                Jobs.Add(job);
            }

            OnPropertyChanged(nameof(AllIncluded));
            UpdateSummary();

            // Populate the per-file audio-track picker in the background so the scan stays fast. Best-effort:
            // a probe failure just leaves that file's picker at "Auto".
            _probeAudioCts?.Cancel();
            _probeAudioCts = new CancellationTokenSource();
            _ = ProbeAudioTracksAsync(Jobs.Where(j => j.Include).ToList(), _probeAudioCts.Token);
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

    // Double-click a row (or pick it from the row's context menu) to play that video in the main player.
    // Brings the player window back from the tray if it was minimized, then opens the file — the generated
    // <name>.ru.srt beside it is picked up automatically by the local-subtitle search.
    public DelegateCommand<BatchSubtitleJob> CmdOpenInPlayer => field ??= new DelegateCommand<BatchSubtitleJob>(job =>
    {
        if (job is null)
            return;

        try
        {
            _tray.RestoreMainWindow();
            FL.Player.OpenAsync(job.MediaPath);
        }
        catch (Exception ex)
        {
            ErrorDialogHelper.ShowUnknownErrorPopup($"Cannot open video in player: {ex.Message}", "Batch subtitles", ex);
        }
    });

    // Probe each file's audio tracks off the UI thread and fill its picker. Sequential + best-effort so a
    // large folder does not spawn many concurrent demuxers and a single unreadable file never breaks the rest.
    private async Task ProbeAudioTracksAsync(IReadOnlyList<BatchSubtitleJob> jobs, CancellationToken token)
    {
        foreach (BatchSubtitleJob job in jobs)
        {
            if (token.IsCancellationRequested)
                return;

            try
            {
                MediaAudioProbeResult probe = await Task.Run(
                    () => new MediaAudioProbe(FL.PlayerConfig).Probe(job.MediaPath, token), token);

                // ObservableCollection must be mutated on the UI thread; also re-check the job is still listed
                // (a re-scan may have replaced it).
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested || !Jobs.Contains(job))
                        return;

                    job.AudioTracks.Clear();
                    // Leading "Auto" sentinel (StreamIndex -1) = automatic selection (prefer Russian).
                    job.AudioTracks.Add(new MediaAudioTrack(-1, Language.Unknown, "Auto (prefer Russian)", null, 0));
                    foreach (MediaAudioTrack track in probe.Tracks)
                        job.AudioTracks.Add(track);
                });
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Best-effort: leave this file's picker at "Auto".
            }
        }
    }

    // ===== Watch-folder auto-batch (F-09) =====

    // Start/stop/re-point the folder watcher to match the current toggle + folder + recursive flag. Called from
    // the WatchFolder/FolderPath/Recursive setters and from OnDialogOpened. The watcher instance lives for the
    // VM's lifetime, so it keeps watching while the dialog is minimized to the tray (where OnDialogClosed does NOT
    // run); it is torn down on toggle-off, real close, or app quit.
    private void UpdateWatcher()
    {
        if (_watchFolder && Directory.Exists(FolderPath))
        {
            _watcher ??= CreateWatcher();
            _watcher.Start(FolderPath, Recursive);
        }
        else
        {
            _watcher?.Stop();
        }
    }

    private BatchFolderWatcher CreateWatcher()
    {
        BatchFolderWatcher watcher = new();
        watcher.FileReady += OnWatchedFileReady;
        watcher.Error += OnWatchedError;
        return watcher;
    }

    // A new video finished copying into the watched folder (raised on the UI thread). Add it with the same rules
    // as a manual scan, then auto-start if nothing is running.
    private void OnWatchedFileReady(string path)
    {
        bool hasTranslation = SubtitleOutputPathBuilder.TranslationExists(path);

        WatchEnqueueDecision decision = WatchFolderPolicy.ShouldEnqueue(
            path,
            Jobs.Select(j => j.MediaPath),
            Utils.IsVideoExtension,
            hasTranslation,
            OverwriteExisting);

        if (decision != WatchEnqueueDecision.Enqueue)
            return;

        bool hasDub = DubbingOutputPathBuilder.DubExistsAnyFormat(path);

        BatchSubtitleJob job = new(path, FolderPath);
        BatchSubtitleScanPolicy.ApplyOutputState(job, hasTranslation, hasDub, OverwriteExisting, GenerateDubbing);
        job.PropertyChanged += OnJobPropertyChanged;
        Jobs.Add(job);
        // Only track it for auto-run if it is actually going to be processed. An already-completed output (overwrite
        // on) is added as Completed by ApplyOutputState; queuing it would just leak in the set (MaybeAutoStartWatch
        // runs only Pending), so skip it.
        if (job.Status == BatchSubtitleStatus.Pending)
            _watchQueued.Add(path);

        OnPropertyChanged(nameof(AllIncluded));
        UpdateSummary();

        // Fill this file's audio-track picker in the background (best-effort), like CmdScan does. Use a live token
        // (a prior scan may have cancelled the shared CTS).
        if (_probeAudioCts == null || _probeAudioCts.IsCancellationRequested)
            _probeAudioCts = new CancellationTokenSource();
        _ = ProbeAudioTracksAsync([job], _probeAudioCts.Token);

        MaybeAutoStartWatch();
    }

    // If watching and idle, start a run for the WATCH-ADDED Pending files only (tracked in _watchQueued — a manual
    // Scan backlog is left for the explicit Start button). RunAsync's own IsRunning guard is the hard safety net;
    // this only ever starts a run when none is in progress (otherwise the files wait as Pending and are drained
    // from RunAsync's finally). Paths are removed from _watchQueued as they enter a run, so a cancelled run does
    // not leave them to be resurrected by the next arrival.
    private void MaybeAutoStartWatch()
    {
        if (!_watchFolder || !IsIdle)
            return;

        List<BatchSubtitleJob> pending = Jobs
            .Where(j => j.Include && j.Status == BatchSubtitleStatus.Pending && _watchQueued.Contains(j.MediaPath))
            .ToList();

        if (pending.Count == 0)
            return;

        foreach (BatchSubtitleJob job in pending)
            _watchQueued.Remove(job.MediaPath);

        _ = RunAsync(pending);
    }

    private void OnWatchedError(string message)
    {
        // The watched folder became inaccessible (deleted/renamed). Stop watching for this session but DON'T persist
        // the opt-in off — a transient failure (e.g. a network-share blip) should not silently disable the feature
        // across restarts; reopening (or the folder coming back) re-validates and resumes.
        SetWatchFolder(false, persist: false);
        ErrorDialogHelper.ShowKnownErrorPopup($"Folder watch stopped: {message}", "Batch subtitles");
    }

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

        // VC++ preflight (T-02): whisper.cpp loads its native CRT in-process; without the redistributable that
        // load crashes the app. The transcriber's constructor also guards this, but the throw would surface as
        // a generic "unknown error" popup. Preflight here so a missing runtime is shown as a clean, actionable
        // known-error modal (INSTALL → Microsoft download page), matching the interactive ASR snackbar's fix.
        // The batch dialog is a separate non-modal window, so the main-window snackbar would not reliably cover
        // it — a Topmost known-error modal is the right surface. faster-whisper (external exe) is not gated.
        if (BatchAsrTranscriber.RequiresVcRedistPreflight(FL.PlayerConfig) &&
            !VcRedistChecker.IsRuntimePresent(out _))
        {
            ErrorDialogHelper.ShowKnownErrorPopup(
                VcRedistChecker.BuildMissingMessage("Speech-to-text (whisper.cpp)"),
                KnownErrorType.Configuration,
                KnownErrorActionKeys.InstallVcRedist,
                "INSTALL");
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
            job.Elapsed = null;
            job.Eta = null;
            job.Transcript.Clear();
        }

        ActiveJob = null;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        _runScope = toRun;
        _activity.SetRunning(true);
        UpdateSummary();

        // While the user is active, faster-whisper transcribes the next chunk on CPU so the GPU stays free.
        // The monitor is polled per chunk inside the engine; the timer just reflects the state in the UI.
        // Only faster-whisper supports the device switch, so the "on CPU" status applies to that engine only.
        bool cpuFallbackApplies = FL.PlayerConfig.Subtitles.ASREngine == SubASREngineType.FasterWhisper;
        BatchActivityMonitor monitor = new(
            () => FL.Config.BatchSubtitles.RunOnCpuWhenActive,
            () => FL.Config.BatchSubtitles.ActiveIdleThresholdSeconds);

        DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        statusTimer.Tick += (_, _) =>
        {
            // 1-second heartbeat: keep the UI moving (elapsed/ETA tick) even between slow ASR segment reports,
            // so the user can see it's alive and roughly how long is left. IsRunningOnCpu setter no-ops if same.
            // Cosmetic — must never crash the dispatcher, so swallow any error.
            try
            {
                IsRunningOnCpu = cpuFallbackApplies && IsRunning && monitor.IsUserActive();
                RefreshLiveEstimates();
                UpdateSummary();
            }
            catch
            {
                // ignore — the next tick will refresh
            }
        };
        statusTimer.Start();

        bool canceled = false;
        IDubbingRenderer? dubber = null;
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
                    uiJob.Elapsed = null;
                    uiJob.Eta = null;
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

            DubbingConfig dubbingConfig = FL.PlayerConfig.Subtitles.DubbingConfig;
            dubber = GenerateDubbing ? new DubbingRenderer(dubbingConfig) : null;
            IDubbingVoiceAssignmentProvider? voiceAssignments = GenerateDubbing
                ? CreateCurrentDubbingVoiceAssignments()
                : null;

            BatchSubtitleProcessor processor = new(
                new BatchAsrTranscriber(
                    FL.PlayerConfig,
                    monitor.IsUserActive,
                    FL.Config.BatchSubtitles.PreferRussianAudio,
                    mediaPath => uiJobs.TryGetValue(mediaPath, out BatchSubtitleJob? uiJob)
                        ? uiJob.AudioStreamIndexOverride
                        : null),
                new BatchSubtitleTranslator(FL.PlayerConfig.Subtitles),
                new SrtSubtitleWriter(new UTF8Encoding(FL.Config.Subs.SubsExportUTF8WithBom)),
                new BatchSubtitleOptions
                {
                    Recursive = Recursive,
                    OverwriteExisting = OverwriteExisting || forceOverwrite,
                    Utf8Bom = FL.Config.Subs.SubsExportUTF8WithBom,
                    SerializeAsrAndTranslate = SerializeAsrAndTranslate,
                    GenerateDubbing = GenerateDubbing,
                    DubbingOutputFormat = dubbingConfig.OutputFormat
                },
                progress,
                dubber,
                voiceAssignments);

            await processor.ProcessAsync(workerJobs, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            MarkPendingCanceled(toRun);
            UpdateSummary();
        }
        catch (Exception ex)
        {
            ErrorDialogHelper.ShowUnknownErrorPopup($"Batch subtitles failed: {ex.Message}", "Batch subtitles", ex);
        }
        finally
        {
            try
            {
                if (dubber is not null)
                    await dubber.DisposeAsync();
            }
            catch
            {
                // Best-effort sidecar cleanup must not prevent the batch UI/activity state from resetting.
            }

            statusTimer.Stop();
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
            _runScope = null;
            IsRunningOnCpu = false;
            _activity.SetRunning(false, canceled);
            UpdateSummary();

            // Watch-folder (F-09) drain: files that arrived during this run sit as Pending. Start the next run once
            // this one fully unwinds — BeginInvoke avoids re-entering RunAsync from its own finally. On an explicit
            // Cancel we drop the watch queue (Cancel means stop — nothing auto-restarts; the rows stay Pending for a
            // manual Start). Otherwise drain: MaybeAutoStartWatch re-checks IsIdle and filters the watch-queued
            // Pending files, so it is idempotent and self-terminating.
            if (canceled)
                _watchQueued.Clear();
            else if (_watchFolder)
                Application.Current?.Dispatcher.BeginInvoke(MaybeAutoStartWatch);
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

    private IDubbingVoiceAssignmentProvider? CreateCurrentDubbingVoiceAssignments()
    {
        List<IDubbingVoiceAssignmentProvider> providers = [];

        // Current-session snapshot: the live sidebar per-line voices for the open local media (highest priority).
        string? mediaPath = GetCurrentLocalMediaPath();
        if (mediaPath is not null)
        {
            List<SubtitleData> assigned = [];
            for (int i = 0; i < 2; i++)
            {
                foreach (SubtitleData sub in FL.Player.SubtitlesManager[i].SnapshotSubs())
                {
                    if (!string.IsNullOrWhiteSpace(sub.AssignedVoiceId))
                        assigned.Add(sub.Clone());
                }
            }

            if (assigned.Count > 0)
                providers.Add(DubbingVoiceAssignmentMap.FromCurrentSubtitles(mediaPath, assigned));
        }

        // F-16 persistence (opt-in): also apply per-line voices saved on disk (video.ru.voices.json) for EACH batch
        // file, so a "translate now, dub later, restart" run still uses the user's saved voices — for any file in the
        // batch, not just the currently open one. Current-session choices (added first) win; the disk provider fills
        // the rest.
        if (FL.PlayerConfig.Subtitles.PersistPerLineVoices)
            providers.Add(new DiskVoiceAssignmentProvider());

        return providers.Count switch
        {
            0 => null,
            1 => providers[0],
            _ => new CompositeVoiceAssignmentProvider([.. providers])
        };
    }

    private string? GetCurrentLocalMediaPath()
    {
        string?[] candidates =
        [
            FL.Player.Playlist.Selected?.Url,
            FL.Player.Playlist.Selected?.DirectUrl,
            FL.Player.Playlist.Url,
        ];

        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                continue;

            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                return candidate;
            }
        }

        return null;
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
            persisted.BatchSubtitles.SerializeAsrAndTranslate = FL.Config.BatchSubtitles.SerializeAsrAndTranslate;
            persisted.BatchSubtitles.RunOnCpuWhenActive = FL.Config.BatchSubtitles.RunOnCpuWhenActive;
            persisted.BatchSubtitles.ActiveIdleThresholdSeconds = FL.Config.BatchSubtitles.ActiveIdleThresholdSeconds;
            persisted.BatchSubtitles.GenerateDubbing = FL.Config.BatchSubtitles.GenerateDubbing;
            persisted.BatchSubtitles.PreferRussianAudio = FL.Config.BatchSubtitles.PreferRussianAudio;
            persisted.BatchSubtitles.WatchFolder = FL.Config.BatchSubtitles.WatchFolder;
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
            SummaryText = _watchFolder ? "👁 Watching — 0 files" : "0 files";
            return;
        }

        // Refresh overall % + ETA first so the summary line below can include the fresh estimate.
        UpdateOverallProgress();

        int completed = Jobs.Count(job => job.Status == BatchSubtitleStatus.Completed);
        int skipped = Jobs.Count(job => job.Status == BatchSubtitleStatus.Skipped);
        int failed = Jobs.Count(job => job.Status == BatchSubtitleStatus.Failed);
        int canceled = Jobs.Count(job => job.Status == BatchSubtitleStatus.Canceled);
        int running = Jobs.Count(job =>
            job.Status is BatchSubtitleStatus.RunningASR
                or BatchSubtitleStatus.QueuedForTranslation
                or BatchSubtitleStatus.Translating
                or BatchSubtitleStatus.Saving
                or BatchSubtitleStatus.Dubbing);

        int included = Jobs.Count(job => job.Include);
        string watchPrefix = _watchFolder ? "👁 Watching — " : "";
        string cpuPrefix = IsRunning && IsRunningOnCpu ? "🖥 ASR on CPU (you're active) — " : "";
        string etaSuffix = string.IsNullOrEmpty(_overallEtaText) ? "" : $" | {_overallEtaText}";
        SummaryText = $"{watchPrefix}{cpuPrefix}{Jobs.Count} files ({included} selected) | {running} running | {completed} completed | {skipped} skipped | {failed} failed | {canceled} canceled{etaSuffix}";

        OnPropertyChanged(nameof(CanRetryFailed));
    }

    // Aggregate progress across the current run for the taskbar button + tray icon. Finished files count as
    // full; the file actively transcribing contributes its ASR fraction; files in the translate/save tail
    // count as nearly done. Mirrored to the BatchActivityService so the tray shows it with the window hidden.
    private void UpdateOverallProgress()
    {
        IReadOnlyList<BatchSubtitleJob>? scope = _runScope;
        if (!IsRunning || scope is null || scope.Count == 0)
        {
            OverallProgress = 0;
            TaskbarProgressState = TaskbarItemProgressState.None;
            _overallEtaText = string.Empty;
            _activity.ReportProgress(0, string.Empty);
            return;
        }

        int total = scope.Count;
        int done = 0;
        double sum = 0;
        foreach (BatchSubtitleJob job in scope)
        {
            switch (job.Status)
            {
                case BatchSubtitleStatus.Completed:
                case BatchSubtitleStatus.Skipped:
                case BatchSubtitleStatus.Failed:
                case BatchSubtitleStatus.Canceled:
                    sum += 1;
                    done++;
                    break;
                case BatchSubtitleStatus.RunningASR:
                    sum += Math.Clamp(job.Progress, 0, 1);
                    break;
                case BatchSubtitleStatus.QueuedForTranslation:
                case BatchSubtitleStatus.Translating:
                case BatchSubtitleStatus.Saving:
                case BatchSubtitleStatus.Dubbing:
                    sum += 0.95; // ASR done, finishing up (translate/write/dub tail)
                    break;
            }
        }

        double overall = Math.Clamp(sum / total, 0, 1);
        OverallProgress = overall;
        // Still progressing (on CPU) when active — Normal, not Paused.
        TaskbarProgressState = TaskbarItemProgressState.Normal;
        _overallEtaText = ComputeOverallEtaText(scope);

        string prefix = IsRunningOnCpu ? "CPU " : "";
        string etaForTray = string.IsNullOrEmpty(_overallEtaText) ? "" : $" • {_overallEtaText}";
        _activity.ReportProgress(overall, $"{prefix}{done}/{total} • {(int)Math.Round(overall * 100)}%{etaForTray}");
    }

    // Per-second heartbeat: refresh the active file's wall-clock elapsed + an approximate ETA so the row keeps
    // moving even between (slow) ASR segment reports. ETA = remaining audio / the cumulative realtime factor.
    private void RefreshLiveEstimates()
    {
        BatchSubtitleJob? job = ActiveJob;
        if (job is null || job.Status != BatchSubtitleStatus.RunningASR || job.StartedAt is not { } started)
            return;

        TimeSpan elapsed = DateTimeOffset.Now - started;
        job.Elapsed = elapsed;

        if (job.ProcessedTime is { TotalSeconds: > 0 } processed
            && job.TotalDuration is { Ticks: > 0 } total
            && elapsed.TotalSeconds >= 1)
        {
            double factor = processed.TotalSeconds / elapsed.TotalSeconds; // realtime factor (cumulative = smoother)
            double remainingAudio = Math.Max(0, total.TotalSeconds - processed.TotalSeconds);
            job.Eta = factor > 0 ? TimeSpan.FromSeconds(remainingAudio / factor) : null;
        }
    }

    // Rough overall ETA: the active file's remaining time + (pending files × average wall-time of files already
    // finished in this run). Returns "" when there's nothing to estimate. Approximate by design.
    private string ComputeOverallEtaText(IReadOnlyList<BatchSubtitleJob> scope)
    {
        double seconds = 0;
        bool any = false;

        if (ActiveJob is { Status: BatchSubtitleStatus.RunningASR, Eta: { } eta })
        {
            seconds += eta.TotalSeconds;
            any = true;
        }

        List<double> finishedWall = scope
            .Where(j => j.Status is BatchSubtitleStatus.Completed or BatchSubtitleStatus.Skipped
                or BatchSubtitleStatus.Failed or BatchSubtitleStatus.Canceled
                && j.StartedAt is { } && j.CompletedAt is { })
            .Select(j => (j.CompletedAt!.Value - j.StartedAt!.Value).TotalSeconds)
            .Where(s => s > 0)
            .ToList();
        int pending = scope.Count(j => j.Status == BatchSubtitleStatus.Pending);

        if (finishedWall.Count > 0 && pending > 0)
        {
            seconds += finishedWall.Average() * pending;
            any = true;
        }

        if (!any || seconds <= 0)
            return string.Empty;

        return $"~{FormatDuration(TimeSpan.FromSeconds(seconds))} left";
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalSeconds < 1)
            return "0:00";
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private void OnAppQuitCancelRequested(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        _watcher?.Stop();
    }

    #region IDialogAware
    public string Title { get; set => Set(ref field, value); } = $"Batch Subtitles - {App.Name}";
    // Open wide by default so the folder-grouped list shows full file names without manual resizing
    // (columns are fixed-width, so a smaller window scrolls horizontally rather than squishing).
    public double WindowWidth { get; set => Set(ref field, value); } = 1650;
    public double WindowHeight { get; set => Set(ref field, value); } = 840;

    public DialogCloseListener RequestClose { get; }

    public bool CanCloseDialog()
    {
        // App is quitting (tray "Quit" / Exit App): cancel silently and allow the close, no prompt.
        if (_activity.IsShuttingDown)
        {
            _cts?.Cancel();
            return true;
        }

        // A batch is running OR folder-watch is active: closing the dialog would cancel the run / stop the watch,
        // so instead minimize this window to the system tray and keep going — consistent with closing the main
        // player window. The watch (F-09 owner scope) is meant to survive a tray-minimize; it stops on toggle-off,
        // the tray's Quit, or app shutdown. Reopen from the main window or the tray menu (Batch subtitles…).
        if (IsRunning || _watchFolder)
        {
            MinimizeOwnWindowToTray();

            // Returning false sets e.Cancel=true in Prism's Closing handler, so the window is NOT closed/disposed
            // and OnDialogClosed never runs — the CTS / run / watcher / VM survive while the window is merely hidden.
            return false;
        }

        return true;
    }

    private void MinimizeOwnWindowToTray()
    {
        Window? window = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => ReferenceEquals(w.DataContext, this));

        // Should always match the open dialog window (Prism sets its DataContext to this VM). Only show the
        // "running in the tray" hint when we actually hid a window, so the balloon can't lie if it ever misses.
        if (window == null)
            return;

        window.Hide();
        _tray.NotifyBatchMinimizedToTray();
    }

    public void OnDialogClosed()
    {
        _cts?.Cancel();
        _probeAudioCts?.Cancel();
        // Real close (not a tray-minimize, which returns false from CanCloseDialog and never reaches here): tear
        // down the folder watcher so it doesn't outlive the dialog session.
        _watcher?.Dispose();
        _watcher = null;
        UnsubscribeJobs();
        _activity.CancelRequested -= OnAppQuitCancelRequested;
        FL.PlayerConfig.Subtitles.DubbingConfig.PropertyChanged -= OnDubbingConfigPropertyChanged;
        // Drop the dialog-open flag so the app can leave the tray / restore the player once nothing is
        // running. If a run was in progress, the cancellation above lets RunAsync's finally clear IsRunning
        // (best-effort — on an app quit the process may exit before that continuation runs).
        _activity.SetDialogOpen(false);
    }

    public void OnDialogOpened(IDialogParameters parameters)
    {
        // Marks the batch surface as open so closing the main window keeps the app alive in the tray, and
        // subscribes to the app-quit cancel signal (paired with the -= in OnDialogClosed).
        _activity.SetDialogOpen(true);
        _activity.CancelRequested += OnAppQuitCancelRequested;

        // Resume watching if the toggle was persisted on (and re-validate the folder still exists).
        UpdateWatcher();
    }
    #endregion IDialogAware
}
