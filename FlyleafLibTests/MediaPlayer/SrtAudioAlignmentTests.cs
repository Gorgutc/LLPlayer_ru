using System.Linq;

namespace FlyleafLib.MediaPlayer;

public class SrtAudioAlignmentTests
{
    [Fact]
    public void Align_RecoversDifferentOffsetsAndKeepsOriginalCueIdentity()
    {
        SrtTimingCue[] cues =
        [
            Cue(5, 1, "Take the <i>first</i> step"),
            Cue(8, 8, "- [Louis] TURN toward the camera!"),
            Cue(9, 16, "Now choose a new brush")
        ];
        WordTiming[] words = [.. Spoken(3, "take the first step"), .. Spoken(11, "turn toward the camera"),
            .. Spoken(20, "now choose a new brush")];

        IReadOnlyList<SrtAlignmentProposal> result = Align(cues, words);

        Assert.Equal(cues, result.Select(proposal => proposal.Cue));
        Assert.Equal(new[] { Seconds(3), Seconds(11), Seconds(20) }, result.Select(proposal => proposal.Start!.Value));
        Assert.Equal(new[] { Seconds(4), Seconds(12), Seconds(21) }, result.Select(proposal => proposal.End!.Value));
        Assert.All(result, proposal => Assert.Equal(SrtAlignmentRejection.None, proposal.Rejection));
    }

    [Fact]
    public void Align_AcceptsOneMissingInteriorWordButRejectsMissingBoundary()
    {
        SrtTimingCue[] cues =
        [
            Cue(1, 0, "bring the blue wooden chair"),
            Cue(2, 3, "please open the tall window")
        ];
        WordTiming[] words = [.. Spoken(8, "bring the wooden chair"), .. Spoken(11, "open the tall window")];

        IReadOnlyList<SrtAlignmentProposal> result = Align(cues, words);

        Assert.Equal(Seconds(8), result[0].Start);
        Assert.Equal(Seconds(9), result[0].End);
        Assert.Equal(0.8, result[0].Coverage, precision: 6);
        Assert.Null(result[1].Start);
        Assert.Null(result[1].End);
    }

    [Theory]
    [InlineData("and two two people one", "and two people one")]
    [InlineData("the the bag his bag", "the bag his bag")]
    [InlineData("we put it back back", "we put it back")]
    public void Align_AnchorsOneOmittedAdjacentRepetition(string authored, string spoken)
    {
        SrtTimingCue cue = Cue(7, 3, authored);

        var proposal = Assert.Single(Align([cue], Spoken(8, spoken), allowElidedRepeat: true));

        Assert.Same(cue, proposal.Cue);
        Assert.Equal(Seconds(8), proposal.Start);
        Assert.Equal(Seconds(9), proposal.End);
        Assert.Equal(SrtAlignmentRejection.None, proposal.Rejection);
        Assert.True(proposal.IsExactAnchor);
        Assert.True(proposal.OneRepeatedWordElided);
        Assert.Equal(0.8, proposal.Coverage, precision: 6);

        var direct = Assert.Single(Align([cue], Spoken(8, spoken)));
        Assert.False(direct.IsExactAnchor);
        Assert.False(direct.OneRepeatedWordElided);
    }

    [Fact]
    public void Align_PrefersTheLiteralPhraseOverAnOmittedRepetition()
    {
        SrtTimingCue cue = Cue(1, 1, "the the bag his bag");
        WordTiming[] words = [.. Spoken(3, "the bag his bag"), .. Spoken(20, "the the bag his bag")];

        var proposal = Assert.Single(Align([cue], words, allowElidedRepeat: true));

        Assert.True(proposal.IsExactAnchor);
        Assert.False(proposal.OneRepeatedWordElided);
        Assert.Equal(Seconds(20), proposal.Start);
        Assert.Equal(1, proposal.Coverage);
    }

    [Theory]
    [InlineData("go go back", "go back")]
    [InlineData("and and two two people one", "and two people one")]
    [InlineData("and two two people one", "and two other one")]
    public void Align_DoesNotAnchorBroaderChanges(string authored, string spoken)
    {
        var proposal = Assert.Single(Align([Cue(1, 1, authored)], Spoken(8, spoken), allowElidedRepeat: true));

        Assert.False(proposal.IsExactAnchor);
    }

    [Fact]
    public void Align_DoesNotAnchorAnOmittedRepetitionAcrossARecordingWithTwoOccurrences()
    {
        SrtTimingCue cue = Cue(1, 1, "and two two people one");
        WordTiming[] words = [.. Spoken(3, "and two people one"), .. Spoken(20, "and two people one")];

        var proposal = Assert.Single(Align([cue], words, allowElidedRepeat: true));

        Assert.Equal(SrtAlignmentRejection.Ambiguous, proposal.Rejection);
        Assert.Null(proposal.Start);
        Assert.False(proposal.IsExactAnchor);
    }

    [Fact]
    public void Align_OmittedRepetitionCannotDisplaceLiteralAnchor()
    {
        SrtTimingCue[] cues = [Cue(0, 1, "and two two people one"), Cue(1, 3, "start with the house")];
        WordTiming[] words = [.. Spoken(8, "start with the house"), .. Spoken(10, "and two people one")];

        var proposals = Align(cues, words, allowElidedRepeat: true);

        Assert.Null(proposals[0].Start);
        Assert.Equal(Seconds(8), proposals[1].Start);
        Assert.True(proposals[1].IsExactAnchor);
    }

    [Fact]
    public void Align_MatchesInteriorVariationWithoutMovingBoundaries()
    {
        SrtTimingCue[] cues = [Cue(7, 1, "take the large blue cube now")];
        WordTiming[] words = Spoken(7, "take the blue large cube now");

        SrtAlignmentProposal result = Assert.Single(Align(cues, words));

        Assert.Equal(Seconds(7), result.Start);
        Assert.Equal(Seconds(8), result.End);
        Assert.Equal(5.0 / 6, result.Coverage, precision: 6);
    }

    [Fact]
    public void Align_TieLeavesRepeatedPhraseUnresolved()
    {
        SrtTimingCue[] cues = [Cue(1, 1, "Turn it over now")];
        WordTiming[] words = [.. Spoken(3, "turn it over now"), .. Spoken(20, "turn it over now")];

        SrtAlignmentProposal result = Assert.Single(Align(cues, words));

        Assert.Equal(SrtAlignmentRejection.Ambiguous, result.Rejection);
        Assert.Null(result.Start);
        Assert.Null(result.End);
    }

    [Fact]
    public void Align_BracketedOneWordUsesAnchorsEvenWhenRepeatedOutside()
    {
        SrtTimingCue[] cues =
        [
            Cue(0, 1, "begin with the handle"),
            Cue(1, 3, "[Louis] Next."),
            Cue(2, 5, "select the edge loop")
        ];
        WordTiming[] words = [.. Spoken(2, "next"), .. Spoken(10, "begin with the handle"),
            .. Spoken(12, "next"), .. Spoken(14, "select the edge loop"), .. Spoken(20, "next")];

        IReadOnlyList<SrtAlignmentProposal> result = Align(cues, words);

        Assert.Equal(Seconds(12), result[1].Start);
        Assert.Equal(Seconds(13), result[1].End);
        Assert.Null(Assert.Single(Align([cues[1]], Spoken(12, "next"))).Start);
    }

    [Fact]
    public void Align_UsesExactNeighboringAnchorsToConfirmAnEnglishSpellingVariationAtTheBoundary()
    {
        SrtTimingCue[] cues =
        [
            Cue(0, 1, "begin with the handle"),
            Cue(1, 3, "before we dive into modeling"),
            Cue(2, 5, "select the edge loop")
        ];
        WordTiming[] words = [.. Spoken(10, "begin with the handle"),
            .. Spoken(12, "before we dive into modelling"), .. Spoken(14, "select the edge loop")];

        SrtAlignmentProposal result = Align(cues, words)[1];

        Assert.Equal(Seconds(12), result.Start);
        Assert.Equal(Seconds(13), result.End);
        Assert.Equal(0.8, result.Coverage, precision: 6);
        Assert.Null(Assert.Single(Align([cues[1]], words)).Start);
    }

    [Fact]
    public void Align_RefusesCrossingMatches()
    {
        SrtTimingCue[] cues = [Cue(4, 1, "draw a square"), Cue(5, 3, "use the pencil")];
        WordTiming[] words = [.. Spoken(10, "use the pencil"), .. Spoken(12, "draw a square")];

        Assert.All(Align(cues, words), proposal =>
        {
            Assert.Equal(SrtAlignmentRejection.ConflictingOrder, proposal.Rejection);
            Assert.Null(proposal.Start);
        });
    }

    [Fact]
    public void Align_RefusesAdjacentIntervalsThatOverlapEvenThoughWordsDoNotRepeat()
    {
        SrtTimingCue[] cues = [Cue(4, 1, "draw a square"), Cue(5, 3, "use the pencil")];
        WordTiming[] words = [.. Spoken(10, "draw a square"), .. Spoken(10.9, "use the pencil")];

        Assert.All(Align(cues, words), proposal => Assert.Equal(SrtAlignmentRejection.ConflictingOrder, proposal.Rejection));
    }

    [Fact]
    public void Align_LeavesSilenceAndUnrelatedTextUntimed()
    {
        SrtTimingCue[] cues = [Cue(0, 1, "несколько слов по-русски"), Cue(1, 3, "[Music]")];

        Assert.All(Align(cues, Spoken(4, "a few English words")), proposal => Assert.Null(proposal.Start));
        Assert.All(Align(cues, []), proposal => Assert.Null(proposal.End));
    }

    [Fact]
    public void Align_IgnoresSubMillisecondOrOutOfRangeWords()
    {
        SrtTimingCue[] cues = [Cue(1, 3, "join the edges")];
        WordTiming[] words =
        [
            new("join", TimeSpan.FromTicks(100), TimeSpan.FromTicks(200)),
            new("the", TimeSpan.FromTicks(200), TimeSpan.FromTicks(400)),
            new("edges", TimeSpan.FromTicks(400), TimeSpan.FromTicks(800))
        ];

        Assert.Null(Assert.Single(Align(cues, words)).Start);
        Assert.Null(Assert.Single(SrtAudioAlignment.Align(cues, Spoken(18, "join the edges"), Seconds(18),
            TestContext.Current.CancellationToken)).Start);
    }

    [Fact]
    public void Align_DoesNotJoinMatchesAcrossLongSilence()
    {
        SrtTimingCue[] cues = [Cue(1, 1, "apply the material")];
        WordTiming[] words = [.. Spoken(3, "apply the"), .. Spoken(17, "material")];

        Assert.Null(Assert.Single(Align(cues, words)).Start);
    }

    [Fact]
    public void Align_IndexesLongRecordingsWithFrequentOpeningWords()
    {
        List<WordTiming> words = [];
        for (int i = 0; i < 10000; i++)
        {
            words.AddRange(Spoken(2 * i, i == 8000 ? "this is the irreplaceable passage" : "this is the introduction"));
        }

        IReadOnlyList<SrtAlignmentProposal> result = SrtAudioAlignment.Align(
            [Cue(0, 15, "this is the irreplaceable passage")], words, Seconds(20000), TestContext.Current.CancellationToken);

        Assert.Equal(Seconds(16000), Assert.Single(result).Start);
    }

    [Fact]
    public void Align_ObservesCancellation()
    {
        Assert.Throws<OperationCanceledException>(() =>
            SrtAudioAlignment.Align([Cue(0, 15, "some text")], Spoken(18, "some text"), Seconds(28), new(true)));
    }

    private static IReadOnlyList<SrtAlignmentProposal> Align(SrtTimingCue[] cues, WordTiming[] words,
        bool allowElidedRepeat = false)
        => allowElidedRepeat
            ? SrtAudioAlignment.Align(cues, words, Seconds(60), TestContext.Current.CancellationToken, allowElidedRepeat)
            : SrtAudioAlignment.Align(cues, words, Seconds(60), TestContext.Current.CancellationToken);

    private static SrtTimingCue Cue(int id, double start, string text) => new(id, Seconds(start), Seconds(start + 1), text);

    private static WordTiming[] Spoken(double start, string text)
    {
        string[] words = text.Split(' ');
        return words.Select((word, i) => new WordTiming(word,
            Seconds(start + (double)i / words.Length), Seconds(start + (double)(i + 1) / words.Length))).ToArray();
    }

    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);
}
