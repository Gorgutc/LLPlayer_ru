using FlyleafLib.MediaPlayer;

namespace FlyleafLibTests.MediaPlayer;

public sealed class AsrPhantomAlignmentEvidenceTests
{
    private const int Rate = 16_000;
    private const int Second = Rate;
    private const int Pad = Rate / 4;
    private static readonly AsrPhantomCandidate Long = new(4 * Second, 40 * Second, .364,
        new(6, 1, 36m, 35m, .01m, .35m, [new(0, Second)]));
    private static readonly AsrPhantomCandidate Short = new(40 * Second + Pad, 41 * Second, .65,
        new(3, 1, .75m, .7m, .07m, .6m, [new(0, 800)]));

    [Fact]
    public void LongNominationUsesGapSizeAndWordCoverageWithoutMinimumNoSpeechScore()
    {
        Assert.True(AsrPhantomAlignmentEvidence.IsLongCandidate(Long));
        Assert.True(AsrPhantomAlignmentEvidence.IsLongCandidate(Long with
        {
            Alignment = Long.Alignment!.Value with { DurationSeconds = 70, LargestGapSeconds = 30 }
        }));
        Assert.False(AsrPhantomAlignmentEvidence.IsLongCandidate(Long with
        {
            Alignment = Long.Alignment!.Value with { MaximumWordProbability = .5m }
        }));
        Assert.False(AsrPhantomAlignmentEvidence.IsLongCandidate(Long with { Alignment = null }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EitherRawVoiceViewVetoesAndDoesNotRunNormalization(bool original)
    {
        short[] vocals = new short[48 * Second];
        var voice = Voice(vocals.Length, new AsrPhantomVoiceInterval(5 * Second, 6 * Second));
        var silent = Voice(vocals.Length);
        var views = original ? new AsrPhantomVoiceViews(voice, silent, silent) : new(silent, voice, silent);

        Assert.Equal(AsrPhantomReason.AlignmentVoiceSupport, AsrPhantomAlignmentEvidence.EvaluateLong(vocals, Long,
            views, _ => throw new InvalidOperationException("A raw voice veto must short circuit")).Reason);
    }

    [Fact]
    public void MissingPadOrGappedLocalCoverageKeepsCue()
    {
        short[] vocals = new short[Long.EndSample + Pad + Second];
        AsrPhantomVoiceViews views = SilentViews(vocals.Length);
        Assert.Equal(AsrPhantomReason.AlignmentUnverifiedPaddedWindow, AsrPhantomAlignmentEvidence.EvaluateLong(
            vocals.AsSpan(0, Long.EndSample + Pad - 1), Long, views, Silence).Reason);
        Assert.Equal(AsrPhantomAction.Review, AsrPhantomAlignmentEvidence.EvaluateLong(vocals, Long, views,
            samples => Voice(Math.Min(samples.Length, Second))).Action);
    }

    [Fact]
    public void SilentIndependentWindowsUseExactPhaseOffsetsAndDrop()
    {
        short[] vocals = new short[48 * Second];
        foreach (int offset in new[] { 0, 28_000, 60_000, 64_000, 92_000, 128_000 })
            vocals[offset + 1] = 1;
        var calls = new List<(int Length, short SecondSample)>();

        var result = AsrPhantomAlignmentEvidence.EvaluateLong(vocals, Long, SilentViews(vocals.Length), chunk =>
        {
            calls.Add((chunk.Length, chunk[1]));
            return Silence(chunk);
        });

        Assert.Equal(AsrPhantomAction.Drop, result.Action);
        Assert.Equal(21, calls.Count);
        Assert.All(new[] { 0, 1, 10, 11, 12 }, i => Assert.Equal((8 * Second, (short)16423), calls[i]));
    }

    [Fact]
    public void OffClockWholeVoteIsTestedInFocusedWindowAndDropsOnlyWithoutCorroboration()
    {
        short[] vocals = new short[48 * Second];
        var silent = Voice(vocals.Length);
        var views = new AsrPhantomVoiceViews(silent, silent, Voice(vocals.Length,
            new AsrPhantomVoiceInterval(22 * Second, 23 * Second)));
        const int focusStart = 18 * Second + Second / 2;
        vocals[focusStart + 1] = 1;
        var calls = new List<short>();

        AsrPhantomDecision Evaluate(bool repeat, bool full) => AsrPhantomAlignmentEvidence.EvaluateLong(
            vocals, Long, views, chunk =>
            {
                calls.Add(chunk[1]);
                if (chunk[1] == 0)
                    return Silence(chunk);
                if (!full)
                    return Voice(4 * Second);
                return repeat
                    ? Voice(chunk.Length, new AsrPhantomVoiceInterval(3 * Second + Second / 2, 4 * Second))
                    : Silence(chunk);
            });

        Assert.Equal(AsrPhantomAction.Drop, Evaluate(repeat: false, full: true).Action);
        Assert.Equal(22, calls.Count);
        Assert.Equal(16423, calls[^1]);
        Assert.Equal(AsrPhantomReason.AlignmentNormalizationConsensus, Evaluate(repeat: true, full: true).Reason);
        Assert.Equal(AsrPhantomReason.AlignmentLocalCoverageMissing, Evaluate(repeat: true, full: false).Reason);
    }

    [Fact]
    public void ALocalVoteNearProjectedNativeWordsVetoesWithoutConsensus()
    {
        short[] vocals = new short[48 * Second];
        var projected = Long with { StartSample = Long.StartSample + Second, EndSample = Long.EndSample + Second };
        int firstWindowStart = projected.StartSample - Pad - 2 * Second;
        vocals[firstWindowStart + 1] = 1;

        var result = AsrPhantomAlignmentEvidence.EvaluateLong(vocals, projected, SilentViews(vocals.Length),
            chunk => chunk[1] == 16423
                ? Voice(chunk.Length, new AsrPhantomVoiceInterval(5 * Second, 5 * Second + Pad)) : Silence(chunk));

        Assert.Equal(AsrPhantomReason.AlignmentVoiceNearWordClock, result.Reason);
    }

    [Fact]
    public void CollapsedShortRequiresTheRejectedLongAnchorAndSubUnitLocalResidual()
    {
        short[] vocals = new short[48 * Second];
        var views = SilentViews(vocals.Length);
        Assert.False(AsrPhantomAlignmentEvidence.IsShortCandidate(vocals, Short, null));
        Assert.False(AsrPhantomAlignmentEvidence.IsShortCandidate(vocals, Short, Long with
        {
            Alignment = Long.Alignment!.Value with { DurationSeconds = 72 }
        }));
        Assert.False(AsrPhantomAlignmentEvidence.IsShortCandidate(vocals,
            Short with { StartSample = Short.StartSample + 1 }, Long));
        Assert.True(AsrPhantomAlignmentEvidence.IsShortCandidate(vocals, Short, Long));
        Assert.Equal(AsrPhantomReason.CollapsedAdjacentAlignment,
            AsrPhantomAlignmentEvidence.EvaluateShort(vocals, Short, views, Silence).Reason);
        var confirmedVoice = AsrPhantomAlignmentEvidence.EvaluateShort(vocals, Short, views,
            chunk => Voice(chunk.Length, new AsrPhantomVoiceInterval(2 * Second + Pad, 2 * Second + 2 * Pad)));
        Assert.Equal(AsrPhantomReason.AlignmentLocalVoiceSupport, confirmedVoice.Reason);
        for (int sample = Short.StartSample; sample < Short.EndSample; sample++)
            vocals[sample] = (short)(sample % 2 == 0 ? 2 : -2);
        Assert.False(AsrPhantomAlignmentEvidence.IsShortCandidate(vocals, Short, Long));
    }

    private static AsrPhantomVoiceViews SilentViews(int length)
        => new(Voice(length), Voice(length), Voice(length));

    private static AsrPhantomVoice? Silence(short[] samples) => Voice(samples.Length);

    private static AsrPhantomVoice Voice(int covered, params AsrPhantomVoiceInterval[] intervals)
        => new(covered, intervals);
}
