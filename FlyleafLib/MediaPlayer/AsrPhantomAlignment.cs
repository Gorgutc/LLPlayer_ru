namespace FlyleafLib.MediaPlayer;

// Word spans are relative to the candidate's outward start. Fractional sample
// offsets retain each native cue edge when the candidate moves to a wider PCM window.
internal readonly record struct AsrPhantomAlignment(int WordCount, int TimedWordCount,
    decimal DurationSeconds, decimal LargestGapSeconds, decimal CoverageFraction, decimal MaximumWordProbability,
    IReadOnlyList<AsrPhantomVoiceInterval> TimedSpans, decimal StartOffset = 0, decimal EndOffset = 0);
