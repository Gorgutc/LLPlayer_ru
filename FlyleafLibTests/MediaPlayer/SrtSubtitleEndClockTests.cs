using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.MediaPlayer;

public sealed class SrtSubtitleEndClockTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ProvenLeadingBoundaryConflictAndWideOnsetsRetainOneCaptionClock()
    {
        var (proposals, speech) = CreateTrack(wideOnsets: true);
        proposals[0] = Absent(proposals[0], SrtAlignmentRejection.ConflictingOrder);
        proposals[1] = Absent(proposals[1], SrtAlignmentRejection.ConflictingOrder);
        var requests = new List<(int First, int Last, IReadOnlyList<(TimeSpan Start, TimeSpan End)> Intervals)>();

        var result = SrtSubtitleEndClock.TryCreate(proposals, speech[2..], (first, last, intervals) =>
        {
            requests.Add((first, last, intervals));
            return true;
        }, At(65_000), Token);

        Assert.NotNull(result);
        var request = Assert.Single(requests);
        Assert.Equal((0, 1), (request.First, request.Last));
        Assert.Same(result.Value.Intervals, request.Intervals);
        Assert.Equal((TimeSpan.Zero, At(2_000)), request.Intervals[0]);
        Assert.Equal((At(4_000), At(6_000)), request.Intervals[1]);
        Assert.Equal(14, result.Value.Summary.AnchorCount);
        Assert.Equal(Enumerable.Range(2, 14), result.Value.AnchorIndices.Order());
        Assert.Equal(proposals.Length, result.Value.Intervals.Count);
        for (int i = 0; i < proposals.Length; i++)
            Assert.Equal((At(i * 4_000), At(i * 4_000 + 2_000)), result.Value.Intervals[i]);
    }

    [Fact]
    public void EnlargedOnsetsRequireExplicitPrefixEvidence()
    {
        var (proposals, speech) = CreateTrack(wideOnsets: true);
        var withoutPrefix = speech.Select(evidence => evidence with { OnsetPrefixEnd = evidence.Start }).ToArray();

        Assert.Null(SrtSubtitleEndClock.TryCreate(proposals, withoutPrefix, (_, _, _) => true, At(65_000), Token));
    }

    [Theory]
    [InlineData(SrtAlignmentRejection.ConflictingOrder)]
    [InlineData(SrtAlignmentRejection.Ambiguous)]
    [InlineData(SrtAlignmentRejection.InvalidTiming)]
    public void InternalContradictionsPreventTrackCorrection(SrtAlignmentRejection rejection)
    {
        var (proposals, speech) = CreateTrack(wideOnsets: false);
        proposals[8] = Absent(proposals[8], rejection);
        var remaining = speech.Where(item => item.CueIndex != 8).ToArray();

        Assert.Null(SrtSubtitleEndClock.TryCreate(proposals, remaining, (_, _, _) => true, At(65_000), Token));
    }

    [Fact]
    public void AShiftedWholePhraseVetoesAnOtherwiseConsistentTrack()
    {
        var (proposals, speech) = CreateTrack(wideOnsets: false);
        speech[8] = speech[8] with
        {
            Start = speech[8].Start + At(2_000),
            OnsetPrefixEnd = speech[8].OnsetPrefixEnd + At(2_000),
            End = speech[8].End + At(2_000)
        };

        Assert.Null(SrtSubtitleEndClock.TryCreate(proposals, speech, (_, _, _) => true, At(65_000), Token));
    }

    [Theory]
    [InlineData(-2_000)]
    [InlineData(2_000)]
    public void ARecordedMatchOutsideTheFitCanVetoWholePhraseDisplacement(int displacement)
    {
        var (proposals, speech) = CreateTrack(wideOnsets: false);
        proposals[8] = proposals[8] with
        {
            Start = proposals[8].Start!.Value + At(displacement),
            End = proposals[8].End!.Value + At(displacement),
            IsExactAnchor = false
        };
        var remaining = speech.Where(anchor => anchor.CueIndex != 8).ToArray();

        Assert.Null(SrtSubtitleEndClock.TryCreate(proposals, remaining, (_, _, _) => true, At(65_000), Token));
    }

    [Fact]
    public void BeyondMarginAnUncorroboratedEdgeIsNotProjected()
    {
        var (proposals, speech) = CreateTrack(wideOnsets: false);
        proposals[^1] = Absent(proposals[^1], SrtAlignmentRejection.Unmatched);
        var requests = new List<(int First, int Last)>();

        var result = SrtSubtitleEndClock.TryCreate(proposals, speech[..^1], (first, last, _) =>
        {
            requests.Add((first, last));
            return false;
        }, At(65_000), Token);

        Assert.Null(result);
        Assert.Equal((15, 15), Assert.Single(requests));
    }

    private static (SrtAlignmentProposal[] Proposals, SrtSpeechEndEvidence[] Evidence) CreateTrack(bool wideOnsets)
    {
        var proposals = new SrtAlignmentProposal[16];
        var evidence = new SrtSpeechEndEvidence[16];
        for (int i = 0; i < evidence.Length; i++)
        {
            int start = i * 4_000;
            bool wide = wideOnsets && i % 2 == 1;
            var cue = new SrtTimingCue(i, At(start + 2_800), At(start + 4_800), $"distinct phrase {i}");
            var spokenStart = At(wide ? start - 1_200 : start);
            var end = At(start + 2_000);
            evidence[i] = new(i, spokenStart, wide ? At(start + 200) : spokenStart, end);
            proposals[i] = new(cue, spokenStart, end, 1, SrtAlignmentRejection.None) { IsExactAnchor = true };
        }
        return (proposals, evidence);
    }

    private static SrtAlignmentProposal Absent(SrtAlignmentProposal proposal, SrtAlignmentRejection why)
        => proposal with { Start = null, End = null, IsExactAnchor = false, Coverage = 0, Rejection = why };

    private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
}
