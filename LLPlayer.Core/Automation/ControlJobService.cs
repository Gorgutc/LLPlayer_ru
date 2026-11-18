using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using FlyleafLib.MediaPlayer;
using LLPlayer.ControlApi;

namespace LLPlayer.Core.Automation;

public delegate Task<ControlJobOutcome> ControlJobHandler(ControlJobContext context, CancellationToken token);

public sealed record ControlJobOutcome(ControlJobStatus Status, JsonElement? Value = null, ControlError? Error = null);

public sealed record ControlTranslationUpdate(JsonElement? Checkpoint, ControlJobOutcome Outcome, bool NoChange = false);

public sealed record ControlJobChange(long Sequence, ControlJob Job);

/// <summary>Owns the durable job lifecycle after the client submitting a request has disconnected.</summary>
public sealed class ControlJobService : IControlJobService, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly ControlJobStore store;
    private readonly IReadOnlyDictionary<string, ControlJobHandler> handlers;
    private readonly Func<string, bool> requiresCleanResources;
    private readonly Func<Guid, string, Task<bool>>? retryResourceDrain;
    private readonly Dictionary<Guid, ActiveJob> active = [];
    private readonly Dictionary<Guid, PendingCleanup> cleanup = [];
    private readonly HashSet<Guid> recoveredCleanup = [];
    private TaskCompletionSource cleanupChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool accepting = true;
    private Task? shutdown;
    internal SemaphoreSlim FilePublication { get; } = new(1, 1);

    public ControlJobService(string automationDirectory, IReadOnlyDictionary<string, ControlJobHandler> handlers,
        Func<string, bool>? requiresCleanResources = null, Func<Guid, string, Task<bool>>? retryResourceDrain = null)
    {
        store = new(automationDirectory);
        this.handlers = handlers;
        this.requiresCleanResources = requiresCleanResources ?? (_ => true);
        this.retryResourceDrain = retryResourceDrain;
        Recover();
    }

    public bool HasPendingRecovery
    {
        get { lock (gate) return recoveredCleanup.Count != 0; }
    }

    /// <summary>Fingerprints settings for the same profile over restarts without disclosing the journal key.</summary>
    public byte[] FingerprintSettings(byte[] settingsBytes)
    {
        ArgumentNullException.ThrowIfNull(settingsBytes);
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, store.FingerprintKey);
        hash.AppendData("llplayer-settings/v1\0"u8);
        hash.AppendData(settingsBytes);
        return hash.GetHashAndReset();
    }

    public Task<ControlResult> EnqueueAsync(ControlRequest request, CancellationToken ct = default) =>
        EnqueueCore(request, null, ct);

    /// <summary>Persists the resolved operation input in the same admission transaction as the request.</summary>
    public Task<ControlResult> EnqueueWithCheckpointAsync(ControlRequest request, Func<JsonElement> capture,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return EnqueueCore(request, capture, ct);
    }

    private Task<ControlResult> EnqueueCore(ControlRequest request, Func<JsonElement>? capture, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.OperationId) ||
            string.IsNullOrWhiteSpace(request.Scope.ProfileId))
            return Task.FromResult(Error("invalid_arguments", "A request ID, operation and profile are required."));

        byte[] fingerprint;
        try { fingerprint = Fingerprint(request); }
        catch (JsonException)
        {
            return Task.FromResult(Error("invalid_arguments", "Operation arguments contain invalid or duplicate JSON properties."));
        }

        bool secret = request.OperationId.StartsWith("settings.credentials.", StringComparison.Ordinal);
        JsonElement? checkpoint = null;
        ControlResult? CheckRequest()
        {
            ct.ThrowIfCancellationRequested();
            if (store.GetByRequest(request.Scope.ProfileId, request.RequestId) is { } existing)
                return CryptographicOperations.FixedTimeEquals(existing.Fingerprint, fingerprint)
                    ? Result(existing.State)
                    : Error("request_conflict", "This request ID already belongs to a different operation.");
            if (!accepting)
                return Error("host_stopping", "The control host is stopping.", retryable: true);
            return null;
        }

        if (capture != null)
        {
            if (secret) return Task.FromResult(Error("invalid_arguments", "A credential operation cannot store a checkpoint."));
            lock (gate)
            {
                if (CheckRequest() is { } existing) return Task.FromResult(existing);
                if (!handlers.ContainsKey(request.OperationId))
                    return Task.FromResult(Error("unknown_operation", "Unknown job operation."));
            }
            // A dispatcher may be reporting on another job. Capture outside the journal lock.
            JsonElement resolved = capture();
            if (resolved.ValueKind != JsonValueKind.Object)
                return Task.FromResult(Error("invalid_arguments", "The resolved checkpoint must be a JSON object."));
            checkpoint = resolved.Clone();
        }

        lock (gate)
        {
            if (CheckRequest() is { } existing) return Task.FromResult(existing);
            if (!handlers.TryGetValue(request.OperationId, out ControlJobHandler? handler))
                return Task.FromResult(Error("unknown_operation", "Unknown job operation."));

            request = request with { Arguments = CloneArguments(request.Arguments) };
            var persistedRequest = secret ? request with { Arguments = JsonSerializer.SerializeToElement(new { }) } : request;
            var state = new ControlJob(Guid.NewGuid(), request.Scope, request.OperationId, ControlJobStatus.Accepted,
                Revision: 1, DateTimeOffset.UtcNow);
            var job = store.Admit(state, persistedRequest, fingerprint, secret, checkpoint);
            Start(job, request, handler, isRecovery: false, acknowledgeUnknownEffect: false);
            return Task.FromResult(Result(job.State));
        }
    }

    public Task<ControlJob?> GetAsync(ControlScope scope, Guid jobId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(store.Get(scope, jobId)?.State);
    }

    // Only recognition can establish a physical media origin for a detached subtitle document.
    public Task<(string Path, string Fingerprint, bool RequirePhysicalSource)?> ReadRecognitionSourceAsync(ControlScope scope, Guid jobId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            var job = store.Get(scope, jobId);
            (string Path, string Fingerprint, bool RequirePhysicalSource)? source = null;
            if (job is { State: { OperationId: "recognition.start",
                    Status: ControlJobStatus.Completed or ControlJobStatus.AwaitingTranslation } }
                && job.State.Scope == scope
                && job.Journal.Identities.TryGetValue(ControlJobContext.RecognitionSourcePathIdentity, out var physicalPath)
                && job.Request.Arguments.GetProperty("mediaPath").GetString() is { } path
                && job.Journal.Identities.TryGetValue("file:" + Path.GetFullPath(path), out var fingerprint))
                source = (physicalPath, fingerprint, job.Request.Arguments.TryGetProperty("requirePhysicalSource", out var guard)
                    && guard.ValueKind == JsonValueKind.True);
            return Task.FromResult(source);
        }
    }

    public Task<ControlJobPage> ListAsync(ControlScope scope, string? cursor = null, int limit = 50,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        long before = long.MaxValue;
        if (cursor != null && (!long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out before) || before <= 0))
            throw new ArgumentException("Invalid job page cursor.", nameof(cursor));
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "A job page contains between 1 and 200 items.");
        lock (gate)
        {
            var jobs = store.List(scope, before, limit + 1);
            string? next = jobs.Count > limit ? jobs[limit - 1].Ordinal.ToString(CultureInfo.InvariantCulture) : null;
            return Task.FromResult(new ControlJobPage(jobs.Take(limit).Select(job => job.State).ToArray(), next));
        }
    }

    public Task<ControlResult> CancelAsync(ControlJobAction action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            var job = store.Get(action.Scope, action.JobId);
            if (job is null)
                return Task.FromResult(NotFound());
            if (active.TryGetValue(action.JobId, out var worker) && job.State.Status != ControlJobStatus.Cancelling)
            {
                job = store.Save(job, job.State with { Status = ControlJobStatus.Cancelling, Stage = "cancelling" });
                worker.SignalCancellation();
            }
            else if (job.State.Status is ControlJobStatus.AwaitingTranslation or ControlJobStatus.Interrupted)
                job = store.Save(job, job.State with { Status = ControlJobStatus.Cancelled, Stage = "cancelled" });
            return Task.FromResult(Result(job.State));
        }
    }

    public Task<ControlJob[]> ListActiveAsync(ControlScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(store.ListActive(scope));
    }

    public async Task<ControlResult> RetryAsync(ControlJobAction action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Task<ControlResult>? drain;
        lock (gate)
        {
            var job = store.Get(action.Scope, action.JobId);
            if (job is null)
                return NotFound();
            if (job.State.Status != ControlJobStatus.AwaitingCleanup)
                return Restart(job, action, resume: false);
            if (!cleanup.TryGetValue(action.JobId, out PendingCleanup? pending))
            {
                if (!TryConfirmOldProcesses(job, out job))
                    return Result(job.State);
                job = ChangeAfterRecovery(job);
                recoveredCleanup.Remove(job.State.JobId);
                NotifyCleanupChanged();
                return Result(job.State);
            }
            drain = pending.Run ??= Task.Run(() => RetryDrainAsync(job, pending));
        }
        return await drain.WaitAsync(ct).ConfigureAwait(false);
    }

    public Task<ControlResult> ResumeAsync(ControlJobAction action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            var job = store.Get(action.Scope, action.JobId);
            return Task.FromResult(job is null ? NotFound() : Restart(job, action, resume: true));
        }
    }

    public Task<ControlResult> ReadAwaitingTranslationCheckpointAsync(ControlScope scope, Guid jobId,
        CancellationToken ct = default) => ReadCheckpointAsync(scope, jobId, awaitingTranslationOnly: true, ct);

    public Task<ControlResult> ReadCheckpointAsync(ControlScope scope, Guid jobId, CancellationToken ct = default) =>
        ReadCheckpointAsync(scope, jobId, awaitingTranslationOnly: false, ct);

    private Task<ControlResult> ReadCheckpointAsync(ControlScope scope, Guid jobId, bool awaitingTranslationOnly,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            var job = store.Get(scope, jobId);
            if (job is null)
                return Task.FromResult(NotFound());
            if (awaitingTranslationOnly && job.State.Status != ControlJobStatus.AwaitingTranslation)
                return Task.FromResult(Error("invalid_job_state", "This job is not awaiting translation."));
            return Task.FromResult(new ControlResult(jobId, job.State.Revision, job.Journal.Checkpoint));
        }
    }

    public Task<ControlJobChange[]> ReadChangesAsync(ControlScope scope, long afterSequence = 0, int limit = 50,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (afterSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit));
        lock (gate) return Task.FromResult(store.Changes(scope, afterSequence, limit));
    }

    public Task<ControlResult> UpdateAwaitingTranslationAsync(ControlScope scope, Guid jobId, long? expectedJobRevision,
        Func<JsonElement?, ControlTranslationUpdate> update, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            var job = store.Get(scope, jobId);
            if (job is null)
                return Task.FromResult(NotFound());
            if (job.State.Status != ControlJobStatus.AwaitingTranslation)
                return Task.FromResult(Error("invalid_job_state", "This job is not awaiting translation."));
            if (expectedJobRevision is long expected && expected != job.State.Revision)
                return Task.FromResult(Error("revision_conflict", "The job has changed since it was read.", retryable: true));
            var updated = update(job.Journal.Checkpoint);
            if (updated.Outcome.Error != null)
                return Task.FromResult(new ControlResult(jobId, job.State.Revision, Error: updated.Outcome.Error));
            if (updated.NoChange)
                return Task.FromResult(Result(job.State));
            var state = ApplyOutcome(job.State, updated.Outcome);
            if (state.Status is not (ControlJobStatus.AwaitingTranslation or ControlJobStatus.Completed))
                throw new InvalidOperationException("Invalid external translation transition.");
            var journal = job.Journal with { Checkpoint = updated.Checkpoint?.Clone() };
            return Task.FromResult(Result(store.Save(job, state, journal).State));
        }
    }

    internal void Update(Guid jobId, int attempt, Func<StoredControlJob, (ControlJob State, ControlJobJournal Journal)> update)
    {
        lock (gate)
        {
            var job = ReadOwned(jobId, attempt);
            var (state, journal) = update(job);
            if (state != job.State || journal != job.Journal)
                store.Save(job, state, journal);
        }
    }

    internal T Read<T>(Guid jobId, int attempt, Func<StoredControlJob, T> read)
    {
        lock (gate) return read(ReadOwned(jobId, attempt));
    }

    private StoredControlJob ReadOwned(Guid jobId, int attempt)
    {
        var job = store.Get(jobId);
        return job is not null && job.Journal.Attempt == attempt &&
            (active.ContainsKey(jobId) || cleanup.ContainsKey(jobId))
            ? job : throw new InvalidOperationException("The operation no longer owns this job.");
    }

    private ControlResult Restart(StoredControlJob job, ControlJobAction action, bool resume)
    {
        if (!accepting)
            return Error("host_stopping", "The control host is stopping.", retryable: true);
        if (job.State.Status != (resume ? ControlJobStatus.Interrupted : ControlJobStatus.Failed))
            return Error("invalid_job_state", resume ? "Only an interrupted job can be resumed." : "Only a failed job can be retried.");
        if (job.Secret)
            return Error("credential_replay_denied", "Submit a new credential operation explicitly.");
        if (job.Journal.DiscardOnRestart)
            return Error("non_recoverable", "This operation depended on state that no longer exists.");
        if (!action.AcknowledgeUnknownEffect && job.Journal.Effects.Values.Any(effect => effect is { Kind: "external", Completed: false }))
            return Error("external_effect_unknown", "An external action may already have occurred. Acknowledge the unknown effect to retry it.");
        if (!handlers.TryGetValue(job.Request.OperationId, out var handler))
            return Error("unknown_operation", "The job operation is no longer supported.");

        var journal = job.Journal with { Attempt = checked(job.Journal.Attempt + 1) };
        job = store.Save(job, job.State with { Status = ControlJobStatus.Accepted, Stage = null, Progress = null, Error = null }, journal);
        Start(job, job.Request, handler, isRecovery: true, action.AcknowledgeUnknownEffect);
        return Result(job.State);
    }

    private void Start(StoredControlJob job, ControlRequest request, ControlJobHandler handler, bool isRecovery,
        bool acknowledgeUnknownEffect)
    {
        var worker = new ActiveJob();
        active.Add(job.State.JobId, worker);
        var context = new ControlJobContext(this, job.State.JobId, job.Journal.Attempt, request, isRecovery,
            acknowledgeUnknownEffect);
        worker.Task = Task.Run(() => RunAsync(context, handler, worker));
    }

    private async Task RunAsync(ControlJobContext context, ControlJobHandler handler, ActiveJob worker)
    {
        ControlJobOutcome outcome;
        EngineProcessDrainException? drain = null;
        try
        {
            await WaitForRecoveredProcessesAsync(context, worker.Token).ConfigureAwait(false);
            worker.Token.ThrowIfCancellationRequested();
            context.Report("running", status: ControlJobStatus.Running);
            using var owned = EngineProcessOwnership.Enter(context);
            outcome = await handler(context, worker.Token).ConfigureAwait(false);
            if (outcome.Status is not (ControlJobStatus.Completed or ControlJobStatus.AwaitingTranslation or
                ControlJobStatus.Failed or ControlJobStatus.Cancelled or ControlJobStatus.Interrupted))
                throw new InvalidOperationException("Invalid handler completion status.");
        }
        catch (OperationCanceledException) when (worker.Token.IsCancellationRequested)
        {
            outcome = new(ControlJobStatus.Cancelled);
        }
        catch (ControlJobException error)
        {
            outcome = new(error.Status, Error: error.Error);
        }
        catch (ControlException error)
        {
            outcome = new(ControlJobStatus.Failed, Error: error.Error);
        }
        catch (EngineProcessDrainException error)
        {
            drain = error;
            outcome = worker.Token.IsCancellationRequested
                ? new(ControlJobStatus.Cancelled)
                : new(ControlJobStatus.Failed, Error: new("engine_failed", "The engine was stopped after the operation failed.", true));
        }
        catch (Exception)
        {
            outcome = new(ControlJobStatus.Failed, Error: new("operation_failed", "The operation failed.", true));
        }

        lock (gate)
        {
            try
            {
                var job = ReadOwned(context.JobId, context.Attempt);
                if (worker.Token.IsCancellationRequested && outcome.Status == ControlJobStatus.AwaitingTranslation)
                    outcome = new(ControlJobStatus.Cancelled);
                if (job.Secret)
                    outcome = outcome with { Value = null, Error = outcome.Error is null ? null :
                        new("credential_operation_failed", "The credential operation failed.", outcome.Error.Retryable) };
                if (drain != null)
                {
                    cleanup[job.State.JobId] = new(drain, outcome);
                    store.Save(job, SetCleanupPending(job.State));
                }
                else if (!TryConfirmOldProcesses(job, out job))
                {
                    store.Save(job, SetCleanupPending(job.State));
                    recoveredCleanup.Add(job.State.JobId);
                }
                else
                    store.Save(job, ApplyOutcome(job.State, outcome));
            }
            finally
            {
                active.Remove(context.JobId);
                worker.Dispose();
            }
        }
    }

    private async Task WaitForRecoveredProcessesAsync(ControlJobContext context, CancellationToken token)
    {
        if (!requiresCleanResources(context.Request.OperationId))
            return;
        while (true)
        {
            Task signal;
            lock (gate)
            {
                foreach (Guid id in recoveredCleanup.ToArray())
                    if (store.Get(id) is { } job && TryConfirmOldProcesses(job, out var drained))
                    {
                        ChangeAfterRecovery(drained);
                        recoveredCleanup.Remove(id);
                        NotifyCleanupChanged();
                    }
                if (recoveredCleanup.Count == 0 && cleanup.Count == 0)
                    return;
                token.ThrowIfCancellationRequested();
                context.Report("awaiting_engine_cleanup", status: ControlJobStatus.WaitingForResource);
                signal = cleanupChanged.Task;
            }
            try { await signal.WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); }
            catch (TimeoutException) { /* A supervisor can exit without any connected client. */ }
        }
    }

    private async Task<ControlResult> RetryDrainAsync(StoredControlJob previous, PendingCleanup pending)
    {
        bool stopped;
        try
        {
            if (retryResourceDrain is not null)
                stopped = await retryResourceDrain(previous.State.JobId, previous.State.OperationId).ConfigureAwait(false);
            else
            {
                await pending.Failure.RetryStop().ConfigureAwait(false);
                stopped = true;
            }
        }
        catch (Exception) { stopped = false; }

        lock (gate)
        {
            var job = store.Get(previous.State.JobId)!;
            if (stopped && TryConfirmOldProcesses(job, out job))
            {
                job = store.Save(job, ApplyOutcome(job.State, pending.Outcome));
                cleanup.Remove(job.State.JobId);
                NotifyCleanupChanged();
            }
            pending.Run = null;
            return Result(job.State);
        }
    }

    private void Recover()
    {
        foreach (var unfinished in store.Unfinished())
        {
            if (!TryConfirmOldProcesses(unfinished, out var job))
            {
                if (job.State.Status != ControlJobStatus.AwaitingCleanup)
                    store.Save(job, SetCleanupPending(job.State));
                recoveredCleanup.Add(job.State.JobId);
            }
            else if (job.State.Status != ControlJobStatus.Interrupted)
                ChangeAfterRecovery(job);
        }
    }

    private StoredControlJob ChangeAfterRecovery(StoredControlJob job) => store.Save(job, ApplyOutcome(job.State,
        new(ControlJobStatus.Interrupted, Error: new("host_interrupted", "The previous host stopped before completing this job.", true))));

    private bool TryConfirmOldProcesses(StoredControlJob job, out StoredControlJob current)
    {
        current = job;
        if (job.Journal.EngineProcesses.Count == 0)
            return true;
        var pending = job.Journal.EngineProcesses.Where(pair => !EngineProcessOwnership.IsDrained(pair.Value))
            .ToDictionary();
        if (pending.Count != job.Journal.EngineProcesses.Count)
            current = store.Save(job, job.State, job.Journal with { EngineProcesses = pending });
        return pending.Count == 0;
    }

    private void NotifyCleanupChanged()
    {
        cleanupChanged.TrySetResult();
        cleanupChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Stops new admissions and drains known workers. A false result leaves recovery diagnostics available.</summary>
    public async Task<bool> DrainForShutdownAsync(CancellationToken token = default)
    {
        Task local;
        lock (gate)
        {
            accepting = false;
            foreach (var (id, worker) in active)
            {
                if (store.Get(id) is { } job && job.State.Status != ControlJobStatus.Cancelling)
                    store.Save(job, job.State with { Status = ControlJobStatus.Cancelling, Stage = "cancelling" });
                worker.SignalCancellation();
            }
            local = Task.WhenAll(active.Values.Select(worker => worker.Task));
        }
        await local.WaitAsync(token).ConfigureAwait(false);

        Task[] pending;
        lock (gate)
            pending = cleanup.Select(entry => entry.Value.Run ??= Task.Run(() =>
                RetryDrainAsync(store.Get(entry.Key)!, entry.Value))).ToArray();
        await Task.WhenAll(pending).WaitAsync(token).ConfigureAwait(false);

        lock (gate)
        {
            foreach (Guid id in recoveredCleanup.ToArray())
                if (store.Get(id) is { } job && TryConfirmOldProcesses(job, out var drained))
                {
                    ChangeAfterRecovery(drained);
                    recoveredCleanup.Remove(id);
                }
            return cleanup.Count == 0 && recoveredCleanup.Count == 0;
        }
    }

    private static ControlJob SetCleanupPending(ControlJob state) => state with
    {
        Status = ControlJobStatus.AwaitingCleanup, Stage = "awaiting_cleanup",
        Error = new("engine_cleanup_pending", "The engine has not confirmed process shutdown.", true)
    };

    private static ControlJob ApplyOutcome(ControlJob state, ControlJobOutcome outcome) => state with
    {
        Status = outcome.Status,
        Stage = outcome.Status switch
        {
            ControlJobStatus.AwaitingTranslation => "awaiting_translation",
            ControlJobStatus.Interrupted => "interrupted",
            _ => outcome.Status.ToString().ToLowerInvariant()
        },
        Progress = outcome.Status == ControlJobStatus.Completed ? 1 : state.Progress,
        Value = outcome.Value?.Clone(),
        Error = outcome.Error
    };

    private static ControlResult Result(ControlJob job) => new(job.JobId, job.Revision, job.Value, job.Error);
    private static ControlResult NotFound() => Error("job_not_found", "The job does not exist in this scope.");

    private static ControlResult Error(string code, string message, bool retryable = false) =>
        new(Error: new(code, message, retryable));

    private byte[] Fingerprint(ControlRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(request.OperationId);
            if (request.Scope.SessionId is Guid session) writer.WriteStringValue(session);
            else writer.WriteNullValue();
            if (request.ExpectedRevision is long revision) writer.WriteNumberValue(revision);
            else writer.WriteNullValue();
            WriteCanonical(writer, CloneArguments(request.Arguments));
            writer.WriteEndArray();
        }
        return HMACSHA256.HashData(store.FingerprintKey, stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    private static JsonElement CloneArguments(JsonElement arguments) => arguments.ValueKind == JsonValueKind.Undefined
        ? JsonSerializer.SerializeToElement(new { })
        : arguments.Clone();

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                string? previous = null;
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (previous == property.Name)
                        throw new JsonException("Duplicate property.");
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                    previous = property.Name;
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                WriteCanonicalNumber(writer, element.GetRawText().AsSpan());
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteCanonicalNumber(Utf8JsonWriter writer, ReadOnlySpan<char> text)
    {
        bool negative = text[0] == '-';
        if (negative)
            text = text[1..];
        int exponentIndex = text.IndexOfAny('e', 'E');
        var exponent = exponentIndex < 0 ? BigInteger.Zero :
            BigInteger.Parse(text[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var coefficient = exponentIndex < 0 ? text : text[..exponentIndex];
        int point = coefficient.IndexOf('.');
        if (point >= 0)
            exponent -= coefficient.Length - point - 1;
        string digits = (point < 0 ? coefficient.ToString() :
            string.Concat(coefficient[..point], coefficient[(point + 1)..])).TrimStart('0');
        if (digits.Length == 0)
        {
            writer.WriteNumberValue(0);
            return;
        }
        string significant = digits.TrimEnd('0');
        exponent += digits.Length - significant.Length;
        writer.WriteRawValue(string.Concat(negative ? "-" : "", significant, "e",
            exponent.ToString(CultureInfo.InvariantCulture)));
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (shutdown is not null)
                return new(shutdown);
            shutdown = DrainForShutdownAsync();
            return new(shutdown);
        }
    }

    private sealed class ActiveJob : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private Task? signal;
        internal Task Task { get; set; } = Task.CompletedTask;
        internal CancellationToken Token => cancellation.Token;

        internal void SignalCancellation() => signal ??= cancellation.CancelAsync();

        public void Dispose()
        {
            if (signal is { IsCompleted: false } pending)
                _ = pending.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
            else
                cancellation.Dispose();
        }
    }

    private sealed record PendingCleanup(EngineProcessDrainException Failure, ControlJobOutcome Outcome)
    {
        internal Task<ControlResult>? Run { get; set; }
    }
}
