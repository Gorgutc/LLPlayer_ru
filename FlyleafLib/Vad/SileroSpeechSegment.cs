// Vendored from snakers4/silero-vad (examples/csharp), MIT License, Copyright (c) 2020-present Silero Team.
// https://github.com/snakers4/silero-vad — see docs/agent/dependency-baseline.md (F-19 slice 2).
// Local changes: namespace only (VadDotNet -> FlyleafLib.Vad); logic unchanged.
namespace FlyleafLib.Vad;

public class SileroSpeechSegment
{
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
    public float? StartSecond { get; set; }
    public float? EndSecond { get; set; }

    public SileroSpeechSegment()
    {
    }

    public SileroSpeechSegment(int startOffset, int? endOffset, float? startSecond, float? endSecond)
    {
        StartOffset = startOffset;
        EndOffset = endOffset;
        StartSecond = startSecond;
        EndSecond = endSecond;
    }
}
