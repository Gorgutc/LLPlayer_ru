using FlyleafLib.MediaPlayer.AsrOmissions;

namespace FlyleafLibTests.MediaPlayer;

public sealed class AsrOmissionDetectorTests
{
    [Fact]
    public void Repairs_a_premature_cue_start_without_rewriting_A()
    {
        AsrOmissionCue left = Cue(Word("we", 0, 400), Word(" need", 400, 900), Word(" to", 900, 1600));
        AsrOmissionWord[] agreedSuffix = [Word(" these", 2850, 3000), Word(" useful", 3000, 3250), Word(" controls", 3250, 3650), Word(" now", 3650, 4000)];
        AsrOmissionCue next = Cue([Word("then", 1600, 1900, .01), Word(" select", 1900, 2850), .. agreedSuffix]);
        AsrOmissionCue donor = Cue([.. left.Words, Word(" check", 1600, 1950), Word(" the", 1950, 2150), Word(" panel", 2150, 2500),
            Word(" then", 2500, 2650), Word(" select", 2650, 2850), .. agreedSuffix]);
        var evidence = new AsrOmissionChunk(10_000, 5000, [left, next], [donor]);

        var candidate = Assert.Single(Scan(evidence));
        Assert.Equal(AsrOmissionKind.EarlyOnsetRepair, candidate.Kind);
        Assert.Equal("check the panel", candidate.Text);
        Assert.Equal(11_600, candidate.StartMs);
        Assert.Equal(12_500, candidate.EndMs);
        Assert.Equal(12_500, candidate.FollowingBaselineStartMs);
        Assert.Equal("then select these useful controls now", next.Text);
        Assert.Equal(1600, next.StartMs);
        Assert.Equal(4000, next.EndMs);

        var confident = Cue([Word("then", 1600, 1900), .. next.Words.Skip(1)]);
        Assert.Empty(Scan(evidence with { Baseline = [left, confident] }));
    }

    [Fact]
    public void Speech_gap_without_right_anchor_needs_local_C_and_preserves_the_B_literal()
    {
        AsrOmissionCue original = Cue(Word("一人", 500, 900), Word("だけ", 900, 1400));
        AsrOmissionCue donor = Cue(Word("ヘリオ", 5000, 5300, .60), Word("だよ", 5300, 6000, .68));
        var confirmation = new AsrOmissionConfirmation(4000,
            [Cue(Word("へりお", 1000, 1300), Word("だよ", 1300, 2000))]);
        var evidence = new AsrOmissionChunk(30_000, 10_000, [original], [donor], [confirmation], [new(4800, 6200)]);

        var candidate = Assert.Single(Scan(evidence));
        Assert.Equal(AsrOmissionKind.ContextConfirmedGap, candidate.Kind);
        Assert.False(candidate.IsReviewOnly);
        Assert.Equal("ヘリオだよ", candidate.Text);
        Assert.Equal(35_000, candidate.StartMs);
        Assert.Equal(36_000, candidate.EndMs);
        Assert.Empty(Scan(evidence with { Confirmations = null }));
        Assert.Empty(Scan(evidence with { VadIntervals = [] }));
        Assert.Empty(Scan(evidence with { Confirmations = [confirmation with { OffsetMs = 6500 }] }));
        Assert.Single(Scan(evidence with { Donor = [donor, donor] }));
    }

    [Fact]
    public void Candidate_inside_an_existing_display_interval_stays_separate_for_review()
    {
        AsrOmissionCue original = Cue(Word("yes", 500, 1000), Word(" I know", 7500, 8000));
        AsrOmissionCue donor = Cue(Word("just", 5000, 5300), Word(" one", 5300, 5650), Word(" moment", 5650, 6100));
        var confirmation = new AsrOmissionConfirmation(4000,
            [Cue(Word("just", 1000, 1300), Word(" one", 1300, 1650), Word(" moment", 1650, 2100))]);
        var evidence = new AsrOmissionChunk(0, 10_000, [original], [donor], [confirmation], [new(4500, 6500)]);
        var candidate = Assert.Single(Scan(evidence));
        Assert.Equal(AsrOmissionReviewReason.BaselineCueAlreadyOnScreen, candidate.ReviewOnlyReason);
        Assert.Null(candidate.FollowingBaselineStartMs);
        Assert.Equal("yes I know", original.Text);

        AsrOmissionCue occupied = Cue(Word("different words", 4800, 6200));
        Assert.Empty(Scan(evidence with { Baseline = [occupied] }));
    }

    static IReadOnlyList<AsrOmissionCandidate> Scan(AsrOmissionChunk chunk)
        => AsrOmissionDetector.Scan([chunk], TestContext.Current.CancellationToken);

    static AsrOmissionWord Word(string text, long start, long end, double probability = .99)
        => new(text, start, end, probability);

    static AsrOmissionCue Cue(params AsrOmissionWord[] words)
        => new(string.Concat(words.Select(word => word.Text)).Trim(), words[0].StartMs, words[^1].EndMs, words);
}
