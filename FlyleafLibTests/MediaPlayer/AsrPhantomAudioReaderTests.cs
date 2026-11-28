using FlyleafLib.MediaPlayer;

namespace FlyleafLibTests.MediaPlayer;

public class AsrPhantomAudioReaderTests
{
    [Theory]
    [InlineData(-626, false)]
    [InlineData(-625, true)]
    [InlineData(0, true)]
    [InlineData(625, true)]
    [InlineData(626, false)]
    [InlineData(9_678, false)]
    [InlineData(380_538, false)]
    public void ChunkBoundaryUsesActualSampleCountWithOneSampleTolerance(int gapTicks, bool canBorrow)
    {
        var left = TimeSpan.FromSeconds(12);
        var frameEnd = left + TimeSpan.FromTicks(8_431L * 625);
        var right = frameEnd + TimeSpan.FromTicks(gapTicks);

        Assert.Equal(canBorrow, AudioReader.CanBorrowPhantomContext(left, 8_431, frameEnd, right));
    }

    [Fact]
    public void EmptyOrReversedChunksNeverAuthorizeBorrowing()
    {
        Assert.False(AudioReader.CanBorrowPhantomContext(TimeSpan.Zero, 0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.False(AudioReader.CanBorrowPhantomContext(TimeSpan.FromSeconds(1), 8_431, TimeSpan.FromSeconds(2), TimeSpan.Zero));
    }

    [Fact]
    public void AGapInSourceFramesCannotBeMaskedByTheAggregatePcmLength()
    {
        var nominalBoundary = TimeSpan.FromSeconds(2);
        var actualFrameEnd = TimeSpan.FromMilliseconds(2_200);

        Assert.False(AudioReader.CanBorrowPhantomContext(TimeSpan.Zero, 32_000, actualFrameEnd, nominalBoundary));
    }

    [Theory]
    [InlineData(372, true)]
    [InlineData(4_800_000, false)]
    [InlineData(5_127_999, false)]
    [InlineData(5_128_001, true)]
    public void GapsInvalidateOnlyTheAcousticWindowAroundTheSplice(int cut, bool canAnalyze)
    {
        var candidate = new AsrPhantomCandidate(4_800_000, 5_120_000, 0.6);

        Assert.Equal(canAnalyze, AudioReader.HasContinuousPhantomWindow(candidate, [cut]));
    }
}
