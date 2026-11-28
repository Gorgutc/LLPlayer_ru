using FlyleafLib.Vad;

namespace FlyleafLibTests.MediaPlayer;

public class AsrVadPhantomEvidenceTests
{
    [Fact]
    public void MeasuredSilenceHasCoverageDistinctFromMissingEvidence()
    {
        Assert.Null(AsrVadDetector.ToPhantomEvidence(511, []));

        var evidence = AsrVadDetector.ToPhantomEvidence(1_100, []);
        Assert.NotNull(evidence);
        Assert.Equal(1_024, evidence.Value.CoveredSamples);
        Assert.Empty(evidence.Value.Intervals);
    }

    [Fact]
    public void SpeechRetainsItsSampleCoordinatesWhileTheUnmeasuredTailStaysUncovered()
    {
        var evidence = AsrVadDetector.ToPhantomEvidence(16_100,
            [new SileroSpeechSegment(0, null, .25f, .5f)]);

        Assert.NotNull(evidence);
        Assert.Equal(15_872, evidence.Value.CoveredSamples);
        var interval = Assert.Single(evidence.Value.Intervals);
        Assert.Equal(4_000, interval.StartSample);
        Assert.Equal(8_000, interval.EndSample);
    }

    [Fact]
    public void MalformedDetectorIntervalsAreNotNegativeEvidence()
    {
        Assert.Null(AsrVadDetector.ToPhantomEvidence(16_000, [new(0, null, -.1f, .5f)]));
        Assert.Null(AsrVadDetector.ToPhantomEvidence(16_000, [new(0, null, .4f, 2f)]));
        Assert.Null(AsrVadDetector.ToPhantomEvidence(16_000, [new(0, null, .4f, .5f), new(0, null, .45f, .7f)]));
    }
}
