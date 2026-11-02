using System.Collections.ObjectModel;
using System.ComponentModel;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;

namespace LLPlayer.Core.Batch;

/// <summary>App-owned queue. Native workers receive detached jobs; the dispatcher alone changes UI jobs.</summary>
public sealed class BatchWorkflow : NotifyPropertyChanged, IAsyncDisposable
{
    readonly IBatchRunFactory factory;
    readonly IBatchFolderWatcher watcher;
    readonly IBatchMediaCatalog catalog;
    readonly Action<Action> dispatch;
    readonly HashSet<string> watchQueued = new(PathComparer);
    CancellationTokenSource? runCancellation, scanCancellation, probeCancellation;
    Task runTask = Task.CompletedTask, scanTask = Task.CompletedTask, probeTask = Task.CompletedTask;
    IReadOnlyList<BatchSubtitleJob> runScope = [];
    IBatchRun? activeRun;
    bool disposed, cancelRequested;
    readonly object teardownGate = new();
    Task? teardownTask;
    long activeWatchSession;
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <param name="dispatch">Synchronously execute on the UI thread (Invoke), or inline in tests.</param>
    public BatchWorkflow(BatchSettings settings, IBatchRunFactory factory, IBatchFolderWatcher watcher,
        Action<Action> dispatch, IBatchMediaCatalog catalog)
    {
        Settings = settings; this.factory = factory; this.watcher = watcher; this.dispatch = dispatch; this.catalog = catalog;
        watcher.FileReady += OnFileReady; watcher.Error += OnWatchError;
        Jobs.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null) foreach (BatchSubtitleJob job in args.OldItems) job.PropertyChanged -= OnJobChanged;
            if (args.NewItems != null) foreach (BatchSubtitleJob job in args.NewItems) job.PropertyChanged += OnJobChanged;
            Refresh();
        };
    }
    public BatchSettings Settings { get; private set; }
    public ObservableCollection<BatchSubtitleJob> Jobs { get; } = [];
    public bool WatchFolder { get; private set => Set(ref field, value); }
    public bool IsOpen { get; private set => Set(ref field, value); }
    public bool IsRunning { get; private set => Set(ref field, value); }
    public bool LastRunCanceled { get; private set => Set(ref field, value); }
    public bool IsScanning { get; private set => Set(ref field, value); }
    public bool IsRunningOnCpu => IsRunning && activeRun?.IsRunningOnCpu == true;
    public bool IsIdle => !IsRunning && !IsScanning;
    public bool KeepAlive => !disposed && (IsOpen || IsRunning || IsScanning || WatchFolder);
    public bool CanScan => !disposed && IsIdle && catalog.FolderExists(Settings.LastFolder);
    public bool CanStart => !disposed && IsIdle && Jobs.Any(j => j.Include);
    public bool CanCancel => IsRunning || IsScanning;
    public bool CanRetryFailed => !disposed && IsIdle && Jobs.Any(j => j.Status == BatchSubtitleStatus.Failed);
    public string Message { get; private set => Set(ref field, value); } = "";
    public string Summary { get; private set => Set(ref field, value); } = "0 files";
    public double OverallProgress { get; private set => Set(ref field, value); }
    public string EtaText { get; private set => Set(ref field, value); } = "";
    public BatchSubtitleJob? ActiveJob { get; private set => Set(ref field, value); }
    public event Action? Changed;

    public void Open() { ObjectDisposedException.ThrowIf(disposed, this); IsOpen = true; RefreshWatcher(); }
    public void Closed()
    {
        if (disposed) return;
        IsOpen = false; activeWatchSession = 0; WatchFolder = false; watcher.Stop();
        scanCancellation?.Cancel(); probeCancellation?.Cancel(); Refresh();
    }
    public void SetMessage(string message) { Message = message; Refresh(); }
    public void ApplySettings(BatchSettings settings)
    {
        settings.Validate();
        bool scopeChanged = settings.LastFolder != Settings.LastFolder || settings.Recursive != Settings.Recursive;
        bool repoint = scopeChanged || settings.WatchFolder != Settings.WatchFolder;
        if (scopeChanged)
        {
            // A replacement watch never owns the previous folder's pending automatic admissions.
            // Keep their visible rows so the user can still select/start them explicitly.
            watchQueued.Clear();
            if (IsScanning)
            {
                scanCancellation?.Cancel();
                Message = "Folder scan canceled because its folder or recursive scope changed. Scan the current folder again.";
            }
        }
        Settings = settings.Clone(); Raise(nameof(Settings));
        if (repoint) RefreshWatcher(); else Refresh();
    }
    public void RefreshWatcher()
    {
        activeWatchSession = 0; WatchFolder = false; watcher.Stop();
        if (!disposed && IsOpen && Settings.WatchFolder)
        {
            try
            {
                if (!catalog.FolderExists(Settings.LastFolder)) throw new DirectoryNotFoundException("The watched folder is not accessible.");
                activeWatchSession = watcher.Start(Settings.LastFolder, Settings.Recursive); WatchFolder = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { Message = $"Folder watch stopped: {ex.Message}"; }
        }
        if (!WatchFolder) watchQueued.Clear();
        Refresh();
    }
    public Task ScanAsync()
    {
        if (!CanScan) return Task.CompletedTask;
        scanTask = ScanCoreAsync(); return scanTask;
    }
    async Task ScanCoreAsync()
    {
        IsScanning = true; Message = "Scanning…"; Refresh();
        scanCancellation?.Dispose(); scanCancellation = new();
        var token = scanCancellation.Token;
        BatchSettings options = Settings.Clone();
        try
        {
            var paths = await catalog.ScanAsync(options.LastFolder, options.Recursive, token);
            token.ThrowIfCancellationRequested();
            // Ready events admitted during the asynchronous scan own their row and queue identity.
            // A scan snapshot can predate the copy/rename, so retain even watched files absent from it.
            var admitted = Jobs.Where(job => watchQueued.Contains(job.MediaPath)).ToDictionary(job => job.MediaPath, PathComparer);
            string root = Path.GetFullPath(options.LastFolder);
            var combined = paths.Select(path => Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path, root))
                .Concat(admitted.Keys).Distinct(PathComparer).ToList();
            var merged = Utils.GetMoviesSorted(combined).Select(path => admitted.TryGetValue(path, out var job) ? job : CreateJob(path, options)).ToArray();
            foreach (var old in Jobs) old.PropertyChanged -= OnJobChanged;
            Jobs.Clear();
            foreach (var job in merged) Jobs.Add(job);
            watchQueued.IntersectWith(merged.Select(job => job.MediaPath));
            Message = ""; BeginProbing(Jobs.ToArray());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Message = $"Cannot scan folder: {ex.Message}"; }
        finally { IsScanning = false; Refresh(); MaybeStartWatch(); }
    }
    BatchSubtitleJob CreateJob(string path, BatchSettings options)
    {
        var job = new BatchSubtitleJob(path, options.LastFolder);
        BatchSubtitleScanPolicy.ApplyOutputState(job, catalog.TranslationExists(path), catalog.DubExists(path), options.OverwriteExisting, options.GenerateDubbing);
        return job;
    }
    public Task StartAsync() => BeginRun(Jobs.Where(j => j.Include).ToArray(), false);
    public Task RetryAsync(BatchSubtitleJob job) => BeginRun([job], true);
    public Task RetryFailedAsync() => BeginRun(Jobs.Where(j => j.Status == BatchSubtitleStatus.Failed).ToArray(), true);
    Task BeginRun(IReadOnlyList<BatchSubtitleJob> scope, bool overwrite)
    {
        if (!IsIdle || disposed || scope.Count == 0) return Task.CompletedTask;
        cancelRequested = false; LastRunCanceled = false; IsRunning = true; runCancellation = new();
        runTask = RunAndDrainAsync(scope, overwrite, runCancellation.Token); return runTask;
    }
    async Task RunAndDrainAsync(IReadOnlyList<BatchSubtitleJob> scope, bool overwrite, CancellationToken token)
    {
        try
        {
            do
            {
                foreach (var job in scope) { watchQueued.Remove(job.MediaPath); Reset(job); }
                runScope = scope; ActiveJob = null; Refresh();
                BatchSettings options = Settings.Clone(); options.OverwriteExisting |= overwrite;
                var workers = scope.Select(j => new BatchSubtitleJob(j.MediaPath) { AudioStreamIndexOverride = j.AudioStreamIndexOverride }).ToArray();
                var byPath = scope.ToDictionary(j => j.MediaPath, PathComparer);
                try
                {
                    activeRun = factory.Create(options, workers);
                    await activeRun.ProcessAsync(new CallbackProgress(update => dispatch(() =>
                    {
                        if (!disposed && byPath.TryGetValue(update.Job.MediaPath, out var ui)) ApplyProgress(ui, update);
                    })), token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    Message = $"Batch subtitles failed: {ex.Message}";
                    foreach (var job in scope.Where(j => !IsTerminal(j.Status)))
                    { job.Status = BatchSubtitleStatus.Failed; job.Error = ex.Message; job.CompletedAt = DateTimeOffset.Now; }
                }
                finally
                {
                    if (activeRun != null)
                    {
                        try { await activeRun.DisposeAsync(); }
                        catch (Exception ex) { Message = $"Batch cleanup failed: {ex.Message}"; }
                        activeRun = null;
                    }
                }
                if (token.IsCancellationRequested)
                {
                    foreach (var job in scope.Where(j => !IsTerminal(j.Status)))
                    { job.Status = BatchSubtitleStatus.Canceled; job.CompletedAt = DateTimeOffset.Now; }
                    watchQueued.Clear(); break;
                }
                scope = WatchFolder && !disposed ? TakeWatchQueue() : []; overwrite = false;
            } while (scope.Count > 0);
        }
        finally
        {
            LastRunCanceled = cancelRequested || token.IsCancellationRequested;
            if (cancelRequested) watchQueued.Clear();
            runScope = []; runCancellation?.Dispose(); runCancellation = null;
            IsRunning = false; Refresh();
        }
    }
    public void Cancel() { cancelRequested = true; watchQueued.Clear(); runCancellation?.Cancel(); scanCancellation?.Cancel(); }
    public async Task DrainAsync() { await runTask; await scanTask; await probeTask; }
    void OnFileReady(BatchWatchReady ready)
    {
        dispatch(() =>
        {
        if (disposed || !WatchFolder || ready.SessionId != activeWatchSession) return;
        List<BatchSubtitleJob> admitted = [];
        foreach (string path in Utils.GetMoviesSorted(ready.Paths.ToList()))
        {
        if (!IsWithinWatchScope(path)) continue;
        bool translated = catalog.TranslationExists(path);
        bool done = translated && (!Settings.GenerateDubbing || catalog.DubExists(path));
        if (WatchFolderPolicy.ShouldEnqueue(path, Jobs.Where(j => PathComparer.Equals(j.MediaPath, path)).Select(j => j.MediaPath),
            Utils.IsVideoExtension, done, Settings.OverwriteExisting) != WatchEnqueueDecision.Enqueue) continue;
        var job = CreateJob(path, Settings); job.Status = BatchSubtitleStatus.Pending; job.CompletedAt = null;
        Jobs.Add(job); watchQueued.Add(path); admitted.Add(job);
        }
        if (admitted.Count > 0) BeginProbing(admitted, replace: false);
        MaybeStartWatch();
        });
    }
    bool IsWithinWatchScope(string path)
    {
        try
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(Settings.LastFolder), Path.GetFullPath(path));
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return false;
            return Settings.Recursive || string.IsNullOrEmpty(Path.GetDirectoryName(relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
    void OnWatchError(BatchWatchError error)
    {
        dispatch(() =>
        {
            if (disposed || error.SessionId != activeWatchSession) return;
            activeWatchSession = 0; WatchFolder = false; watcher.Stop(); watchQueued.Clear(); Message = $"Folder watch stopped: {error.Message}"; Refresh();
        });
    }
    IReadOnlyList<BatchSubtitleJob> TakeWatchQueue()
    {
        var pending = Jobs.Where(j => j.Include && j.Status == BatchSubtitleStatus.Pending && watchQueued.Contains(j.MediaPath))
            .ToDictionary(job => job.MediaPath, PathComparer);
        return Utils.GetMoviesSorted(pending.Keys.ToList()).Select(path => pending[path]).ToArray();
    }
    void MaybeStartWatch() { if (WatchFolder && IsIdle && !disposed) _ = BeginRun(TakeWatchQueue(), false); }
    void BeginProbing(IReadOnlyList<BatchSubtitleJob> scope, bool replace = true)
    {
        if (replace) { probeCancellation?.Cancel(); probeCancellation?.Dispose(); probeCancellation = new(); }
        else probeCancellation ??= new();
        Task previous = probeTask; probeTask = ProbeAsync(previous, scope, probeCancellation.Token);
    }
    async Task ProbeAsync(Task previous, IReadOnlyList<BatchSubtitleJob> scope, CancellationToken token)
    {
        await previous;
        foreach (var job in scope)
        {
            try
            {
                token.ThrowIfCancellationRequested(); var tracks = await catalog.ProbeAsync(job.MediaPath, token); token.ThrowIfCancellationRequested();
                if (disposed || !Jobs.Contains(job)) continue;
                job.AudioTracks.Clear(); job.AudioTracks.Add(new MediaAudioTrack(-1, Language.Unknown, "Auto", null, 0));
                foreach (var track in tracks) job.AudioTracks.Add(track);
            }
            catch (OperationCanceledException) { break; }
            catch { /* Probe is best effort. The actual ASR failure remains visible on the job. */ }
        }
    }
    static void Reset(BatchSubtitleJob job)
    {
        job.Status = BatchSubtitleStatus.Pending; job.Error = null; job.SubtitleCount = 0;
        job.StartedAt = null; job.CompletedAt = null; job.ProcessedTime = null; job.TotalDuration = null;
        job.Progress = 0; job.IsIndeterminateProgress = false; job.Throughput = ""; job.Elapsed = null; job.Eta = null; job.Transcript.Clear();
    }
    void ApplyProgress(BatchSubtitleJob job, BatchSubtitleProgress p)
    {
        job.Status = p.Status;
        if (p.Error != null) job.Error = p.Error;
        if (p.StartedAt != null) job.StartedAt = p.StartedAt;
        if (p.CompletedAt != null) job.CompletedAt = p.CompletedAt;
        if (p.SubtitleCount != null) job.SubtitleCount = p.SubtitleCount.Value;
        if (p.ProcessedTime != null) job.ProcessedTime = p.ProcessedTime;
        if (p.TotalDuration != null) job.TotalDuration = p.TotalDuration;
        if (p.Status == BatchSubtitleStatus.RunningASR) { ActiveJob = job; job.IsIndeterminateProgress = job.TotalDuration is not { Ticks: > 0 }; }
        else
        {
            job.IsIndeterminateProgress = false;
            if (IsTerminal(p.Status)) job.Progress = 1;
        }
        if (!string.IsNullOrWhiteSpace(p.AsrSegmentText))
        { ActiveJob = job; job.Transcript.Add(p.AsrSegmentText.Trim()); while (job.Transcript.Count > 200) job.Transcript.RemoveAt(0); }
        Tick();
    }
    public void Tick()
    {
        foreach (var job in runScope.Where(j => j.Status == BatchSubtitleStatus.RunningASR && j.StartedAt != null))
        {
            job.Elapsed = DateTimeOffset.Now - job.StartedAt!.Value;
            if (job.TotalDuration is { Ticks: > 0 } total && job.ProcessedTime is { TotalSeconds: > 0 } processed)
            {
                job.Progress = Math.Clamp(processed.TotalSeconds / total.TotalSeconds, 0, 1);
                if (job.Elapsed.Value.TotalSeconds >= 1)
                {
                    double speed = processed.TotalSeconds / job.Elapsed.Value.TotalSeconds;
                    job.Throughput = $"x{speed:0.0} realtime";
                    double remaining = Math.Max(0, total.TotalSeconds - processed.TotalSeconds) / speed;
                    job.Eta = double.IsFinite(remaining) && remaining < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(remaining) : null;
                }
            }
        }
        Refresh();
    }
    static bool IsTerminal(BatchSubtitleStatus status) => status is BatchSubtitleStatus.Completed or BatchSubtitleStatus.Failed or BatchSubtitleStatus.Canceled or BatchSubtitleStatus.Skipped;
    void OnJobChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(BatchSubtitleJob.Include)) Refresh(); }
    void Refresh()
    {
        int done = runScope.Count(j => IsTerminal(j.Status));
        OverallProgress = runScope.Count == 0 ? 0 : Math.Clamp(runScope.Sum(j => IsTerminal(j.Status) ? 1 : j.Status == BatchSubtitleStatus.RunningASR ? j.Progress : j.Status == BatchSubtitleStatus.Pending ? 0 : 0.95) / runScope.Count, 0, 1);
        double seconds = ActiveJob?.Eta?.TotalSeconds ?? 0;
        var finished = runScope.Where(j => j.StartedAt != null && j.CompletedAt != null).Select(j => (j.CompletedAt!.Value - j.StartedAt!.Value).TotalSeconds).Where(s => s > 0).ToArray();
        if (finished.Length > 0) seconds += finished.Average() * runScope.Count(j => j.Status == BatchSubtitleStatus.Pending);
        EtaText = seconds > 0 && double.IsFinite(seconds) && seconds < TimeSpan.MaxValue.TotalSeconds ? $"~{TimeSpan.FromSeconds(seconds):g} left" : "";
        Summary = $"{(WatchFolder ? "Watching · " : "")}{(IsRunningOnCpu ? "ASR on CPU · " : "")}{Jobs.Count} files ({Jobs.Count(j => j.Include)} selected) · {Jobs.Count(j => j.Status == BatchSubtitleStatus.Completed)} completed · {Jobs.Count(j => j.Status == BatchSubtitleStatus.Failed)} failed · {Jobs.Count(j => j.Status == BatchSubtitleStatus.Skipped)} skipped · {Jobs.Count(j => j.Status == BatchSubtitleStatus.Canceled)} canceled";
        if (IsRunning) Summary += $" · {done}/{runScope.Count} · {OverallProgress:P0} {EtaText}";
        Raise(nameof(IsIdle)); Raise(nameof(KeepAlive)); Raise(nameof(CanScan)); Raise(nameof(CanStart)); Raise(nameof(CanCancel)); Raise(nameof(CanRetryFailed)); Raise(nameof(IsRunningOnCpu)); Changed?.Invoke();
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task shared;
        lock (teardownGate)
        {
            if (teardownTask == null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                teardownTask = completion.Task;
            }
            shared = teardownTask;
        }
        // Publish the shared task before touching events or awaiting native work: a reentrant or
        // simultaneous Quit must observe exactly the same complete teardown, including the watcher.
        if (completion != null) _ = CompleteTeardownAsync(completion);
        return new ValueTask(shared);
    }
    async Task CompleteTeardownAsync(TaskCompletionSource completion)
    {
        try { await TeardownAsync(); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }
    async Task TeardownAsync()
    {
        disposed = true; IsOpen = false; WatchFolder = false; activeWatchSession = 0;
        watcher.FileReady -= OnFileReady; watcher.Error -= OnWatchError; watcher.Dispose();
        Cancel(); scanCancellation?.Cancel(); probeCancellation?.Cancel();
        await Task.WhenAll(DrainAsync(), watcher.DisposeAsync().AsTask());
        scanCancellation?.Dispose(); scanCancellation = null;
        probeCancellation?.Dispose(); probeCancellation = null;
        foreach (var job in Jobs) job.PropertyChanged -= OnJobChanged;
        Refresh();
    }
    sealed class CallbackProgress(Action<BatchSubtitleProgress> callback) : IProgress<BatchSubtitleProgress>
    { public void Report(BatchSubtitleProgress value) => callback(value); }
}
