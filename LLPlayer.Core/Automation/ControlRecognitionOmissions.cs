using System.Collections.Immutable;
using System.Text.Json;
using FlyleafLib.MediaPlayer.AsrOmissions;
using LLPlayer.ControlApi;

namespace LLPlayer.Core.Automation;

/// <summary>Persists separately paged ASR suggestions without adding them to the subtitle document.</summary>
public sealed record ControlRecognitionOmissions(bool Enabled, string Status, ImmutableArray<ControlOmissionCandidate> Candidates)
{
    private const int MaximumPageSize = 256;

    public static ControlRecognitionOmissions Capture(AsrOmissionReport? report, bool requested)
    {
        if (!requested)
            return new(false, "disabled", []);
        if (report is null)
            return new(true, "unavailable", []);

        return new(report.Enabled, CamelCase(report.Status), report.Candidates.Select(candidate =>
            new ControlOmissionCandidate(candidate.Text, candidate.StartMs, candidate.EndMs,
                CamelCase(candidate.Kind), candidate.ReviewOnlyReason is { } reason ? CamelCase(reason) : null,
                candidate.FollowingBaselineCueIndex, candidate.FollowingBaselineStartMs, candidate.ChunkOffsetMs))
            .ToImmutableArray());
    }

    public ControlOmissionSummary Summarize() => new(Enabled, Status, Candidates.Length);

    public ControlOmissionPage Read(ControlOmissionRead args)
    {
        if (args.Offset < 0 || args.Offset > Candidates.Length || args.Limit is < 1 or > MaximumPageSize)
            throw new ControlException(new("invalid_arguments", "The omission suggestion page range is invalid."));
        int count = Math.Min(args.Limit, Candidates.Length - args.Offset);
        int next = args.Offset + count;
        return new(args.JobId, Summarize(), ImmutableArray.Create(Candidates.AsSpan(args.Offset, count)),
            next < Candidates.Length ? next : null, args.FileIndex);
    }

    private static string CamelCase<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
