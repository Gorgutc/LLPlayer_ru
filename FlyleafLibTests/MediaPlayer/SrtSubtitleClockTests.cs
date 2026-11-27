using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.MediaPlayer;

public sealed class SrtSubtitleClockTests
{
    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void DistributedExactAnchorsRetimesTheWholeTrackAndPreservesCueShapes()
    {
        static int Expected(int original) => original * 19 / 20 + 2000;
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => Expected(time));
        foreach (int i in new[] { 3, 4, 9 })
            proposals[i] = proposals[i] with
            {
                Start = null, End = null, IsExactAnchor = false, Coverage = 0, Rejection = SrtAlignmentRejection.Unmatched
            };

        var fit = SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(60), Token);

        Assert.NotNull(fit);
        Assert.Equal(9, fit.Value.Summary.AnchorCount);
        Assert.Equal(.95, fit.Value.Summary.Scale, precision: 6);
        Assert.Equal(TimeSpan.FromMilliseconds(1850), fit.Value.Summary.FirstAdjustment);
        Assert.Equal(TimeSpan.FromMilliseconds(-420), fit.Value.Summary.LastAdjustment);
        Assert.Equal(proposals.Length, fit.Value.Intervals.Count);
        for (int i = 0; i < proposals.Length; i++)
        {
            var authored = proposals[i].Cue;
            var (start, end) = fit.Value.Intervals[i];
            Assert.Equal(TimeSpan.FromMilliseconds(Expected((int)authored.Start.TotalMilliseconds)), start);
            Assert.Equal(TimeSpan.FromMilliseconds(1330), end - start);
        }
    }

    [Fact]
    public void OrdinaryAuthorLeadAndHoldDoesNotMoveTheTimeline()
    {
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => time);
        for (int i = 0; i < proposals.Length; i++)
            proposals[i] = proposals[i] with
            {
                Start = proposals[i].Start!.Value.Add(TimeSpan.FromMilliseconds(350)),
                End = proposals[i].End!.Value.Subtract(TimeSpan.FromMilliseconds(200))
            };

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(60), Token));
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 6)]
    [InlineData(0, 5)]
    public void PartialOrWeaklyAnchoredTracksCannotMoveUnverifiedEnds(int firstAnchor, int anchorCount)
    {
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => time + 2800);
        for (int i = 0; i < proposals.Length; i++)
            proposals[i] = proposals[i] with { IsExactAnchor = i >= firstAnchor && i - firstAnchor < anchorCount };

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(60), Token));
    }

    [Fact]
    public void ALocalOffsetChangeCannotMoveTheEntireSrt()
    {
        SrtAlignmentProposal[] proposals = MakeProposals((i, time) => time + (i < 6 ? 2000 : 6000));

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(60), Token));
    }

    [Fact]
    public void SystematicLocalBiasIsRejectedEvenWhenAllEdgesAreWithinTheGlobalTolerance()
    {
        // Held-out residuals stay within 600 ms, but the second quartile is off by a median 600 ms.
        SrtAlignmentProposal[] proposals = MakeProposals((i, time) => time + (i is >= 3 and < 6 ? 3400 : 2800));

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(60), Token));
    }

    [Fact]
    public void ExtrapolationPastMediaDurationDoesNotClipAnUnmatchedCue()
    {
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => time + 3500);
        proposals[^1] = proposals[^1] with
        {
            Cue = proposals[^1].Cue with
            {
                Start = TimeSpan.FromMilliseconds(44500), End = TimeSpan.FromMilliseconds(45100)
            },
            Start = null, End = null, IsExactAnchor = false, Coverage = 0, Rejection = SrtAlignmentRejection.Unmatched
        };

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromMilliseconds(48200), Token));
    }

    [Fact]
    public void ASmallOvershootTrimsOnlyIndependentlyObservedMediaEdges()
    {
        var (proposals, duration) = PlaybackEdgeProposals(firstOvershootMs: 300, lastOvershootMs: 300);

        var fit = SrtSubtitleClock.TryCreate(proposals, duration, Token);

        Assert.NotNull(fit);
        Assert.Equal(2, fit.Value.Summary.ClippedBoundaryCount);
        Assert.Equal(TimeSpan.Zero, fit.Value.Intervals[0].Start);
        Assert.Equal(duration, fit.Value.Intervals[^1].End);
        for (int i = 0; i < proposals.Length; i++)
        {
            Assert.Equal(proposals[i].Start, fit.Value.Intervals[i].Start);
            Assert.Equal(proposals[i].End, fit.Value.Intervals[i].End);
            Assert.True(SrtSubtitleClock.IsSupported(proposals[i], fit.Value.Intervals[i]));
        }
    }

    [Theory]
    [InlineData(301, 0)]
    [InlineData(0, 301)]
    public void AnOvershootBeyondTheAudioEdgeToleranceAbstains(int firstOvershootMs, int lastOvershootMs)
    {
        var (proposals, duration) = PlaybackEdgeProposals(firstOvershootMs, lastOvershootMs);

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, duration, Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnExactPhraseAwayFromTheMediaEdgeCannotAuthorizeClipping(bool missingStartEvidence)
    {
        var (proposals, duration) = PlaybackEdgeProposals(firstOvershootMs: 290, lastOvershootMs: 290);
        if (missingStartEvidence)
            proposals[0] = proposals[0] with { Start = TimeSpan.FromMilliseconds(400) };
        else
            proposals[^1] = proposals[^1] with { End = duration.Subtract(TimeSpan.FromMilliseconds(400)) };

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, duration, Token));
    }

    [Fact]
    public void AnUnobservedLeadingCueBeyondTheSupportMarginIsNotProjected()
    {
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => time + 2800);
        proposals[0] = Unmatched(proposals[0]);

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(60), Token));
    }

    [Fact]
    public void ALongRunWithNoAnchorsCannotHideALocalEdit()
    {
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => time + 2800, count: 24);
        for (int i = 7; i < 15; i++)
            proposals[i] = Unmatched(proposals[i]);

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(110), Token));
    }

    [Fact]
    public void AnOutlierCannotBridgeARequiredGapInTheFinalMap()
    {
        SrtAlignmentProposal[] proposals = MakeProposals((i, time) => time + (i == 5 ? 4800 : 2800), count: 45);
        for (int i = 2; i < 10; i++)
            if (i != 5)
                proposals[i] = Unmatched(proposals[i]);

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(190), Token));
    }

    [Theory]
    [InlineData(SrtAlignmentRejection.ConflictingOrder)]
    [InlineData(SrtAlignmentRejection.InvalidTiming)]
    public void GlobalTimingDoesNotOverrideExplicitConflictingEvidence(SrtAlignmentRejection rejection)
    {
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => time + 2800);
        proposals[6] = Unmatched(proposals[6]) with { Rejection = rejection };

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(60), Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverlappingIntervalsOrDuplicateOrdinalsAreNotRewritten(bool badOrder)
    {
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => time + 2800);
        var cue = proposals[5].Cue;
        proposals[5] = proposals[5] with
        {
            Cue = badOrder ? cue with { Start = proposals[4].Cue.End.Subtract(TimeSpan.FromMilliseconds(100)) }
                : cue with { Id = proposals[4].Cue.Id }
        };

        Assert.Null(SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(60), Token));
    }

    [Fact]
    public void ACancelledFitDoesNotProduceIntervals()
    {
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => time + 2800);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            SrtSubtitleClock.TryCreate(proposals, TimeSpan.FromSeconds(60), new CancellationToken(canceled: true)));
    }

    static SrtAlignmentProposal Unmatched(SrtAlignmentProposal proposal) => proposal with
    {
        Start = null, End = null, IsExactAnchor = false, Coverage = 0, Rejection = SrtAlignmentRejection.Unmatched
    };

    static (SrtAlignmentProposal[] Proposals, TimeSpan Duration) PlaybackEdgeProposals(
        int firstOvershootMs, int lastOvershootMs)
    {
        SrtAlignmentProposal[] proposals = MakeProposals((_, time) => time - 3000 - firstOvershootMs);
        TimeSpan duration = proposals[^1].End!.Value.Subtract(TimeSpan.FromMilliseconds(lastOvershootMs));
        proposals[0] = proposals[0] with { Start = TimeSpan.Zero };
        proposals[^1] = proposals[^1] with { End = duration };
        return (proposals, duration);
    }

    static SrtAlignmentProposal[] MakeProposals(Func<int, int, int> map, int count = 12) => Enumerable.Range(0, count).Select(i =>
    {
        int start = 3000 + 4000 * i;
        int end = start + 1400;
        var cue = new SrtTimingCue(i, TimeSpan.FromMilliseconds(start), TimeSpan.FromMilliseconds(end), $"Spoken cue {i}");
        return new SrtAlignmentProposal(cue, TimeSpan.FromMilliseconds(map(i, start)), TimeSpan.FromMilliseconds(map(i, end)),
            1, SrtAlignmentRejection.None) { IsExactAnchor = true };
    }).ToArray();
}
