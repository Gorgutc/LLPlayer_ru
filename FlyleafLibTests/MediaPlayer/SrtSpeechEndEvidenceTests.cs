namespace FlyleafLib.MediaPlayer;

public sealed class SrtSpeechEndEvidenceTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(40);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("the little city is still quiet tonight")]
    [InlineData("the little city it's quiet tonight")]
    public void ReadSpeechEndsAcceptsOneInteriorInsertionWithoutChangingTheSpokenEdges(string recognized)
    {
        WordTiming[] words = Spoken(8000, recognized);
        SrtTimingCue[] cues = [Cue(0, "the little city is quiet tonight")];

        var anchor = Assert.Single(Evidence(cues, words).Anchors);

        Assert.Equal(0, anchor.CueIndex);
        Assert.Equal(words[0].Start, anchor.Start);
        Assert.Equal(words[^1].End, anchor.End);
    }

    [Fact]
    public void ReadSpeechEndsDeclinesAnInsertedPhraseRepeatedElsewhereOnTheTrack()
    {
        WordTiming[] words =
        [
            .. Spoken(8000, "the little city is still quiet tonight"),
            .. Spoken(25000, "the little city is very quiet tonight")
        ];
        SrtTimingCue[] cues = [Cue(0, "the little city is quiet tonight")];

        Assert.Empty(Evidence(cues, words).Anchors);
    }

    [Theory]
    [InlineData("the city little is quiet tonight")]
    [InlineData("a little city is still quiet tonight")]
    [InlineData("the little city is still quiet today")]
    [InlineData("the little city is rather very quiet tonight")]
    public void ReadSpeechEndsDeclinesReorderedWordsChangedEdgesOrTwoInsertions(string recognized)
    {
        SrtTimingCue[] cues = [Cue(0, "the little city is quiet tonight")];

        Assert.Empty(Evidence(cues, Spoken(8000, recognized)).Anchors);
    }

    [Fact]
    public void ProjectedEdgeCanShareOneLiteralBoundaryWordOnlyNearItsSpeech()
    {
        SrtTimingCue[] cues = [Cue(0, "it hangs by the dresser"), Cue(1, "dresser next to your chair")];
        WordTiming[] words = Spoken(8000, "it hangs by the dresser next to your chair");
        var proposals = SrtAudioAlignment.Align(cues, words, Duration, Token);
        Assert.All(proposals, proposal => Assert.Equal(SrtAlignmentRejection.ConflictingOrder, proposal.Rejection));
        var track = SrtAudioAlignment.ReadSpeechEnds(proposals, words, Duration, Token);
        Assert.NotNull(track);

        var nearBoundary = TimeSpan.FromMilliseconds(9150);
        (TimeSpan Start, TimeSpan End)[] local = [(words[0].Start, nearBoundary), (nearBoundary, words[^1].End)];
        var distant = local.Select(interval =>
            (interval.Start.Add(TimeSpan.FromSeconds(10)), interval.End.Add(TimeSpan.FromSeconds(10)))).ToArray();
        Assert.True(track.CorroboratesEdge(0, 1, local, Token));
        Assert.False(track.CorroboratesEdge(0, 1, distant, Token));
    }

    [Fact]
    public void ProjectedEdgeRejectsAnActualCrossingOfTwoWords()
    {
        SrtTimingCue[] cues = [Cue(0, "it hangs by the old dresser"), Cue(1, "old dresser next to your chair")];
        WordTiming[] words = Spoken(8000, "it hangs by the old dresser next to your chair");
        var proposals = SrtAudioAlignment.Align(cues, words, Duration, Token);
        Assert.All(proposals, proposal => Assert.Equal(SrtAlignmentRejection.ConflictingOrder, proposal.Rejection));
        var track = SrtAudioAlignment.ReadSpeechEnds(proposals, words, Duration, Token);
        Assert.NotNull(track);

        var nearBoundary = TimeSpan.FromMilliseconds(9300);
        Assert.False(track.CorroboratesEdge(0, 1, [(words[0].Start, nearBoundary), (nearBoundary, words[^1].End)], Token));
    }

    [Fact]
    public void PoolingAllSpeechUnderOneCueCannotCorroborateAnEarlierSilentCue()
    {
        SrtTimingCue[] cues = [Cue(0, "alpha beta gamma pivot"), Cue(1, "pivot delta epsilon zeta")];
        WordTiming[] words = Spoken(4250, "alpha beta gamma pivot delta epsilon zeta");
        var track = Evidence(cues, words);
        var four = TimeSpan.FromSeconds(4);
        var five = TimeSpan.FromSeconds(5);
        var six = TimeSpan.FromSeconds(6);

        Assert.True(track.CorroboratesEdge(0, 1, [(four, five), (five, six)], Token));
        Assert.False(track.CorroboratesEdge(0, 1, [(TimeSpan.Zero, TimeSpan.FromSeconds(2)), (four, six)], Token));
    }

    [Theory]
    [InlineData("could you please answer the dresser", true)]
    [InlineData("could you please answer the phone", false)]
    public void RepeatChallengerRecognizesDressOffForDresserWithoutConfusingOtherEdges(string authored, bool expected)
    {
        WordTiming[] words = Spoken(8000, "could you please answer the dress off");

        Assert.Equal(expected, SrtAudioAlignment.HasRepeatChallenger(Cue(0, authored), words, Duration, Token));
    }

    private static SrtAudioAlignment.SpeechEndTrack Evidence(SrtTimingCue[] cues, WordTiming[] words)
    {
        var proposals = SrtAudioAlignment.Align(cues, words, Duration, Token);
        var track = SrtAudioAlignment.ReadSpeechEnds(proposals, words, Duration, Token);
        Assert.NotNull(track);
        return track;
    }

    private static SrtTimingCue Cue(int index, string text) =>
        new(index, TimeSpan.FromSeconds(2 * index + 1), TimeSpan.FromSeconds(2 * index + 2), text);

    private static WordTiming[] Spoken(int startMs, string words) => words.Split(' ').Select((word, i) =>
        new WordTiming(word, TimeSpan.FromMilliseconds(startMs + 250 * i),
            TimeSpan.FromMilliseconds(startMs + 250 * i + 200))).ToArray();
}
