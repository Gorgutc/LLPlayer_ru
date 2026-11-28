#nullable enable

namespace FlyleafLib.MediaPlayer.AsrOmissions;

public sealed record AsrOmissionWord(string Text, long StartMs, long EndMs, double? Probability = null);

public sealed record AsrOmissionCue(string Text, long StartMs, long EndMs, IReadOnlyList<AsrOmissionWord> Words);

public sealed record AsrOmissionInterval(long StartMs, long EndMs);

/// <summary>A separate decode of a tile from the same PCM. Tile cues use tile-relative milliseconds.</summary>
public sealed record AsrOmissionConfirmation(long OffsetMs, IReadOnlyList<AsrOmissionCue> Cues);

/// <summary>Comparable evidence from the same PCM. A, B and VAD clocks are chunk-relative.</summary>
public sealed record AsrOmissionChunk(
    long OffsetMs,
    long DurationMs,
    IReadOnlyList<AsrOmissionCue> Baseline,
    IReadOnlyList<AsrOmissionCue> Donor,
    IReadOnlyList<AsrOmissionConfirmation>? Confirmations = null,
    IReadOnlyList<AsrOmissionInterval>? VadIntervals = null);

public enum AsrOmissionKind
{
    Gap,
    Covered,
    Mixed,
    CollapsedTiming,
    Seam,
    ShortCompletion,
    CueHeadCompletion,
    CompleteCueInGap,
    EarlyOnsetRepair,
    ContextConfirmedGap
}

public enum AsrOmissionReviewReason
{
    BaselineCueAlreadyOnScreen,
    PartialDonorCue
}

/// <summary>
/// An additive suggestion on the source clock. The optional following A cue is zero-based within its chunk;
/// its proposed start is for a detached preview only. The donor range is end-exclusive and the
/// optional confirmation tile offset is chunk-relative.
/// </summary>
public sealed record AsrOmissionCandidate(
    string Text,
    long StartMs,
    long EndMs,
    AsrOmissionKind Kind,
    AsrOmissionReviewReason? ReviewOnlyReason = null,
    int? FollowingBaselineCueIndex = null,
    long? FollowingBaselineStartMs = null,
    long ChunkOffsetMs = 0,
    int DonorWordStartIndex = 0,
    int DonorWordEndIndex = 0,
    long? ConfirmationOffsetMs = null)
{
    public bool IsReviewOnly => ReviewOnlyReason.HasValue;
}
