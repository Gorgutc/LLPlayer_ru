namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>One translated line to dub, with its source timing (the slot the dub should fit).</summary>
public sealed record DubbingLine(int Index, string Text, TimeSpan Start, TimeSpan End)
{
    public double SlotMs => (End - Start).TotalMilliseconds;
}

/// <summary>
/// A single-line synthesis request. <paramref name="RefClipPath"/> is the per-speaker reference clip
/// for timbre cloning (Phase 3); cloud <see cref="ITtsService"/> implementations MUST reject it
/// (cloud cloning of arbitrary speakers violates provider consent ToS).
/// </summary>
public sealed record TtsRequest(
    string Text,
    string VoiceId,
    int TargetDurationMs,
    string? RefClipPath = null,
    string? Gender = null);

/// <summary>Result of synthesizing one line: a path to a temp WAV file + its actual duration.</summary>
public sealed record TtsResult(string WavPath, int DurationMs);

/// <summary>A selectable voice (preset bank entry or a discovered voice).</summary>
public sealed record TtsVoice(string Id, string DisplayName, string Gender, string Language);

/// <summary>
/// A synthesized clip placed on the dub timeline: its temp WAV, its start offset (ms), and the
/// pitch-preserving time-stretch factor to apply (atempo &gt; 1 shortens). Computed by C#
/// (<see cref="DubbingIsochrony"/>) and executed by the sidecar's DSP.
/// </summary>
public sealed record DubClip(string WavPath, double StartMs, double Atempo);

/// <summary>
/// Whole-job assembly request: the sidecar decodes the original audio, time-stretches + places each
/// clip on a silence bed, ducks the original under the dub, and encodes the result to
/// <paramref name="OutputPath"/>. All DSP runs in the sidecar (LLPlayer bundles the libav DLLs, not an
/// ffmpeg CLI), keeping the licensed DSP libs in the separate-process aggregation boundary.
/// </summary>
public sealed record AssembleRequest(
    string MediaPath,
    string OutputPath,
    string OutputFormat,
    int DuckingPercent,
    double TotalMs,
    IReadOnlyList<DubClip> Clips);
