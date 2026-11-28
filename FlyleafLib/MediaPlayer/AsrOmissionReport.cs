namespace FlyleafLib.MediaPlayer.AsrOmissions;

#nullable enable

public enum AsrOmissionStatus
{
    Disabled,
    Unsupported,
    Unavailable,
    Analyzed,
    Partial
}

/// <summary>Detached ASR review suggestions. Their text and clocks never replace recognition cues.</summary>
public sealed record AsrOmissionReport(bool Enabled, AsrOmissionStatus Status,
    IReadOnlyList<AsrOmissionCandidate> Candidates)
{
    public static AsrOmissionReport Initial(bool enabled, bool supported) =>
        new(enabled, !enabled ? AsrOmissionStatus.Disabled : supported ? AsrOmissionStatus.Unavailable :
            AsrOmissionStatus.Unsupported, Array.Empty<AsrOmissionCandidate>());
}

internal sealed record AsrOmissionEvidence(AsrOmissionStatus Status, AsrOmissionChunk? Chunk = null);
