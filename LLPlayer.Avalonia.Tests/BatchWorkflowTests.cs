using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;
using LLPlayer.Core.Batch;

namespace LLPlayer.Avalonia.Tests;

public sealed class BatchWorkflowTests
{
    static readonly string FakeRoot = Path.Combine(Path.GetTempPath(), "llplayer-batch-fake-root");
    [Fact]
    public async Task Window_closed_after_scan_and_disposal_is_idempotent_and_does_not_cancel_retired_sources()
    {
        var workflow = Create(new Runs(), new Watcher()); workflow.Open();
        await workflow.ScanAsync(); await workflow.DrainAsync();
        await workflow.DisposeAsync();
        workflow.Closed(); workflow.Closed(); workflow.Cancel();
        Assert.False(workflow.KeepAlive); Assert.False(workflow.IsOpen); Assert.False(workflow.WatchFolder);
    }
    [Fact]
    public async Task Repoint_while_running_retires_old_auto_queue_but_keeps_old_rows_for_manual_start()
    {
        var runs = new Runs(); var watcher = new Watcher(); await using var workflow = Create(runs, watcher);
        workflow.Open(); long oldSession = watcher.Session;
        watcher.Ready("A1.mkv"); await runs.Started.Task;
        watcher.Ready("A2.mkv"); var oldQueued = workflow.Jobs.Single(job => job.FileName == "A2.mkv");
        var settings = workflow.Settings.Clone(); settings.LastFolder = FakeRoot + "-B"; workflow.ApplySettings(settings);
        watcher.Fail("retired A error", oldSession); watcher.Ready("retired-A.mkv", oldSession);
        watcher.Ready("B1.mkv");
        runs.Release(); await workflow.DrainAsync();
        Assert.Equal(2, runs.Scopes.Count);
        Assert.Equal("B1.mkv", Assert.Single(runs.Scopes[1]).FileName);
        Assert.Same(oldQueued, workflow.Jobs.Single(job => job.FileName == "A2.mkv"));
        Assert.Equal(BatchSubtitleStatus.Pending, oldQueued.Status);
        Assert.True(workflow.WatchFolder); Assert.DoesNotContain("retired A error", workflow.Message);
    }

    [Fact]
    public async Task Scope_change_retires_a_late_scan_snapshot_even_when_catalog_ignores_cancellation()
    {
        var catalog = new GatedCatalog { IgnoreCancellation = true };
        await using var workflow = new BatchWorkflow(new BatchSettings { LastFolder = FakeRoot }, new Runs(), new Watcher(), a => a(), catalog);
        Task scan = workflow.ScanAsync(); await catalog.Entered.Task;
        try
        {
            var settings = workflow.Settings.Clone(); settings.Recursive = !settings.Recursive; workflow.ApplySettings(settings);
            catalog.Release.TrySetResult(); await scan;
            Assert.Empty(workflow.Jobs); Assert.False(workflow.IsScanning);
            Assert.Contains("scope changed", workflow.Message);
        }
        finally { catalog.Release.TrySetResult(); }
    }

    [Fact]
    public async Task Concurrent_disposal_callers_both_wait_for_the_same_retired_watcher_drain()
    {
        var watcher = new Watcher { HoldDispose = true };
        var workflow = Create(new Runs(), watcher); workflow.Open();
        Task first = workflow.DisposeAsync().AsTask();
        try
        {
            await watcher.DisposeEntered.Task;
            Task second = workflow.DisposeAsync().AsTask();
            Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
            Assert.Equal(1, watcher.AsyncDisposeCalls);
            watcher.ReleaseDispose.TrySetResult(); await Task.WhenAll(first, second);
            Assert.False(workflow.KeepAlive);
        }
        finally { watcher.ReleaseDispose.TrySetResult(); await workflow.DisposeAsync(); }
    }
    [Fact]
    public async Task Scan_completion_preserves_ready_watch_job_identity_and_drains_only_watch_admission()
    {
        var scans = new GatedCatalog();
        var runs = new Runs(); var watcher = new Watcher();
        await using var workflow = new BatchWorkflow(new BatchSettings { LastFolder = FakeRoot, WatchFolder = true }, runs, watcher, a => a(), scans);
        workflow.Open();
        Task scan = workflow.ScanAsync();
        await scans.Entered.Task;
        watcher.Ready("fresh.mkv");
        var watched = Assert.Single(workflow.Jobs);
        watched.AudioStreamIndexOverride = 5;
        scans.Release.TrySetResult();
        await scan;
        await runs.Started.Task;
        Assert.Same(watched, workflow.Jobs.Single(j => Path.GetFileName(j.MediaPath) == "fresh.mkv"));
        Assert.Equal("fresh.mkv", Path.GetFileName(Assert.Single(runs.Scopes[0]).MediaPath));
        Assert.Equal(5, runs.Scopes[0][0].AudioStreamIndexOverride);
        Assert.Equal(BatchSubtitleStatus.Pending, workflow.Jobs.Single(j => Path.GetFileName(j.MediaPath) == "manual.mkv").Status);
        runs.Release(); await workflow.DrainAsync();
    }

    [Fact]
    public async Task Old_session_error_and_ready_callbacks_cannot_disable_or_populate_repointed_watch()
    {
        var watcher = new Watcher(); await using var workflow = Create(new Runs(), watcher);
        workflow.Open(); long oldSession = watcher.Session;
        var settings = workflow.Settings.Clone(); settings.Recursive = false; workflow.ApplySettings(settings);
        Assert.NotEqual(oldSession, watcher.Session);
        watcher.Fail("old error", oldSession); watcher.Ready("old.mkv", oldSession);
        Assert.True(workflow.WatchFolder); Assert.Empty(workflow.Jobs);
        Assert.DoesNotContain("old error", workflow.Message);
    }

    [Fact]
    public async Task One_ready_batch_is_naturally_ordered_before_first_run_is_admitted()
    {
        var runs = new Runs(); var watcher = new Watcher(); await using var workflow = Create(runs, watcher);
        workflow.Open(); watcher.ReadyBatch("lesson10.mkv", "lesson2.mkv", "lesson1.mkv");
        await runs.Started.Task;
        Assert.Equal(new[] { "lesson1.mkv", "lesson2.mkv", "lesson10.mkv" }, runs.Scopes[0].Select(j => Path.GetFileName(j.MediaPath)));
        runs.Release(); await workflow.DrainAsync();
    }
    [Fact]
    public async Task Watch_arrival_does_not_sweep_manual_backlog_and_drains_after_current_run()
    {
        var factory = new Runs();
        var watcher = new Watcher();
        await using var workflow = Create(factory, watcher);
        workflow.Jobs.Add(new BatchSubtitleJob("manual.mkv"));
        workflow.Open();
        watcher.Ready("first.mkv");
        await factory.Started.Task;
        Assert.Equal("first.mkv", Path.GetFileName(Assert.Single(factory.Scopes[0]).MediaPath));
        watcher.Ready("next.mkv");
        watcher.Ready("next.mkv");
        Assert.Equal(3, workflow.Jobs.Count);
        Assert.Single(factory.Scopes);
        factory.Release();
        await workflow.DrainAsync();
        Assert.Equal(2, factory.Scopes.Count);
        Assert.Equal("next.mkv", Path.GetFileName(Assert.Single(factory.Scopes[1]).MediaPath));
        Assert.Equal(BatchSubtitleStatus.Pending, workflow.Jobs[0].Status);
    }

    [Fact]
    public async Task Explicit_cancel_drops_watch_backlog_and_later_arrival_cannot_resurrect_it()
    {
        var factory = new Runs();
        var watcher = new Watcher();
        await using var workflow = Create(factory, watcher);
        workflow.Open();
        watcher.Ready("first.mkv");
        await factory.Started.Task;
        watcher.Ready("queued.mkv");
        workflow.Cancel();
        await workflow.DrainAsync();
        Assert.Single(factory.Scopes);
        watcher.Ready("new.mkv");
        await workflow.DrainAsync();
        Assert.Equal(2, factory.Scopes.Count);
        Assert.Equal("new.mkv", Path.GetFileName(Assert.Single(factory.Scopes[1]).MediaPath));
        Assert.Equal(BatchSubtitleStatus.Pending, workflow.Jobs.Single(j => Path.GetFileName(j.MediaPath) == "queued.mkv").Status);
    }

    [Fact]
    public async Task Watch_failure_stops_session_but_preserves_persisted_intent()
    {
        var factory = new Runs();
        var watcher = new Watcher();
        await using var workflow = Create(factory, watcher);
        workflow.Open();
        watcher.Fail("Folder disappeared");
        Assert.False(workflow.WatchFolder);
        Assert.True(workflow.Settings.WatchFolder);
        Assert.True(watcher.Stopped);
        Assert.Contains("Folder disappeared", workflow.Message);
        watcher.Ready("late.mkv");
        Assert.Empty(workflow.Jobs);
    }

    [Fact]
    public async Task Run_uses_detached_jobs_and_setting_snapshot_and_retry_forces_overwrite()
    {
        var factory = new Runs();
        var watcher = new Watcher();
        await using var workflow = Create(factory, watcher);
        var ui = new BatchSubtitleJob("file.mkv") { AudioStreamIndexOverride = 4 };
        workflow.Jobs.Add(ui);
        Task first = workflow.StartAsync();
        await factory.Started.Task;
        workflow.Settings.PreferRussianAudio = false;
        ui.AudioStreamIndexOverride = 9;
        Assert.True(factory.Options[0].PreferRussianAudio);
        Assert.Equal(4, factory.Scopes[0][0].AudioStreamIndexOverride);
        Assert.NotSame(ui, factory.Scopes[0][0]);
        factory.Release();
        await first;
        await workflow.RetryAsync(ui);
        Assert.True(factory.Options[1].OverwriteExisting);
        Assert.Equal(9, factory.Scopes[1][0].AudioStreamIndexOverride);
    }

    [Fact]
    public async Task Scan_keeps_existing_output_deselected_and_supports_dub_only_queue()
    {
        var catalog = new Catalog { HasTranslation = true };
        var settings = new BatchSettings { LastFolder = "root", GenerateDubbing = false };
        await using var workflow = new BatchWorkflow(settings, new Runs(), new Watcher(), action => action(), catalog);
        await workflow.ScanAsync();
        Assert.False(Assert.Single(workflow.Jobs).Include);
        Assert.Equal(BatchSubtitleStatus.Completed, workflow.Jobs[0].Status);
        settings.GenerateDubbing = true;
        await workflow.ScanAsync();
        Assert.True(Assert.Single(workflow.Jobs).Include);
        Assert.Equal(BatchSubtitleStatus.Pending, workflow.Jobs[0].Status);
    }

    [Fact]
    public async Task Close_cancels_and_drains_inflight_run_before_releasing_watch()
    {
        var factory = new Runs();
        var watcher = new Watcher();
        var workflow = Create(factory, watcher);
        workflow.Open();
        watcher.Ready("first.mkv");
        await factory.Started.Task;
        Assert.True(workflow.KeepAlive);
        await workflow.DisposeAsync();
        Assert.False(workflow.KeepAlive);
        Assert.True(factory.Disposed);
        Assert.True(watcher.Disposed);
    }

    static BatchWorkflow Create(Runs runs, Watcher watcher) => new(
        new BatchSettings { LastFolder = FakeRoot, WatchFolder = true }, runs, watcher, a => a(), new Catalog());

    sealed class Runs : IBatchRunFactory
    {
        readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<IReadOnlyList<BatchSubtitleJob>> Scopes { get; } = [];
        public List<BatchSettings> Options { get; } = [];
        public bool Disposed { get; private set; }
        public void Release() => release.TrySetResult();
        public IBatchRun Create(BatchSettings settings, IReadOnlyList<BatchSubtitleJob> jobs)
        {
            Scopes.Add(jobs); Options.Add(settings);
            return new Run(this, jobs, Scopes.Count == 1);
        }
        sealed class Run(Runs owner, IReadOnlyList<BatchSubtitleJob> jobs, bool wait) : IBatchRun
        {
            public bool IsRunningOnCpu => false;
            public async Task ProcessAsync(IProgress<BatchSubtitleProgress> progress, CancellationToken token)
            {
                owner.Started.TrySetResult();
                if (wait) await owner.release.Task.WaitAsync(token);
                foreach (var job in jobs)
                    progress.Report(new BatchSubtitleProgress(job, BatchSubtitleStatus.Completed, CompletedAt: DateTimeOffset.UtcNow));
            }
            public ValueTask DisposeAsync() { owner.Disposed = true; return ValueTask.CompletedTask; }
        }
    }
    sealed class Watcher : IBatchFolderWatcher
    {
        public event Action<BatchWatchReady>? FileReady;
        public event Action<BatchWatchError>? Error;
        public long Session { get; private set; }
        string folder = FakeRoot;
        public bool HoldDispose { get; init; }
        public int AsyncDisposeCalls { get; private set; }
        public TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public long Start(string path, bool recursive) { folder = path; Stopped = false; return ++Session; }
        public void Stop() => Stopped = true;
        public void Ready(string path, long? session = null) => FileReady?.Invoke(new(session ?? Session, [Path.Combine(folder, path)]));
        public void ReadyBatch(params string[] paths) => FileReady?.Invoke(new(Session, paths.Select(path => Path.Combine(folder, path)).ToArray()));
        public void Fail(string message, long? session = null) => Error?.Invoke(new(session ?? Session, message));
        public void Dispose() => Disposed = true;
        public async ValueTask DisposeAsync()
        {
            AsyncDisposeCalls++; Disposed = true; DisposeEntered.TrySetResult();
            if (HoldDispose) await ReleaseDispose.Task;
        }
    }
    sealed class Catalog : IBatchMediaCatalog
    {
        public bool HasTranslation { get; init; }
        public bool FolderExists(string path) => true;
        public bool TranslationExists(string path) => HasTranslation;
        public bool DubExists(string path) => false;
        public Task<IReadOnlyList<string>> ScanAsync(string folder, bool recursive, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(["file.mkv"]);
        public Task<IReadOnlyList<MediaAudioTrack>> ProbeAsync(string path, CancellationToken token) => Task.FromResult<IReadOnlyList<MediaAudioTrack>>([]);
    }
    sealed class GatedCatalog : IBatchMediaCatalog
    {
        public bool IgnoreCancellation { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FolderExists(string path) => true;
        public bool TranslationExists(string path) => false;
        public bool DubExists(string path) => false;
        public async Task<IReadOnlyList<string>> ScanAsync(string folder, bool recursive, CancellationToken token)
        { Entered.TrySetResult(); if (IgnoreCancellation) await Release.Task; else await Release.Task.WaitAsync(token); return [Path.Combine(FakeRoot, "manual.mkv")]; }
        public Task<IReadOnlyList<MediaAudioTrack>> ProbeAsync(string path, CancellationToken token) => Task.FromResult<IReadOnlyList<MediaAudioTrack>>([]);
    }
}
