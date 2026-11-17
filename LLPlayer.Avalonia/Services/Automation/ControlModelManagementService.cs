using LLPlayer.ControlApi;
using LLPlayer.Core.Services.Models;

namespace LLPlayer.Avalonia.Services.Automation;

internal sealed class ControlModelManagementService(
    IModelManagementService discovery, AppPaths paths, string whisperDirectory) : IModelManagementService
{
    static readonly ControlScope Scope = new(ControlProtocol.DefaultProfileId);
    bool detached;

    public IReadOnlyList<ModelEntry> Discover(ModelKind kind) => discovery.Discover(kind);
    public string GetDirectory(ModelKind kind) => discovery.GetDirectory(kind);
    public void Detach() => Volatile.Write(ref detached, true);

    public Task DownloadAsync(string id, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        => RunAsync("models.download", id, progress, cancellationToken);

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
        => RunAsync("models.remove", id, null, cancellationToken);

    async Task RunAsync(string operation, string id, IProgress<ModelDownloadProgress>? progress, CancellationToken token)
    {
        using ControlClient client = await ControlClient.ConnectOrStartAsync(paths.ConfigDir, paths.StateDir, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Once this call is made, wait for the durable identifier even if the window detaches.
        ControlResult accepted = await client.InvokeAsync(new(Guid.NewGuid(), operation, Scope, null,
            ControlProtocol.Encode(new { Id = id, WhisperDirectory = Path.GetFullPath(whisperDirectory) }))).ConfigureAwait(false);
        Require(accepted);
        Guid jobId = accepted.JobId ?? throw new InvalidOperationException("The model host did not return a job.");
        try
        {
            while (true)
            {
                ControlJob job = await ReadJobAsync(client, jobId, token).ConfigureAwait(false);
                if (job.Status == ControlJobStatus.Completed) return;
                ThrowIfStopped(job, token);
                string stage = job.Stage ?? job.Status.ToString();
                string percent = job.Progress is { } fraction ? $" ({fraction:P0})" : "";
                progress?.Report(new(0, null, stage + percent));
                await Task.Delay(TimeSpan.FromMilliseconds(300), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !Volatile.Read(ref detached))
        {
            ControlResult cancellation = await client.CancelAsync(new(jobId, Scope)).ConfigureAwait(false);
            Require(cancellation);
            while (true)
            {
                ControlJob job = await ReadJobAsync(client, jobId, CancellationToken.None).ConfigureAwait(false);
                if (job.Status == ControlJobStatus.Completed) return;
                ThrowIfStopped(job, token);
                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
            }
        }
    }

    static async Task<ControlJob> ReadJobAsync(ControlClient client, Guid id, CancellationToken ct)
        => await client.GetAsync(Scope, id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The model job was not found.");

    static void ThrowIfStopped(ControlJob job, CancellationToken cancellation)
    {
        if (job.Status == ControlJobStatus.Cancelled) throw new OperationCanceledException(cancellation);
        if (job.Status is ControlJobStatus.Failed or ControlJobStatus.Interrupted or ControlJobStatus.AwaitingCleanup)
            throw new ControlException(job.Error ?? new("model_job_failed", $"The model job ended with status {job.Status}."));
    }

    static void Require(ControlResult result)
    {
        if (result.Error is { } error) throw new ControlException(error);
    }
}
