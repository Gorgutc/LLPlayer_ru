using System.Text.Json.Serialization;

namespace LLPlayer.ControlApi;

public sealed record ControlOmissionRead(Guid JobId, int Offset = 0, int Limit = 64, int? FileIndex = null);

public sealed record ControlOmissionSummary(bool Enabled, string Status, int TotalCount)
{
    public bool Applied => Enabled && Available == true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Available => Status switch
    {
        "analyzed" or "partial" => true,
        "unavailable" or "unsupported" => false,
        _ => null
    };
}

/// <summary>An additive suggestion on the source media clock. ChunkOffsetMs is provenance only.</summary>
public sealed record ControlOmissionCandidate(string Text, long StartMs, long EndMs, string Kind,
    string? ReviewOnlyReason = null, int? FollowingBaselineCueIndex = null,
    long? FollowingBaselineStartMs = null, long ChunkOffsetMs = 0)
{
    public bool IsReviewOnly => ReviewOnlyReason is not null;
}

public sealed record ControlOmissionPage(Guid JobId, ControlOmissionSummary OmissionDetection,
    IReadOnlyList<ControlOmissionCandidate> Candidates, int? NextOffset, int? FileIndex = null);
