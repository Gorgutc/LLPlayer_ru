namespace FlyleafLib.MediaPlayer;

public sealed class FasterWhisperPhantomCandidatesTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("llplayer-phantom-native-");
    private string JsonPath => Path.Combine(directory.FullName, "native.json");

    [Fact]
    public void ReadMatchesNativeRowsInOrderAndRoundsOutwardToCompleteSamples()
    {
        File.WriteAllText(JsonPath, """
            {"segments":[
                {"text":"  Repeated phrase  ","start":0.10003125,"end":0.90001875,"no_speech_prob":0.4},
                {"text":"Repeated phrase","start":0.91,"end":1.0,"no_speech_prob":0.99}
            ]}
            """);

        var candidates = FasterWhisperPhantomCandidates.Read(JsonPath,
            [Cue("Repeated phrase", .101, .901), Cue("Repeated phrase", .91, 1)], 16_000);

        Assert.NotNull(candidates);
        Assert.Equal(
            [new AsrPhantomCandidate(1_600, 14_401, .4), new AsrPhantomCandidate(14_560, 16_000, .99)],
            candidates);
    }

    [Fact]
    public void ExactlyOneMillisecondOfRoundingIsAccepted()
    {
        File.WriteAllText(JsonPath, """
            {"segments":[{"text":"Cue","start":0.101,"end":0.901,"no_speech_prob":0}]}
            """);

        var candidates = FasterWhisperPhantomCandidates.Read(JsonPath, [Cue("Cue", .1, .9)], 16_000);

        Assert.NotNull(candidates);
        Assert.Equal(new AsrPhantomCandidate(1_616, 14_416, 0), Assert.Single(candidates));
    }

    [Fact]
    public void WordAlignmentMergesOverlappingSpansAndPinsTheNativeSampleClock()
    {
        File.WriteAllText(JsonPath, """
            {"segments":[{"text":"Cue","start":0.10004,"end":0.90002,"no_speech_prob":0.364,"words":[
                {"start":0.25,"end":0.45,"probability":0.7},
                {"start":0.2,"end":0.3,"probability":0.8},
                {"start":0.45,"end":0.5,"probability":0.6},
                {"start":0.8,"end":0.8,"probability":0.9600000001}
            ]}]}
            """);

        var candidates = FasterWhisperPhantomCandidates.Read(JsonPath, [Cue("Cue", .1, .9)], 16_000);

        var candidate = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AsrPhantomCandidate>>(candidates));
        Assert.Equal((1_600, 14_401, .364), (candidate.StartSample, candidate.EndSample, candidate.NoSpeechProbability));
        var alignment = Assert.IsType<AsrPhantomAlignment>(candidate.Alignment);
        Assert.Equal((4, 3, .79998m, .40002m, .375009m, .96m, .64m, -.68m), (alignment.WordCount,
            alignment.TimedWordCount, alignment.DurationSeconds, alignment.LargestGapSeconds,
            alignment.CoverageFraction, alignment.MaximumWordProbability, alignment.StartOffset, alignment.EndOffset));
        Assert.Equal([new AsrPhantomVoiceInterval(1_600, 6_400)], alignment.TimedSpans);
    }

    [Fact]
    public void NearestSampleDoesNotProvideMissingFractionalPadding()
    {
        File.WriteAllText(JsonPath, """
            {"segments":[{"text":"Cue","start":0.24999,"end":31,"no_speech_prob":0.364,"words":[
                {"start":0.25,"end":0.35,"probability":0.3},
                {"start":9,"end":9,"probability":0.3},
                {"start":20,"end":20,"probability":0.3}
            ]}]}
            """);

        short[] vocals = new short[32 * 16_000];
        var candidates = FasterWhisperPhantomCandidates.Read(JsonPath, [Cue("Cue", .25, 31)], vocals.Length);
        var candidate = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AsrPhantomCandidate>>(candidates));
        var silent = new AsrPhantomVoice(vocals.Length, []);

        Assert.True(AsrPhantomAlignmentEvidence.IsLongCandidate(candidate));
        Assert.Equal(AsrPhantomReason.AlignmentUnverifiedPaddedWindow,
            AsrPhantomAlignmentEvidence.EvaluateLong(vocals, candidate, new(silent, silent, silent),
                samples => new AsrPhantomVoice(samples.Length, [])).Reason);
    }

    [Fact]
    public void AllZeroLengthWordTimingsCannotProvideAWordClock()
    {
        File.WriteAllText(JsonPath, """
            {"segments":[{"text":"Cue","start":0.1,"end":0.9,"no_speech_prob":0.4,"words":[
                {"start":0.2,"end":0.2,"probability":0.3},
                {"start":0.8,"end":0.8,"probability":0.4}
            ]}]}
            """);

        var candidates = FasterWhisperPhantomCandidates.Read(JsonPath, [Cue("Cue", .1, .9)], 16_000);

        var candidate = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AsrPhantomCandidate>>(candidates));
        var alignment = Assert.IsType<AsrPhantomAlignment>(candidate.Alignment);
        Assert.Equal((2, 0, .8m, .8m, 0m, .4m), (alignment.WordCount, alignment.TimedWordCount,
            alignment.DurationSeconds, alignment.LargestGapSeconds, alignment.CoverageFraction,
            alignment.MaximumWordProbability));
        Assert.Empty(alignment.TimedSpans);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{\"start\":0.3,\"end\":0.4}]")]
    [InlineData("[{\"start\":0.3,\"end\":0.4,\"probability\":\"0.9\"}]")]
    [InlineData("[{\"start\":0.3,\"end\":0.4,\"probability\":-0.01}]")]
    [InlineData("[{\"start\":0.3,\"end\":0.4,\"probability\":1.01}]")]
    [InlineData("[{\"start\":0.3,\"end\":0.4,\"probability\":1e999}]")]
    [InlineData("[{\"start\":0.09998,\"end\":0.4,\"probability\":0.9}]")]
    [InlineData("[{\"start\":0.3,\"end\":0.90002,\"probability\":0.9}]")]
    [InlineData("[{\"start\":0.4,\"end\":0.399999,\"probability\":0.9}]")]
    [InlineData("[{\"start\":0.3,\"end\":0.4,\"probability\":0.9},{\"start\":0.4,\"end\":0.5}]")]
    public void IncompleteOrInvalidWordMetadataKeepsOnlyTheNativeCueIdentity(string words)
    {
        File.WriteAllText(JsonPath, $$"""
            {"segments":[{"text":"Cue","start":0.1,"end":0.9,"no_speech_prob":0.4,"words":{{words}}}]}
            """);

        var candidates = FasterWhisperPhantomCandidates.Read(JsonPath, [Cue("Cue", .1, .9)], 16_000);

        Assert.NotNull(candidates);
        Assert.Equal(new AsrPhantomCandidate(1_600, 14_400, .4), Assert.Single(candidates));
    }

    [Theory]
    [InlineData("{broken JSON")]
    [InlineData("null")]
    [InlineData("{\"segments\":{}}")]
    [InlineData("{\"segments\":[]}")]
    [InlineData("{\"segments\":[null]}")]
    [InlineData("{\"segments\":[{\"text\":\"cue\",\"start\":0.1,\"end\":0.9,\"no_speech_prob\":0.7}]}")]
    [InlineData("{\"segments\":[{\"text\":\"Cue\",\"start\":0.10101,\"end\":0.9,\"no_speech_prob\":0.7}]}")]
    [InlineData("{\"segments\":[{\"text\":\"Cue\",\"start\":0.1,\"end\":0.89899,\"no_speech_prob\":0.7}]}")]
    [InlineData("{\"segments\":[{\"text\":\"Cue\",\"start\":0.1,\"end\":1e999,\"no_speech_prob\":0.7}]}")]
    [InlineData("{\"segments\":[{\"text\":\"Cue\",\"start\":0.1,\"end\":0.9}]}")]
    [InlineData("{\"segments\":[{\"text\":\"Cue\",\"start\":0.1,\"end\":0.9,\"no_speech_prob\":null}]}")]
    [InlineData("{\"segments\":[{\"text\":\"Cue\",\"start\":0.1,\"end\":0.9,\"no_speech_prob\":-0.01}]}")]
    [InlineData("{\"segments\":[{\"text\":\"Cue\",\"start\":0.1,\"end\":0.9,\"no_speech_prob\":1.01}]}")]
    [InlineData("{\"segments\":[{\"text\":\"Cue\",\"start\":0.1,\"end\":0.9,\"no_speech_prob\":1e999}]}")]
    [InlineData("{\"segments\":[{\"text\":\"Cue\",\"start\":0.1,\"end\":0.9,\"no_speech_prob\":0.2,\"no_speech_prob\":0.7}]}")]
    public void InvalidOrMissingNativeIdentityNeverAuthorizesAnyCandidate(string json)
    {
        File.WriteAllText(JsonPath, json);

        Assert.Null(FasterWhisperPhantomCandidates.Read(JsonPath, [Cue("Cue", .1, .9)], 16_000));
    }

    [Theory]
    [InlineData(-0.00001, 0.5)]
    [InlineData(0.5, 1.00001)]
    [InlineData(0.5, 0.5)]
    public void NativeBoundsMustFitExactPcmEvenWhenTheRoundedStdoutClockFits(double start, double end)
    {
        File.WriteAllText(JsonPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            segments = new[] { new { text = "Cue", start, end, no_speech_prob = .8 } }
        }));
        var rounded = Cue("Cue", Math.Max(0, start), Math.Min(1, end));

        Assert.Null(FasterWhisperPhantomCandidates.Read(JsonPath, [rounded], 16_000));
    }

    [Fact]
    public void BadSecondCueInvalidatesEntireCandidateMapping()
    {
        File.WriteAllText(JsonPath, """
            {"segments":[
                {"text":"First","start":0.1,"end":0.5,"no_speech_prob":0.9},
                {"text":"Mismatched","start":0.6,"end":0.9,"no_speech_prob":0.9}
            ]}
            """);

        Assert.Null(FasterWhisperPhantomCandidates.Read(JsonPath,
            [Cue("First", .1, .5), Cue("Second", .6, .9)], 16_000));
    }

    [Fact]
    public void DuplicateIdentitiesCannotEstablishWhichScoreBelongsToWhichCue()
    {
        File.WriteAllText(JsonPath, """
            {"segments":[
                {"text":"Cue","start":0.1,"end":0.9,"no_speech_prob":0.1},
                {"text":"Cue","start":0.1,"end":0.9,"no_speech_prob":0.9}
            ]}
            """);

        Assert.Null(FasterWhisperPhantomCandidates.Read(JsonPath,
            [Cue("Cue", .1, .9), Cue("Cue", .1, .9)], 16_000));
        Assert.Null(FasterWhisperPhantomCandidates.Read(JsonPath,
            [Cue("Cue", .1, .9), Cue("Cue", .101, .9)], 16_000));
    }

    [Fact]
    public void MissingNativeFileDoesNotProvideCandidates()
    {
        Assert.Null(FasterWhisperPhantomCandidates.Read(JsonPath, [Cue("Cue", .1, .9)], 16_000));
    }

    private static (string text, TimeSpan start, TimeSpan end, string language) Cue(string text, double start, double end)
        => (text, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), "en");

    public void Dispose()
    {
        File.Delete(JsonPath);
        directory.Delete();
    }
}
