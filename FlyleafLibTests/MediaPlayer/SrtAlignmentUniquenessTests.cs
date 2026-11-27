using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.MediaPlayer;

public sealed class SrtAlignmentUniquenessTests
{
    private const string Phrase = "the man took the dress";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(Phrase)]
    [InlineData("the man took the dresser")]
    [InlineData("the man unexpectedly took the dress")]
    public async Task ARepeatMissedByFullRecognitionBlocksTheSolitaryCandidate(string boundedSpeech)
    {
        List<SrtAlignmentAudioWindow> requests = [];
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(.64, 3.08), Sec(10), window =>
        {
            requests.Add(window);
            return Words(window.Start <= Sec(6.5) && window.End >= Sec(8)
                ? Spoken(6.5, boundedSpeech)
                : []);
        }, Token);

        Assert.Equal(SrtAlignmentRejection.Ambiguous, result);
        Assert.Contains(requests, range => range.Start <= Sec(6.5) && range.End >= Sec(8));
    }

    [Fact]
    public async Task AShortSolitaryCandidateRemainsAvailableWithNoDuplicateInItsComplement()
    {
        List<SrtAlignmentAudioWindow> requests = [];
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(1.5, 3), Sec(10), window =>
        {
            requests.Add(window);
            // A validator receiving obsolete/padded words must not count the original against itself.
            return Words(Spoken(1.5));
        }, Token);

        Assert.Equal(SrtAlignmentRejection.None, result);
        Assert.Contains(requests, range => range.Start == TimeSpan.Zero && range.End >= Sec(1.5));
        Assert.Contains(requests, range => range.Start <= Sec(3) && range.End == Sec(10));
    }

    [Fact]
    public async Task ASeparatePhraseWithAnUnrelatedBoundaryWordIsNotAChallenger()
    {
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(.64, 3.08), Sec(10), window =>
            Words(window.End >= Sec(8) ? Spoken(6.5, "the man took the phone") : []), Token);

        Assert.Equal(SrtAlignmentRejection.None, result);
    }

    [Theory]
    [InlineData(.5)]
    [InlineData(2.8)]
    public async Task ACompletePhraseWithItsMidpointOutsideTheCandidateCanContradictAStretchedBoundary(double at)
    {
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(1, 3), Sec(10), window =>
            Words(window.Start <= Sec(at) && window.End >= Sec(at + .7) ? Spoken(at, wordDuration: .14) : []), Token);

        Assert.Equal(SrtAlignmentRejection.Ambiguous, result);
    }

    [Fact]
    public async Task AShortCandidateWhollyIncludedInPaddingIsNotItsOwnChallenger()
    {
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(1, 1.2), Sec(10),
            _ => Words(Spoken(1, wordDuration: .04)), Token);

        Assert.Equal(SrtAlignmentRejection.None, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AStretchedFinalWordIsAuditedForAHiddenCompletePhrase(bool containsRepeat)
    {
        List<SrtAlignmentAudioWindow> requests = [];
        WordTiming[] primary =
        [
            new("the", Sec(4), Sec(4.2)), new("man", Sec(4.2), Sec(4.4)), new("took", Sec(4.4), Sec(4.7)),
            new("the", Sec(4.7), Sec(5)), new("dress", Sec(5), Sec(9))
        ];
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(4, 9), Sec(10), window =>
        {
            requests.Add(window);
            if (window.Start > Sec(6) || window.End < Sec(8))
                return Words([]);
            return Words(containsRepeat ? Spoken(6, wordDuration: .4) : [new("dress", Sec(6), Sec(8))]);
        }, Token, primary);

        Assert.Equal(containsRepeat ? SrtAlignmentRejection.Ambiguous : SrtAlignmentRejection.None, result);
        Assert.Contains(new SrtAlignmentAudioWindow(Sec(5), Sec(9)), requests);
    }

    [Fact]
    public async Task OrdinaryPrimaryWordsDoNotAddInteriorProbes()
    {
        List<SrtAlignmentAudioWindow> requests = [];
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(4, 9), Sec(10), window =>
        {
            requests.Add(window);
            return Words([]);
        }, Token, Spoken(4, wordDuration: 1));

        Assert.Equal(SrtAlignmentRejection.None, result);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task StretchedWordProbesCannotExceedTheVerificationBudget()
    {
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(0, 15), Sec(15),
            _ => throw new InvalidOperationException("The complete audit must fit within the window budget."),
            Token, Spoken(0, wordDuration: 3));

        Assert.Equal(SrtAlignmentRejection.InvalidTiming, result);
    }

    [Fact]
    public async Task SeveralMatchesInsideOneProbeAreAlsoAmbiguous()
    {
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(0, 1.5), Sec(10), window =>
            Words(window.End >= Sec(8) ? [.. Spoken(3), .. Spoken(6.5)] : []), Token);

        Assert.Equal(SrtAlignmentRejection.Ambiguous, result);
    }

    [Fact]
    public async Task ALongComplementUsesOverlappingWindowsWithoutDroppingItsTail()
    {
        List<SrtAlignmentAudioWindow> requests = [];
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(0, 2), Sec(70), window =>
        {
            requests.Add(window);
            return Words([]);
        }, Token);

        Assert.Equal(SrtAlignmentRejection.None, result);
        Assert.InRange(requests.Count, 3, 4);
        Assert.True(requests[0].Start <= Sec(2));
        Assert.Equal(Sec(70), requests[^1].End);
        Assert.All(requests, window => Assert.InRange(window.End - window.Start, TimeSpan.FromTicks(1), Sec(28)));
        for (int i = 1; i < requests.Count; i++)
            Assert.True(requests[i - 1].End - requests[i].Start >= Sec(2));
    }

    [Fact]
    public async Task UncoverableComplementDoesNotConfirmTheSolitaryCandidate()
    {
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(60, 62), Sec(150),
            _ => throw new InvalidOperationException("The complete complement must be planned before recognition."), Token);

        Assert.Equal(SrtAlignmentRejection.InvalidTiming, result);
    }

    [Fact]
    public async Task InvalidEvidenceIsNotProofOfUniqueness()
    {
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(0, 2), Sec(10),
            _ => throw new InvalidDataException("Incomplete recognition."), Token);

        Assert.Equal(SrtAlignmentRejection.InvalidTiming, result);
    }

    [Fact]
    public async Task InvalidFirstProbeDoesNotHideARepeatProvenByAnotherProbe()
    {
        var result = await SrtAlignmentUniqueness.VerifyAsync(Match(.64, 3.08), Sec(10), window =>
            window.Start == TimeSpan.Zero
                ? throw new InvalidDataException("Leading probe returned invalid evidence.")
                : Words(Spoken(6.5)), Token);

        Assert.Equal(SrtAlignmentRejection.Ambiguous, result);
    }

    [Fact]
    public async Task ARecognizerDrainFailureIsLeftForTheResourceOwner()
    {
        var failure = new EngineProcessDrainException("Engine still running.", new IOException("Stop timed out."),
            () => ValueTask.CompletedTask);

        Assert.Same(failure, await Assert.ThrowsAsync<EngineProcessDrainException>(() =>
            SrtAlignmentUniqueness.VerifyAsync(Match(0, 2), Sec(10), _ => throw failure, Token)));
    }

    [Fact]
    public async Task CancellationDoesNotConfirmUniqueness()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SrtAlignmentUniqueness.VerifyAsync(Match(1.5, 3), Sec(10), _ =>
            {
                cancellation.Cancel();
                return Words([]);
            }, cancellation.Token));
    }

    private static SrtAlignmentProposal Match(double start, double end) =>
        new(new SrtTimingCue(0, Sec(4), Sec(5.73), Phrase), Sec(start), Sec(end), 1, SrtAlignmentRejection.None)
        { IsExactAnchor = true };

    private static WordTiming[] Spoken(double start, string phrase = Phrase, double wordDuration = .3) =>
        phrase.Split(' ').Select((word, i) => new WordTiming(word, Sec(start + wordDuration * i),
            Sec(start + wordDuration * (i + 1)))).ToArray();

    private static Task<IReadOnlyList<WordTiming>> Words(WordTiming[] words) => Task.FromResult<IReadOnlyList<WordTiming>>(words);
    private static TimeSpan Sec(double seconds) => TimeSpan.FromSeconds(seconds);
}
