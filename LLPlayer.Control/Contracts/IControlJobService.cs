using System.Text.Json;

namespace LLPlayer.ControlApi;

public enum ControlJobStatus
{
    Accepted, WaitingForResource, Running, AwaitingTranslation, Cancelling,
    AwaitingCleanup, Completed, Failed, Cancelled, Interrupted
}

public sealed record ControlJob(
    Guid JobId, ControlScope Scope, string OperationId, ControlJobStatus Status,
    long Revision, DateTimeOffset UpdatedAt, string? Stage = null, double? Progress = null,
    JsonElement? Value = null, ControlError? Error = null);

public sealed record ControlJobPage(IReadOnlyList<ControlJob> Items, string? NextCursor = null);
public sealed record ControlJobAction(Guid JobId, ControlScope Scope, bool AcknowledgeUnknownEffect = false);

public interface IControlJobService
{
    Task<ControlJob?> GetAsync(ControlScope scope, Guid jobId, CancellationToken ct = default);
    Task<ControlJobPage> ListAsync(ControlScope scope, string? cursor = null, int limit = 50,
        CancellationToken ct = default);
    Task<ControlResult> CancelAsync(ControlJobAction action, CancellationToken ct = default);
    Task<ControlResult> RetryAsync(ControlJobAction action, CancellationToken ct = default);
    Task<ControlResult> ResumeAsync(ControlJobAction action, CancellationToken ct = default);
}
