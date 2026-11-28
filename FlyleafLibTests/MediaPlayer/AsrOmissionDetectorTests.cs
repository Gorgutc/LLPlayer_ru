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

    [Theory]
    [InlineData(1060, .32, "the panels, the camera and the light.", 1300, 5, true)]
    [InlineData(1060, .99, "objects, the panels, the camera and the light.", 1100, 4, true)]
    [InlineData(1100, .32, "objects, the panels, the camera and the light.", 1100, 4, false)]
    public void Trims_a_boundary_echo_but_preserves_a_separately_timed_repeat(
        int repeatStart, double repeatProbability, string expected, int start, int donorStart, bool reviewOnly)
    {
        AsrOmissionCue left = Cue(Word("store", 0, 200), Word(" these", 200, 400),
            Word(" useful", 400, 700), Word(" objects,", 700, 1100));
        AsrOmissionWord[] rightAnchor = [Word(" we", 4000, 4250), Word(" can", 4250, 4500),
            Word(" start", 4500, 4800), Word(" work", 4800, 5200)];
        AsrOmissionCue right = Cue([Word("Now,", 1100, 1300), .. rightAnchor]);
        AsrOmissionCue donor = Cue([.. left.Words.Take(3), Word(" objects.", 700, repeatStart),
            Word(" objects,", repeatStart, 1300, repeatProbability), Word(" the", 1300, 1550), Word(" panels,", 1550, 1900),
            Word(" the", 1900, 2150), Word(" camera", 2150, 2830), Word(" and", 2830, 3140),
            Word(" the", 3140, 3330), Word(" light.", 3330, 3600), Word(" Now,", 3700, 4000), .. rightAnchor]);

        var candidate = Assert.Single(Scan(new(10_000, 6000, [left, right], [donor])));

        Assert.Equal(expected, candidate.Text);
        Assert.Equal(10_000 + start, candidate.StartMs);
        Assert.Equal(13_600, candidate.EndMs);
        Assert.Equal(donorStart, candidate.DonorWordStartIndex);
        Assert.Equal(12, candidate.DonorWordEndIndex);
        Assert.Equal(reviewOnly ? AsrOmissionReviewReason.BoundaryRepeatUncertain : null, candidate.ReviewOnlyReason);
        Assert.Equal(13_700, candidate.FollowingBaselineStartMs);
        Assert.Equal("store these useful objects,", left.Text);
        Assert.Equal(1100, left.EndMs);
        Assert.Equal("Now, we can start work", right.Text);
        Assert.Equal(1100, right.StartMs);
    }

    [Fact]
    public void Trims_an_echo_of_the_following_anchor_at_the_end_of_a_gap()
    {
        AsrOmissionCue left = Cue(Word("store", 0, 200), Word(" the", 200, 500), Word(" objects.", 500, 1100));
        AsrOmissionCue right = Cue(Word("Now,", 3710, 4000), Word(" we", 4000, 4250), Word(" can", 4250, 4500),
            Word(" start", 4500, 4800), Word(" work", 4800, 5200));
        AsrOmissionCue donor = Cue([.. left.Words,
            Word(" Check", 1100, 1500), Word(" the", 1500, 1800), Word(" panel,", 1800, 2300),
            Word(" the", 2300, 2550), Word(" camera", 2550, 2850), Word(" and", 2850, 3100),
            Word(" light.", 3100, 3500), Word(" Now.", 3500, 3730, .33), Word(" Now,", 3730, 4000), .. right.Words.Skip(1)]);

        var candidate = Assert.Single(Scan(new(0, 6000, [left, right], [donor])));

        Assert.Equal("Check the panel, the camera and light.", candidate.Text);
        Assert.Equal(1100, candidate.StartMs);
        Assert.Equal(3500, candidate.EndMs);
        Assert.Equal(3, candidate.DonorWordStartIndex);
        Assert.Equal(10, candidate.DonorWordEndIndex);
        Assert.Equal(3710, candidate.FollowingBaselineStartMs);
        Assert.Equal(AsrOmissionReviewReason.BoundaryRepeatUncertain, candidate.ReviewOnlyReason);
    }

    static IReadOnlyList<AsrOmissionCandidate> Scan(AsrOmissionChunk chunk)
        => AsrOmissionDetector.Scan([chunk], TestContext.Current.CancellationToken);

    static AsrOmissionWord Word(string text, long start, long end, double probability = .99)
        => new(text, start, end, probability);

    static AsrOmissionCue Cue(params AsrOmissionWord[] words)
        => new(string.Concat(words.Select(word => word.Text)).Trim(), words[0].StartMs, words[^1].EndMs, words);
}
