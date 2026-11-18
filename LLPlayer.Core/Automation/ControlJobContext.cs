using System.Security.Cryptography;
using System.Text.Json;
using FlyleafLib.MediaPlayer;
using LLPlayer.ControlApi;

namespace LLPlayer.Core.Automation;

/// <summary>A handler may report progress and checkpoint effects while its attempt owns the job.</summary>
public sealed class ControlJobContext : IEngineProcessOwner
{
    public const string RecognitionSourcePathIdentity = "recognition_source_path";

    private readonly ControlJobService service;
    private readonly bool acknowledgeUnknownEffect;
    private readonly SemaphoreSlim effects = new(1, 1);
    private static readonly StringComparer FilePaths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal ControlJobContext(ControlJobService service, Guid jobId, int attempt, ControlRequest request,
        bool isRecovery, bool acknowledgeUnknownEffect)
    {
        this.service = service;
        this.acknowledgeUnknownEffect = acknowledgeUnknownEffect;
        JobId = jobId;
        Attempt = attempt;
        Request = request;
        IsRecovery = isRecovery;
    }

    public Guid JobId { get; }
    internal int Attempt { get; }
    public ControlRequest Request { get; }
    public bool IsRecovery { get; }
    public JsonElement? Checkpoint => service.Read(JobId, Attempt, job => job.Journal.Checkpoint);

    public void Report(string stage, double? progress = null, ControlJobStatus? status = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (progress is double value && (!double.IsFinite(value) || value is < 0 or > 1))
            throw new ArgumentOutOfRangeException(nameof(progress));
        if (status is not (null or ControlJobStatus.Running or ControlJobStatus.WaitingForResource))
            throw new ArgumentOutOfRangeException(nameof(status));
        service.Update(JobId, Attempt, job =>
        {
            if (job.State.Status == ControlJobStatus.Cancelling)
                return (job.State, job.Journal);
            if (job.Secret)
                return (job.State with { Status = status ?? job.State.Status, Stage = status?.ToString().ToLowerInvariant() },
                    job.Journal);
            return (job.State with { Stage = stage, Progress = progress, Status = status ?? job.State.Status }, job.Journal);
        });
    }

    public void SaveCheckpoint(JsonElement checkpoint) =>
        UpdateJournal(journal => journal with { Checkpoint = checkpoint.Clone() });

    /// <summary>Use this for a command that relies on an open window or another transient input.</summary>
    public void RequireOriginalHost() => UpdateJournal(journal => journal with { DiscardOnRestart = true });

    public async Task VerifyInputAsync(string path, CancellationToken token, string? expectedRevision = null,
        string? identityKey = null)
    {
        path = Path.GetFullPath(path);
        try
        {
            var current = await FingerprintInputAsync(path, token).ConfigureAwait(false);
            if (expectedRevision is not null && !current.Equals(expectedRevision, StringComparison.OrdinalIgnoreCase))
                throw new ControlJobException("stale_media", "The media differs from the previously probed source.");
            VerifyIdentity(identityKey ?? "file:" + path, current);
        }
        catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ControlJobException("source_unavailable", "The job's source file is unavailable.",
                IsRecovery ? ControlJobStatus.Interrupted : ControlJobStatus.Failed, retryable: true);
        }
    }

    public static async Task<string> FingerprintInputAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }

    /// <summary>Checks a detached source or settings fingerprint on every attempt. Supply a digest for sensitive values.</summary>
    public void VerifyIdentity(string key, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        UpdateJournal(journal =>
        {
            if (journal.Identities.TryGetValue(key, out string? previous))
                return previous == fingerprint ? journal :
                    throw new ControlJobException("input_changed", "The job's saved input no longer matches.", ControlJobStatus.Interrupted);
            var identities = new Dictionary<string, string>(journal.Identities, StringComparer.Ordinal) { [key] = fingerprint };
            return journal with { Identities = identities };
        });
    }

    /// <summary>
    /// Publishes an already written staging file beside its destination. A crash after the rename can be recovered by hash.
    /// </summary>
    public async Task PublishFileAsync(string effectId, string stagedPath, string destination, bool overwrite,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        stagedPath = Path.GetFullPath(stagedPath);
        destination = Path.GetFullPath(destination);
        if (!IsSibling(stagedPath, destination))
            throw new ArgumentException("The staging file must be distinct and beside its destination.", nameof(stagedPath));

        await PublishWithOwnershipAsync(effectId, stagedPath, destination, overwrite, token).ConfigureAwait(false);
    }

    /// <summary>Writes a unique sibling, retaining it on failure only when the journal owns it for recovery.</summary>
    public async Task WriteAndPublishFileAsync(string effectId, string destination, bool overwrite,
        Func<string, CancellationToken, Task> writeStage, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentNullException.ThrowIfNull(writeStage);
        destination = Path.GetFullPath(destination);
        string directory = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("The output must be a file path.", nameof(destination));
        string stage = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        bool stageCreated = false;
        bool publicationAttempted = false;
        try
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            using (new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stageCreated = true;
            await writeStage(stage, token).ConfigureAwait(false);
            publicationAttempted = true;
            await PublishFileAsync(effectId, stage, destination, overwrite, token).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (stageCreated && (!publicationAttempted || !FilePaths.Equals(ReadEffect(effectId)?.StagedPath, stage)))
                    File.Delete(stage);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
                or Microsoft.Data.Sqlite.SqliteException)
            {
                // Keep a stage whose ownership cannot be read. Cleanup must not hide the operation error.
                System.Diagnostics.Trace.TraceWarning("Could not clean up a temporary control output.");
            }
        }
    }

    /// <summary>Reconciles a known publication before recreating expensive staged output.</summary>
    public Task<bool> TryRecoverFileAsync(string effectId, string destination, bool overwrite, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        return PublishWithOwnershipAsync(effectId, stagedPath: null, Path.GetFullPath(destination), overwrite, token);
    }

    private async Task<bool> PublishWithOwnershipAsync(string effectId, string? stagedPath, string destination, bool overwrite,
        CancellationToken token)
    {
        await effects.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await service.FilePublication.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await PublishStagedFileAsync(effectId, stagedPath, destination, overwrite, token).ConfigureAwait(false);
            }
            finally { service.FilePublication.Release(); }
        }
        finally { effects.Release(); }
    }

    private async Task<bool> PublishStagedFileAsync(string effectId, string? stagedPath, string destination, bool overwrite,
        CancellationToken token)
    {
        var recorded = ReadEffect(effectId);
        bool recovering = stagedPath is null;
        if (recorded is null && recovering)
            return false;
        string? existingHash = await ReadFileHashAsync(destination, token).ConfigureAwait(false);
        if (recorded is not null)
        {
            if (recorded.Kind != "file" || !FilePaths.Equals(recorded.Path, destination) || recorded.Overwrite != overwrite)
                throw new ControlJobException("file_effect_conflict", "The staged file effect conflicts with the journal.");
            if (existingHash == recorded.Hash)
            {
                if (!recorded.Completed)
                    SaveEffect(effectId, recorded with { Completed = true });
                return true;
            }
            if (recorded.Completed || existingHash != recorded.PreviousHash)
                throw new ControlJobException("output_changed", "The destination was changed since this job staged its output.",
                    ControlJobStatus.Interrupted);
        }
        else if (existingHash != null && !overwrite)
        {
            throw new ControlJobException("output_exists", "The destination already exists.");
        }

        if (!File.Exists(stagedPath) && recorded?.StagedPath is { } savedStage && IsSibling(savedStage, destination))
            stagedPath = savedStage;
        if (stagedPath is null || (recovering && !File.Exists(stagedPath)))
            return false;
        string stagingHash = await FlushAndHashAsync(stagedPath, token).ConfigureAwait(false);
        if (recorded is not null && recorded.Hash != stagingHash)
            throw new ControlJobException("staging_changed", "The resumable staged output differs from the original.",
                ControlJobStatus.Interrupted);
        if (recorded is null)
        {
            recorded = new("file", false, destination, stagingHash, existingHash, overwrite, StagedPath: stagedPath);
            SaveEffect(effectId, recorded);
        }
        else if (!FilePaths.Equals(recorded.StagedPath, stagedPath))
        {
            recorded = recorded with { StagedPath = stagedPath };
            SaveEffect(effectId, recorded);
        }
        token.ThrowIfCancellationRequested();
        if (await ReadFileHashAsync(destination, token).ConfigureAwait(false) != existingHash)
            throw new ControlJobException("output_changed", "The destination changed before the file could be published.",
                ControlJobStatus.Interrupted);
        File.Move(stagedPath, destination, overwrite: overwrite && existingHash is not null);
        SaveEffect(effectId, recorded with { Completed = true });
        return true;
    }

    private static bool IsSibling(string path, string destination) => !FilePaths.Equals(path, destination) &&
        FilePaths.Equals(Path.GetDirectoryName(path), Path.GetDirectoryName(destination));

    /// <summary>Records intent before an external action and preserves its receipt once confirmed.</summary>
    public async Task<JsonElement> RunExternalEffectAsync(string effectId,
        Func<CancellationToken, Task<JsonElement>> execute, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        await effects.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var recorded = ReadEffect(effectId);
            if (recorded is { Kind: not "external" })
                throw new ControlJobException("external_effect_conflict", "The external action conflicts with the journal.");
            if (recorded is { Completed: true, Receipt: JsonElement receipt })
                return receipt;
            if (recorded != null && !acknowledgeUnknownEffect)
                throw new ControlJobException("external_effect_unknown", "The prior external action might already have occurred.",
                    ControlJobStatus.Interrupted);
            token.ThrowIfCancellationRequested();
            recorded ??= new("external", false);
            SaveEffect(effectId, recorded);
            JsonElement result = await execute(token).ConfigureAwait(false);
            SaveEffect(effectId, recorded with { Completed = true, Receipt = result.Clone() });
            return result;
        }
        finally { effects.Release(); }
    }

    public JsonElement? GetExternalEffectReceipt(string effectId) =>
        ReadEffect(effectId) is { Kind: "external", Completed: true } effect ? effect.Receipt?.Clone() : null;

    private ControlJobEffect? ReadEffect(string id) =>
        service.Read(JobId, Attempt, job => job.Journal.Effects.GetValueOrDefault(id));

    private void SaveEffect(string id, ControlJobEffect effect) =>
        UpdateJournal(journal => journal with
        {
            Effects = new Dictionary<string, ControlJobEffect>(journal.Effects, StringComparer.Ordinal) { [id] = effect }
        });

    private void UpdateJournal(Func<ControlJobJournal, ControlJobJournal> update) =>
        service.Update(JobId, Attempt, job => !job.Secret
            ? (job.State, update(job.Journal))
            : throw new InvalidOperationException("Credential operations cannot retain checkpoints."));

    private static async Task<string?> ReadFileHashAsync(string path, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        }
        catch (FileNotFoundException) { return null; }
    }

    private static async Task<string> FlushAndHashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Flush(flushToDisk: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }

    void IEngineProcessOwner.Record(EngineProcessIdentity process)
    {
        UpdateJournal(journal => journal with { EngineProcesses = new(journal.EngineProcesses) { [process.Id] = process } });
    }

    void IEngineProcessOwner.Drained(Guid id)
    {
        UpdateJournal(journal =>
        {
            if (!journal.EngineProcesses.ContainsKey(id))
                return journal;
            var remaining = new Dictionary<Guid, EngineProcessIdentity>(journal.EngineProcesses);
            remaining.Remove(id);
            return journal with { EngineProcesses = remaining };
        });
    }
}

public sealed class ControlJobException : Exception
{
    public ControlJobException(string code, string message, ControlJobStatus status = ControlJobStatus.Failed, bool retryable = false)
        : base(message)
    {
        Error = new(code, message, retryable);
        Status = status;
    }

    internal ControlError Error { get; }
    internal ControlJobStatus Status { get; }
}
