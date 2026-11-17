using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using System.Collections.Concurrent;
using System.Text.Json;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Batch;

namespace LLPlayer.ControlHost;

internal sealed partial class ControlHostOperations
{
    private readonly SemaphoreSlim batchAdmission = new(1, 1);
    private ActiveBatch? activeBatch;
    private readonly ConcurrentDictionary<ControlBatchVoiceReference, BatchVoiceInput> liveBatchVoices = new();
    private Guid watchEpoch;
    private ControlScope? watchScope;

    private void RegisterBatch()
    {
        runtime.Dispatch(() => runtime.Batch.AdmitWatchedFiles = EnqueueWatchedFiles);
        Read<BatchReadRequest, BatchStatus>("batch.jobs", "batch", "Read batch files and audio choices.",
            (_, args, _) => Task.FromResult(BatchRead(batch => SnapshotBatch(batch, args))));
        Read<BatchReadRequest, BatchStatus>("batch.status", "batch", "Read current batch progress and results.",
            (_, args, _) => Task.FromResult(BatchRead(batch => SnapshotBatch(batch, args))));
        Job<BatchScanRequest>("batch.scan", "batch", "Scan the requested local folder for batch processing.", ScanBatchAsync);
        Job<BatchSelectionRequest>("batch.select_audio", "batch", "Select the audio stream for a queued file.",
            (context, args, ct) => ChangeBatchItem(context, args.MediaPath, entry =>
            {
                if (args.StreamIndex is { } index && (index < 0 || !entry.AudioTracks.Any(track => track.StreamIndex == index)))
                    throw new ControlJobException("stale_track", "The selected audio track is no longer available.");
                entry.AudioStreamIndexOverride = args.StreamIndex;
            }, ct));
        Job<BatchIncludeRequest>("batch.set_included", "batch", "Set whether a queued file participates in the batch.",
            (context, args, ct) => ChangeBatchItem(context, args.MediaPath, entry => entry.Include = args.Include, ct));
        RegisterBatchRun<BatchStartRequest>("batch.start", "Run the selected files using the existing Russian batch workflow.",
            (batch, _) => batch.Jobs.Where(file => file.Include), forceOverwrite: false);
        RegisterBatchRun<BatchRetryRequest>("batch.retry", "Retry one or all failed batch files.",
            (batch, args) => batch.Jobs.Where(file => file.Status == BatchSubtitleStatus.Failed &&
                (args.MediaPath == null || BatchWorkflow.PathComparer.Equals(file.MediaPath, Absolute(args.MediaPath)))),
            forceOverwrite: true);
        Job<BatchCancelRequest>("batch.cancel", "batch", "Cancel the selected batch and await its own result.", CancelBatchAsync);
        Read<BatchResumeRequest, ControlResult>("batch.resume", "batch", "Resume a batch and optionally reattach its live voice choices.",
            ResumeBatchAsync, mutates: true);
        Read<RuntimeEmpty, WatchStatus>("watch.get", "batch", "Read folder watcher state.",
            (_, _, _) => Task.FromResult(BatchRead(SnapshotWatch)));
        Job<BatchScanRequest>("watch.start", "batch", "Watch the local folder for stable media files.",
            (context, args, _) =>
            {
                context.RequireOriginalHost();
                return Task.FromResult(Done(BatchRead(batch =>
                {
                    BatchSettings candidate = CaptureBatchSettings(args);
                    candidate.WatchFolder = true;
                    watchEpoch = Guid.NewGuid();
                    watchScope = context.Request.Scope with { SessionId = null };
                    batch.ApplySettings(candidate);
                    if (!batch.IsOpen) batch.Open();
                    if (!batch.WatchFolder)
                        throw new ControlJobException("watch_unavailable", batch.Message);
                    return SnapshotWatch(batch);
                })));
            });
        Job<RuntimeEmpty>("watch.stop", "batch", "Stop watching without stopping the current file.",
            (context, _, _) =>
            {
                context.RequireOriginalHost();
                return Task.FromResult(Done(BatchRead(batch =>
                {
                    watchScope = null;
                    batch.Closed();
                    return SnapshotWatch(batch);
                })));
            });
        RegisterBatchRun<WatchedBatchRequest>("watch.process", "Process a ready file group from the active watcher.",
            SelectWatched, forceOverwrite: false, args => args.Epoch);
    }

    private void RegisterBatchRun<TArgs>(string id, string description,
        Func<BatchWorkflow, TArgs, IEnumerable<BatchSubtitleJob>> select, bool forceOverwrite, Func<TArgs, Guid?>? getEpoch = null)
        where TArgs : class
    {
        handlers.Add(id, RunBatchAsync);
        catalog.Add<TArgs, ControlResult>(new(id, "batch", description, true, ControlOperationCompletion.Job),
            (invocation, args, ct) => EnqueueBatchAsync(invocation.Request, args, select, forceOverwrite, getEpoch?.Invoke(args), ct));
    }

    private async Task<ControlResult> EnqueueBatchAsync<TArgs>(ControlRequest request, TArgs args,
        Func<BatchWorkflow, TArgs, IEnumerable<BatchSubtitleJob>> select, bool overwrite, Guid? epoch, CancellationToken ct)
        where TArgs : class
    {
        BatchVoiceInput? live = args switch
        {
            BatchStartRequest start => start.VoiceAssignments,
            BatchRetryRequest retry => retry.VoiceAssignments,
            _ => null,
        };
        if (live == null)
            return await jobs.EnqueueWithCheckpointAsync(Normalize(request, args),
                () => PrepareBatch(args, select, overwrite, epoch), ct);

        live = live with { MediaPath = Absolute(live.MediaPath) };
        var reference = new ControlBatchVoiceReference(request.RequestId, VoiceFingerprint(live), live.MediaPath);
        object persisted = args is BatchRetryRequest retryArgs
            ? new { retryArgs.MediaPath, voiceFingerprint = reference.Fingerprint }
            : new { voiceFingerprint = reference.Fingerprint };
        bool captured = false;
        try
        {
            var result = await jobs.EnqueueWithCheckpointAsync(Normalize(request, persisted), () =>
            {
                var checkpoint = PrepareBatch(args, select, overwrite, epoch, reference, live);
                captured = true;
                return checkpoint;
            }, ct);
            if (result.Error != null && captured) liveBatchVoices.TryRemove(reference, out _);
            return result;
        }
        catch
        {
            if (captured) liveBatchVoices.TryRemove(reference, out _);
            throw;
        }
    }

    private IEnumerable<BatchSubtitleJob> SelectWatched(BatchWorkflow batch, WatchedBatchRequest args)
    {
        if (!batch.WatchFolder || args.Epoch != watchEpoch || watchScope == null)
            throw new ControlException(new("watch_unavailable", "The watcher changed before the queued files started."));
        return batch.SelectWatched(args.Paths);
    }

    private JsonElement PrepareBatch<TArgs>(TArgs args, Func<BatchWorkflow, TArgs, IEnumerable<BatchSubtitleJob>> select,
        bool forceOverwrite, Guid? epoch, ControlBatchVoiceReference? voiceReference = null, BatchVoiceInput? live = null)
        => BatchRead(batch =>
        {
            var saved = settings.CaptureWorkflowSettings();
            BatchSettings options = settings.CaptureBatch();
            options.LastFolder = batch.Settings.LastFolder;
            options.Recursive = batch.Settings.Recursive;
            options.WatchFolder = batch.WatchFolder;
            options.OverwriteExisting |= forceOverwrite;
            var paths = select(batch, args).Select(row =>
            {
                FileInfo source = new(row.MediaPath);
                bool present = source.Exists;
                return new ControlBatchRow(row.MediaPath, row.AudioStreamIndexOverride, row.OutputPath,
                    options.GenerateDubbing
                        ? DubbingOutputPathBuilder.BuildRussianDubPath(row.MediaPath, saved.Config.Subtitles.DubbingConfig.OutputFormat)
                        : null, present ? source.Length : null, present ? source.LastWriteTimeUtc.Ticks : null);
            }).ToArray();
            if (paths.Length == 0)
                throw new ControlException(new("batch_unavailable", "There are no eligible files in the batch."));
            if (paths.Select(row => row.SubtitlePath).Distinct(BatchWorkflow.PathComparer).Count() != paths.Length)
                throw new ControlException(new("output_conflict", "Two selected media files have the same subtitle output path."));
            ControlBatchVoiceReference? voice = options.GenerateDubbing && live != null &&
                paths.Any(row => BatchWorkflow.PathComparer.Equals(row.MediaPath, live.MediaPath)) ? voiceReference : null;
            if (voice is not null) liveBatchVoices[voice] = live!;
            return ControlProtocol.Encode(new ControlBatchCheckpoint(options, paths, saved.RecognitionIdentity,
                saved.TranslationIdentity, saved.DubbingIdentity, settings.CapturePlayback().SubtitleExportUtf8Bom, epoch, voice));
        });

    private string VoiceFingerprint(BatchVoiceInput input) =>
        Convert.ToHexString(jobs.FingerprintSettings(JsonSerializer.SerializeToUtf8Bytes(input, ControlProtocol.Json)));

    private async Task<ControlResult> ResumeBatchAsync(ControlRequest request, BatchResumeRequest args, CancellationToken ct)
    {
        ControlJob? state = await jobs.GetAsync(request.Scope, args.JobId, ct);
        if (state?.OperationId is not ("batch.start" or "batch.retry" or "watch.process"))
            return ControlResult.Fail("batch_not_found", "The requested batch job is unavailable.");
        ControlResult stored = await jobs.ReadCheckpointAsync(request.Scope, args.JobId, ct);
        var checkpoint = stored.Value?.Deserialize<ControlBatchCheckpoint>(ControlProtocol.Json);
        if (checkpoint == null)
            return ControlResult.Fail("batch_snapshot_missing", "The original batch selection is unavailable.");
        if (checkpoint.LiveVoices is { } expected && args.VoiceAssignments is { } input)
        {
            input = input with { MediaPath = Absolute(input.MediaPath) };
            if (!string.Equals(expected.Fingerprint, VoiceFingerprint(input), StringComparison.Ordinal))
                return ControlResult.Fail("voice_snapshot_changed", "The supplied voice choices differ from the original batch.");
            liveBatchVoices[expected] = input;
        }
        var action = new ControlJobAction(args.JobId, request.Scope);
        return state.Status is ControlJobStatus.Failed or ControlJobStatus.Cancelled
            ? await jobs.RetryAsync(action, ct) : await jobs.ResumeAsync(action, ct);
    }

    private IDubbingVoiceAssignmentProvider? GetBatchVoiceProvider(ControlBatchCheckpoint checkpoint, bool persist)
    {
        if (checkpoint.LiveVoices is not { } reference) return null;
        if (!liveBatchVoices.TryGetValue(reference, out var live))
            throw new ControlJobException("voice_snapshot_unavailable", "Resubmit the original live voice choices to resume this batch.",
                ControlJobStatus.Interrupted);
        var cues = live.Cues.Select(cue => new SubtitleData
        {
            StartTime = TimeSpan.FromTicks(cue.StartTicks),
            EndTime = TimeSpan.FromTicks(cue.EndTicks),
            Text = cue.Text,
            AssignedVoiceId = cue.VoiceId,
        }).ToArray();
        return new DubbingBatchVoiceAssignmentProvider(live.MediaPath, cues,
            cues.Where(cue => string.IsNullOrWhiteSpace(cue.AssignedVoiceId)).ToArray(), persist);
    }

    private void EnqueueWatchedFiles(IReadOnlyList<string> paths)
    {
        if (watchScope is not { } scope) return;
        var args = new WatchedBatchRequest(watchEpoch, paths.ToArray());
        var request = new ControlRequest(Guid.NewGuid(), "watch.process", scope, null, ControlProtocol.Encode(args));
        JsonElement checkpoint = PrepareBatch(args, SelectWatched, false, args.Epoch);
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(async () =>
            {
                try
                {
                    var queued = await jobs.EnqueueWithCheckpointAsync(request, () => checkpoint, CancellationToken.None);
                    if (queued.Error != null)
                        runtime.Dispatch(() => runtime.Batch.SetMessage("A ready watched file could not be scheduled."));
                }
                catch (Exception)
                {
                    // Host shutdown can close the dispatcher while the watcher settles.
                    System.Diagnostics.Trace.TraceError("A ready watched file could not be scheduled.");
                }
            });
    }

    private async Task<ControlJobOutcome> ChangeBatchItem(ControlJobContext context, string mediaPath,
        Action<BatchSubtitleJob> change, CancellationToken ct)
    {
        context.RequireOriginalHost();
        await batchAdmission.WaitAsync(ct);
        try { return Done(BatchRead(batch =>
        {
            if (!batch.IsIdle) throw new ControlJobException("batch_busy", "Wait for the current batch operation.");
            BatchSubtitleJob entry = FindBatchItem(batch, mediaPath);
            change(entry);
            return SnapshotBatchItem(entry);
        })); }
        finally { batchAdmission.Release(); }
    }

    private async Task<ControlJobOutcome> ScanBatchAsync(ControlJobContext context, BatchScanRequest args, CancellationToken ct)
    {
        await batchAdmission.WaitAsync(ct);
        ActiveBatch owner = new(context.JobId, context.Request.Scope, ct);
        try
        {
            Task scan = BatchRead(batch =>
            {
                if (!batch.IsIdle) throw new ControlJobException("batch_busy", "Wait for the current batch operation.");
                activeBatch = owner;
                var saved = context.Checkpoint?.Deserialize<BatchSettings>(ControlProtocol.Json);
                BatchSettings candidate = saved ?? CaptureBatchSettings(args);
                if (saved == null) context.SaveCheckpoint(ControlProtocol.Encode(candidate));
                candidate.WatchFolder = batch.WatchFolder;
                batch.ApplySettings(candidate);
                if (!batch.CanScan) throw new ControlJobException("folder_unavailable", "The requested batch folder is unavailable.");
                return batch.ScanAsync();
            });
            using var registration = ct.Register(() => CancelOwnedWork(owner));
            await scan;
            ct.ThrowIfCancellationRequested();
            BatchStatus result = BatchRead(batch => SnapshotBatch(batch));
            return string.IsNullOrWhiteSpace(result.Message) ? Done(result)
                : new(ControlJobStatus.Failed, ControlProtocol.Encode(result), new("batch_scan_failed", result.Message));
        }
        finally { CompleteBatchOwner(owner); }
    }

    private async Task<ControlJobOutcome> RunBatchAsync(ControlJobContext context, CancellationToken ct)
    {
        ControlBatchCheckpoint checkpoint = context.Checkpoint?.Deserialize<ControlBatchCheckpoint>(ControlProtocol.Json)
            ?? throw new ControlJobException("batch_snapshot_missing", "The original batch selection is unavailable.",
                ControlJobStatus.Interrupted);
        await batchAdmission.WaitAsync(ct);
        ActiveBatch owner = new(context.JobId, context.Request.Scope, ct);
        var previousStreamSelections = new Dictionary<BatchSubtitleJob, int?>();
        var rows = new BatchSubtitleJob[checkpoint.Rows.Length];
        owner.Rows = rows;

        void SaveFinishedRows()
        {
            bool changed = false;
            for (int index = 0; index < rows.Length; index++)
            {
                BatchSubtitleJob row = rows[index];
                if (checkpoint.Rows[index].Receipt != null ||
                    row.Status is not (BatchSubtitleStatus.Completed or BatchSubtitleStatus.Skipped)) continue;
                checkpoint.Rows[index] = checkpoint.Rows[index] with { Receipt = new(row.Status, row.Error,
                    row.SubtitleCount, row.StartedAt, row.CompletedAt) };
                changed = true;
            }
            if (changed) context.SaveCheckpoint(ControlProtocol.Encode(checkpoint));
        }

        void Report()
        {
            SaveFinishedRows();
            context.Report("batch", Math.Clamp(runtime.Batch.OverallProgress, 0, 1));
        }

        try
        {
            BatchRead(batch =>
            {
                if (!batch.IsIdle) throw new ControlJobException("batch_busy", "Wait for the current batch operation.");
                if (checkpoint.WatchEpoch is { } epoch && !context.IsRecovery &&
                    (!batch.WatchFolder || watchEpoch != epoch || watchScope == null))
                    throw new ControlJobException("watch_unavailable", "The watcher changed before the queued files started.",
                        ControlJobStatus.Cancelled);
                activeBatch = owner;
                return true;
            });
            var saved = settings.CaptureWorkflowSettings();
            if (saved.RecognitionIdentity != checkpoint.RecognitionIdentity ||
                saved.TranslationIdentity != checkpoint.TranslationIdentity ||
                (checkpoint.Settings.GenerateDubbing && saved.DubbingIdentity != checkpoint.DubbingIdentity))
                throw new ControlJobException("input_changed", "Batch engine settings changed after submission.",
                    ControlJobStatus.Interrupted);
            context.VerifyIdentity("batch_recognition", saved.RecognitionIdentity);
            context.VerifyIdentity("batch_translation", saved.TranslationIdentity);
            var files = new ControlBatchFiles(context, checkpoint);
            var recovered = new bool[rows.Length];
            var missing = new bool[rows.Length];
            for (int index = 0; index < rows.Length; index++)
            {
                context.Report("checking_inputs", (double)index / rows.Length);
                if (!context.IsRecovery && !File.Exists(checkpoint.Rows[index].MediaPath))
                    missing[index] = recovered[index] = true;
                else
                    recovered[index] = await files.RecoverRowAsync(checkpoint.Rows[index], ct);
            }
            var voiceProvider = checkpoint.LiveVoices is { } live && checkpoint.Rows
                .Where((_, index) => !recovered[index])
                .Any(row => BatchWorkflow.PathComparer.Equals(row.MediaPath, live.MediaPath))
                ? GetBatchVoiceProvider(checkpoint, saved.Config.Subtitles.PersistPerLineVoices) : null;

            Task run = BatchRead(batch =>
            {
                var fileRows = batch.Jobs.ToDictionary(row => row.MediaPath, BatchWorkflow.PathComparer);
                for (int index = 0; index < rows.Length; index++)
                {
                    ControlBatchRow file = checkpoint.Rows[index];
                    if (!fileRows.TryGetValue(file.MediaPath, out var row))
                    {
                        row = new BatchSubtitleJob(file.MediaPath, checkpoint.Settings.LastFolder);
                        batch.Jobs.Add(row);
                    }
                    previousStreamSelections[row] = row.AudioStreamIndexOverride;
                    row.AudioStreamIndexOverride = file.StreamIndex;
                    rows[index] = row;

                    if (recovered[index])
                    {
                        var receipt = file.Receipt ?? (missing[index]
                            ? new ControlBatchReceipt(BatchSubtitleStatus.Failed, "The selected media file is unavailable.", 0,
                                null, DateTimeOffset.Now)
                            : new ControlBatchReceipt(BatchSubtitleStatus.Completed, null, DubbingSrtReader.ParseFile(file.SubtitlePath).Count,
                                null, DateTimeOffset.Now));
                        row.Status = receipt.Status;
                        row.Error = receipt.Error;
                        row.SubtitleCount = receipt.SubtitleCount;
                        row.StartedAt = receipt.StartedAt;
                        row.CompletedAt = receipt.CompletedAt;
                        row.Progress = 1;
                    }
                }
                var pending = rows.Where((_, index) => !recovered[index]).ToArray();
                if (pending.Length == 0) return Task.CompletedTask;
                batch.SetMessage("");
                var factory = runtime.CreateOwnedBatchFactory(saved.Config, checkpoint.Utf8Bom, files.Outputs, voiceProvider);
                Task work = batch.StartOwnedAsync(pending, checkpoint.Settings, factory);
                batch.Changed += Report;
                return work;
            });
            using var registration = ct.Register(() => CancelOwnedWork(owner));
            await run;
            EnsureBatchDrained();
            ct.ThrowIfCancellationRequested();
            var (result, failed) = BatchRead(batch =>
            {
                SaveFinishedRows();
                return (SnapshotBatch(batch, selected: rows),
                    rows.Any(row => row.Status == BatchSubtitleStatus.Failed));
            });
            if (failed)
                return new(ControlJobStatus.Failed, ControlProtocol.Encode(result), new("batch_file_failed", "At least one batch file failed."));
            if (checkpoint.LiveVoices is { } completedVoices)
                liveBatchVoices.TryRemove(completedVoices, out _);
            return Done(result);
        }
        catch (EngineProcessDrainException)
        {
            owner.PendingCleanup = true;
            throw;
        }
        finally { CompleteBatchOwner(owner, Report, previousStreamSelections); }
    }

    private async Task<ControlJobOutcome> CancelBatchAsync(ControlJobContext context, BatchCancelRequest args, CancellationToken ct)
    {
        var owner = BatchRead(_ => activeBatch);
        if (owner == null || (args.JobId.HasValue && owner.JobId != args.JobId))
        {
            if (args.JobId.HasValue)
                throw new ControlJobException("batch_not_active", "The requested batch is no longer active.");
            return Done(BatchRead(batch => SnapshotBatch(batch, selected: [])));
        }
        var cancelled = await jobs.CancelAsync(new(owner.JobId, owner.Scope), ct);
        if (cancelled.Error != null)
            return new(ControlJobStatus.Failed, Error: cancelled.Error);
        BatchStatus result = await owner.Completion.Task.WaitAsync(ct);
        return owner.PendingCleanup
            ? new(ControlJobStatus.Failed, ControlProtocol.Encode(result), new("cleanup_pending", "The batch engine has not confirmed shutdown."))
            : Done(result);
    }

    private void CancelOwnedWork(ActiveBatch owner) => runtime.Dispatch(() =>
    {
        if (ReferenceEquals(activeBatch, owner)) runtime.Batch.CancelBatchOnly();
    });

    private void CompleteBatchOwner(ActiveBatch owner, Action? report = null,
        IReadOnlyDictionary<BatchSubtitleJob, int?>? previousStreamSelections = null)
    {
        try
        {
            runtime.Dispatch(() =>
            {
                var batch = runtime.Batch;
                if (report != null) batch.Changed -= report;
                var selected = owner.Rows?.OfType<BatchSubtitleJob>().ToArray();
                var outcome = SnapshotBatch(batch, selected: selected);
                owner.Completion.TrySetResult(outcome with
                {
                    LastRunCanceled = outcome.LastRunCanceled || owner.Cancellation.IsCancellationRequested,
                });
                if (previousStreamSelections != null)
                    foreach (var (row, prior) in previousStreamSelections) row.AudioStreamIndexOverride = prior;
                if (ReferenceEquals(activeBatch, owner)) activeBatch = null;
            });
        }
        finally { batchAdmission.Release(); }
    }

    private void EnsureBatchDrained()
    {
        if (runtime.Gpu.PendingDrain != "Batch subtitles") return;
        var failure = new IOException("The batch engine has not confirmed process shutdown.");
        throw new EngineProcessDrainException(failure.Message, failure,
            async () => { await runtime.Gpu.RetryDrainAsync("Batch subtitles"); });
    }

    private BatchSettings CaptureBatchSettings(BatchScanRequest args)
    {
        BatchSettings candidate = settings.CaptureBatch();
        candidate.LastFolder = Absolute(args.Folder ?? candidate.LastFolder);
        if (args.Recursive.HasValue) candidate.Recursive = args.Recursive.Value;
        return candidate;
    }

    private T BatchRead<T>(Func<BatchWorkflow, T> get)
    {
        T value = default!;
        runtime.Dispatch(() => value = get(runtime.Batch));
        return value;
    }

    private static BatchSubtitleJob FindBatchItem(BatchWorkflow batch, string mediaPath)
    {
        string absolute = Absolute(mediaPath);
        return batch.Jobs.FirstOrDefault(file => BatchWorkflow.PathComparer.Equals(file.MediaPath, absolute))
            ?? throw new ControlJobException("batch_file_missing", "Scan the file's folder before editing the batch queue.");
    }

    private BatchStatus SnapshotBatch(BatchWorkflow batch, BatchReadRequest? paging = null,
        IReadOnlyList<BatchSubtitleJob>? selected = null)
    {
        int offset = paging?.Offset ?? 0;
        int limit = paging?.Limit ?? 200;
        if (offset < 0 || limit is < 1 or > 500)
            throw new ControlException(new("invalid_cursor", "Supply a nonnegative offset and a limit from 1 to 500."));
        var files = (selected ?? batch.Jobs).Skip(offset).Take(selected != null ? selected.Count : limit)
            .Select(SnapshotBatchItem).ToArray();
        int? next = selected == null && (long)offset + files.Length < batch.Jobs.Count ? offset + files.Length : null;
        bool cancelled = selected?.Any(row => row.Status == BatchSubtitleStatus.Canceled) ?? batch.LastRunCanceled;
        double progress = selected is { Count: > 0 } ? selected.Average(row => row.Progress) : batch.OverallProgress;
        return new(batch.IsRunning, batch.IsScanning, cancelled, progress, batch.Message, files, next,
            batch.ActiveJob?.MediaPath, batch.Summary, batch.EtaText, batch.IsRunningOnCpu, batch.CanCancel,
            batch.CanRetryFailed, runtime.BatchActivityStatus);
    }

    private static BatchItem SnapshotBatchItem(BatchSubtitleJob job) => new(job.MediaPath, job.OutputPath,
        job.Include, job.AudioStreamIndexOverride, job.Status, job.Error, job.SubtitleCount, job.Progress,
        job.AudioTracks.Where(track => track.StreamIndex >= 0)
            .Select(track => new BatchTrack(track.StreamIndex, track.Language.ISO6391, track.Title, track.Display,
                track.Codec, track.Channels, track.BitRate)).ToArray(),
        job.StartedAt, job.CompletedAt, job.Elapsed, job.Eta, job.ProcessedTime, job.TotalDuration,
        job.IsIndeterminateProgress, job.Throughput, job.Transcript.ToArray());

    private static WatchStatus SnapshotWatch(BatchWorkflow batch) => new(batch.WatchFolder, batch.Settings.LastFolder,
        batch.Settings.Recursive, batch.Message);

    internal sealed record BatchReadRequest(int Offset = 0, int Limit = 200);
    internal sealed record BatchScanRequest(string? Folder = null, bool? Recursive = null);
    internal sealed record BatchSelectionRequest(string MediaPath, int? StreamIndex = null);
    internal sealed record BatchIncludeRequest(string MediaPath, bool Include);
    internal sealed record BatchStartRequest(BatchVoiceInput? VoiceAssignments = null);
    internal sealed record BatchRetryRequest(string? MediaPath = null, BatchVoiceInput? VoiceAssignments = null);
    internal sealed record BatchCancelRequest(Guid? JobId = null);
    internal sealed record BatchResumeRequest(Guid JobId, BatchVoiceInput? VoiceAssignments = null);
    internal sealed record BatchVoiceInput(string MediaPath, BatchVoiceCue[] Cues);
    internal sealed record BatchVoiceCue(long StartTicks, long EndTicks, string? Text, string? VoiceId);
    internal sealed record WatchedBatchRequest(Guid Epoch, string[] Paths);
    internal sealed record BatchStatus(bool IsRunning, bool IsScanning, bool LastRunCanceled, double Progress,
        string Message, BatchItem[] Files, int? NextOffset, string? ActiveMediaPath, string Summary, string EtaText,
        bool IsRunningOnCpu, bool CanCancel, bool CanRetryFailed, string ActivityStatus);
    internal sealed record BatchItem(string MediaPath, string OutputPath, bool Include, int? StreamIndex,
        BatchSubtitleStatus Status, string? Error, int SubtitleCount, double Progress, BatchTrack[] AudioTracks,
        DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, TimeSpan? Elapsed, TimeSpan? Eta, TimeSpan? ProcessedTime,
        TimeSpan? TotalDuration, bool IsIndeterminateProgress, string Throughput, string[] Transcript);
    internal sealed record BatchTrack(int StreamIndex, string Language, string? Title, string Display,
        string? Codec, int Channels, long BitRate);
    internal sealed record WatchStatus(bool Active, string Folder, bool Recursive, string Message);

    private sealed class ActiveBatch(Guid jobId, ControlScope scope, CancellationToken cancellation)
    {
        public Guid JobId { get; } = jobId;
        public ControlScope Scope { get; } = scope;
        public CancellationToken Cancellation { get; } = cancellation;
        public bool PendingCleanup { get; set; }
        public BatchSubtitleJob[]? Rows { get; set; }
        public TaskCompletionSource<BatchStatus> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
