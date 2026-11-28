using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLPlayer.ControlApi;

namespace LLPlayer.Core.Automation;

public sealed record ControlDocumentReference(Guid JobId, string Revision);
public sealed record ControlDocumentRead(Guid JobId, string? Revision = null, int Offset = 0, int Limit = 64);
public sealed record ControlTranslationRead(Guid JobId, string Revision, int Offset = 0, int Limit = 64);
public sealed record ControlTranslationSubmit(Guid JobId, string Revision, IReadOnlyList<ControlCueTranslation> Cues);
public sealed record ControlTranslationCommit(Guid JobId, string Revision);

public sealed record ControlDocumentPage(Guid DocumentId, string Revision, string? SourceLanguage,
    string? TargetLanguage, ImmutableArray<ControlCue> Cues, int TotalCount, int? NextOffset);
public sealed record ControlDocumentSummary(Guid DocumentId, string Revision, string? SourceLanguage,
    string? TargetLanguage, int TotalCount, int? SelectedCount = null, int? ReceivedCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ControlOmissionSummary? OmissionDetection = null);

/// <summary>Routes external translation through the persisted job and keeps document results bounded at the wire boundary.</summary>
public sealed class ControlTranslationOperations(ControlJobService jobs)
{
    private sealed record Checkpoint(ControlCueDocument? Document = null, ControlTranslationDraft? Draft = null,
        string? CommittedSourceRevision = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ControlRecognitionOmissions? Omissions = null);

    public static ControlJobOutcome SaveDocument(ControlJobContext context, ControlCueDocument document,
        ControlRecognitionOmissions? omissions = null)
    {
        context.SaveCheckpoint(ControlProtocol.Encode(new Checkpoint(Document: document, Omissions: omissions)));
        return new(ControlJobStatus.Completed, ControlProtocol.Encode(Summary(document, omissions: omissions)));
    }

    public static void SaveDocumentPreview(ControlJobContext context, ControlCueDocument document)
        => context.SaveCheckpoint(ControlProtocol.Encode(new Checkpoint(Document: document)));

    public static ControlJobOutcome AwaitHarnessTranslation(ControlJobContext context, ControlCueDocument document,
        string targetLanguage, int start = 0, int? count = null, ControlRecognitionOmissions? omissions = null)
    {
        try
        {
            var draft = ControlTranslationDraft.Start(document, targetLanguage, start, count);
            context.SaveCheckpoint(ControlProtocol.Encode(new Checkpoint(Draft: draft, Omissions: omissions)));
            return Awaiting(draft, omissions);
        }
        catch (ControlCueException e) { throw Failure(e); }
    }

    /// <summary>Restore a saved draft after the handler revalidates its input and configuration.</summary>
    public static ControlJobOutcome? RestoreHarnessTranslation(ControlJobContext context) =>
        ReadCheckpoint(context.Checkpoint) is { Draft: { } draft } saved ? Awaiting(draft, saved.Omissions) : null;

    public static ControlRecognitionOmissions? ReadRecognitionOmissions(JsonElement? checkpoint) =>
        ReadCheckpoint(checkpoint)?.Omissions;

    public async Task<ControlCueDocument> GetDocumentAsync(ControlScope scope, ControlDocumentReference reference,
        CancellationToken token = default)
    {
        var job = await jobs.GetAsync(scope, reference.JobId, token).ConfigureAwait(false);
        if (job is null)
            throw new ControlException(new("job_not_found", "The selected job does not exist in this scope."));
        if (job.Status is not (ControlJobStatus.Completed or ControlJobStatus.AwaitingTranslation))
            throw new ControlException(new("document_unavailable", "The operation requires a completed subtitle document.", true));
        var (result, document) = await ReadDocumentAsync(scope, reference.JobId, token).ConfigureAwait(false);
        if (result.Error is { } error) throw new ControlException(error);
        if (document!.Revision != reference.Revision)
            throw new ControlException(new("revision_conflict", "The document changed since it was read."));
        return document;
    }

    public async Task<ControlResult> ReadDocumentAsync(ControlScope scope, ControlDocumentRead args,
        CancellationToken token = default)
    {
        var (result, document) = await ReadDocumentAsync(scope, args.JobId, token).ConfigureAwait(false);
        if (result.Error is not null) return result;
        if (args.Revision is not null && document!.Revision != args.Revision)
            return ControlResult.Fail("revision_conflict", "The document changed since it was read.", retryable: true);
        if (args.Offset < 0 || args.Offset > document!.Cues.Length || args.Limit is < 1 or > ControlTranslationDraft.MaximumPageSize
            || (args.Offset != 0 && args.Revision is null))
            return ControlResult.Fail("invalid_arguments", "The cue page range is invalid or the cursor's revision is missing.");
        int count = Math.Min(args.Limit, document.Cues.Length - args.Offset);
        int next = args.Offset + count;
        var page = new ControlDocumentPage(document.DocumentId, document.Revision, document.SourceLanguage, document.TargetLanguage,
            ImmutableArray.Create(document.Cues.AsSpan(args.Offset, count)), document.Cues.Length,
            next < document.Cues.Length ? next : null);
        return result with { Value = ControlProtocol.Encode(page) };
    }

    public async Task<ControlResult> ReadDraftAsync(ControlScope scope, ControlTranslationRead args,
        CancellationToken token = default)
    {
        var result = await jobs.ReadAwaitingTranslationCheckpointAsync(scope, args.JobId, token).ConfigureAwait(false);
        if (result.Error is not null) return result;
        try { return result with { Value = ControlProtocol.Encode(RequireDraft(result.Value).Read(args.Revision, args.Offset, args.Limit)) }; }
        catch (ControlCueException e) { return DraftError(e); }
    }

    public Task<ControlResult> SubmitAsync(ControlScope scope, long? expectedJobRevision, ControlTranslationSubmit args,
        CancellationToken token = default) => jobs.UpdateAwaitingTranslationAsync(scope, args.JobId, expectedJobRevision, checkpoint =>
    {
        try
        {
            var saved = RequireDraftCheckpoint(checkpoint);
            var draft = saved.Draft!;
            var updated = draft.Submit(args.Revision, args.Cues);
            return new(ControlProtocol.Encode(saved with { Draft = updated }), Awaiting(updated, saved.Omissions),
                NoChange: ReferenceEquals(draft, updated));
        }
        catch (ControlCueException e) { return new(checkpoint, new(ControlJobStatus.AwaitingTranslation, Error: DraftError(e).Error)); }
    }, token);

    public async Task<ControlResult> CommitAsync(ControlScope scope, long? expectedJobRevision, ControlTranslationCommit args,
        CancellationToken token = default)
    {
        var result = await jobs.UpdateAwaitingTranslationAsync(scope, args.JobId, expectedJobRevision, checkpoint =>
        {
            try
            {
                var saved = RequireDraftCheckpoint(checkpoint);
                var draft = saved.Draft!;
                var replacement = draft.Commit(args.Revision, draft.Source);
                return new(ControlProtocol.Encode(new Checkpoint(Document: replacement,
                        CommittedSourceRevision: draft.Source.Revision, Omissions: saved.Omissions)),
                    new(ControlJobStatus.Completed, ControlProtocol.Encode(Summary(replacement, omissions: saved.Omissions))));
            }
            catch (ControlCueException e) { return new(checkpoint, new(ControlJobStatus.AwaitingTranslation, Error: DraftError(e).Error)); }
        }, token).ConfigureAwait(false);
        if (result.Error?.Code != "invalid_job_state") return result;
        var retained = await jobs.ReadCheckpointAsync(scope, args.JobId, token).ConfigureAwait(false);
        if (ReadCheckpoint(retained.Value) is { Document: not null } committed && committed.CommittedSourceRevision == args.Revision)
        {
            var job = await jobs.GetAsync(scope, args.JobId, token).ConfigureAwait(false);
            if (job is { Status: ControlJobStatus.Completed })
                return new(job.JobId, job.Revision, job.Value, job.Error);
        }
        return result;
    }

    private async Task<(ControlResult Result, ControlCueDocument? Document)> ReadDocumentAsync(ControlScope scope, Guid jobId,
        CancellationToken token)
    {
        var result = await jobs.ReadCheckpointAsync(scope, jobId, token).ConfigureAwait(false);
        if (result.Error is not null) return (result, null);
        var checkpoint = ReadCheckpoint(result.Value);
        var document = checkpoint?.Document ?? checkpoint?.Draft?.Source;
        return document is null
            ? (ControlResult.Fail("document_unavailable", "The requested job has no retained subtitle document."), null)
            : (result, document);
    }

    private static Checkpoint? ReadCheckpoint(JsonElement? checkpoint)
    {
        if (checkpoint is not { ValueKind: JsonValueKind.Object } data ||
            (!data.TryGetProperty("document", out _) && !data.TryGetProperty("draft", out _))) return null;
        return data.Deserialize<Checkpoint>(ControlProtocol.Json);
    }

    private static Checkpoint RequireDraftCheckpoint(JsonElement? checkpoint) =>
        ReadCheckpoint(checkpoint) is { Draft: not null } saved ? saved
            : throw new ControlCueException("translation_unavailable", "No translation draft was retained by this job.");

    private static ControlTranslationDraft RequireDraft(JsonElement? checkpoint) => RequireDraftCheckpoint(checkpoint).Draft!;

    private static ControlJobOutcome Awaiting(ControlTranslationDraft draft, ControlRecognitionOmissions? omissions) =>
        new(ControlJobStatus.AwaitingTranslation, ControlProtocol.Encode(Summary(draft.Source, draft, omissions)));

    private static ControlDocumentSummary Summary(ControlCueDocument document, ControlTranslationDraft? draft = null,
        ControlRecognitionOmissions? omissions = null) =>
        new(document.DocumentId, document.Revision, document.SourceLanguage, draft?.TargetLanguage ?? document.TargetLanguage,
            document.Cues.Length, draft?.CueOrdinals.Length, draft?.Submitted.Count, omissions?.Summarize());

    private static ControlResult DraftError(ControlCueException e) =>
        ControlResult.Fail(e.Code, e.Message, retryable: e.Code is "revision_conflict" or "translation_incomplete");

    private static ControlException Failure(ControlCueException e) => new(new(e.Code, e.Message));
}
