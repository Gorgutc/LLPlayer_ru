using FlyleafLib.MediaPlayer;

namespace FlyleafLibTests.MediaPlayer;

public sealed class AsrPhantomAnalysisTests
{
    private const int Rate = 16_000;
    private static readonly AsrPhantomCandidate Interior = new(Rate, 3 * Rate, .65);
    private static readonly AsrPhantomCandidate Edge = new(0, Rate, .65);

    [Fact]
    public async Task Original_voice_stops_separation_and_keeps_native_order()
    {
        short[] center = Alternating(4 * Rate, 1400);
        int measured = 0, separated = 0;
        using var analysis = new AsrPhantomAnalysis((pcm, _) =>
        {
            measured++;
            Assert.True(pcm.Span.SequenceEqual(center));
            return new(pcm.Length, [new(Rate, 2 * Rate)]);
        }, (_, _) =>
        {
            separated++;
            return Task.FromResult<short[]?>(null);
        });

        AsrPhantomDecision[] results = await analysis.AnalyzeAsync(center,
            [null, Interior, Interior with { NoSpeechProbability = .2 }], default, default, false, false,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Length);
        Assert.Equal(AsrPhantomReason.VoiceSupport, results[1].Reason);
        Assert.All(results, result => Assert.Equal(AsrPhantomAction.Keep, result.Action));
        Assert.Equal(1, measured);
        Assert.Equal(0, separated);
    }

    [Fact]
    public async Task Exact_adjacent_samples_enable_edge_only_after_the_center_veto()
    {
        short[] center = Alternating(4 * Rate, 1400);
        short[] left = Alternating(2 * Rate, 1200);
        short[] right = Alternating(2 * Rate, 1500);
        short[] extended = [.. left, .. center, .. right];
        List<short[]> separated = [];
        using var analysis = new AsrPhantomAnalysis((pcm, _) => new(pcm.Length, []), (pcm, _) =>
        {
            separated.Add(pcm.ToArray());
            return Task.FromResult<short[]?>(Alternating(pcm.Length, 1));
        });

        AsrPhantomDecision[] noHalo = await analysis.AnalyzeAsync(center, [Edge], default, right,
            false, false, TestContext.Current.CancellationToken);
        Assert.Equal(AsrPhantomReason.UnavailableContext, Assert.Single(noHalo).Reason);
        Assert.Empty(separated);

        AsrPhantomDecision[] both = await analysis.AnalyzeAsync(center, [Edge, Interior], left, right,
            false, false, TestContext.Current.CancellationToken);

        Assert.All(both, result => Assert.Equal(AsrPhantomAction.Drop, result.Action));
        Assert.Equal(2, separated.Count);
        Assert.Contains(separated, pcm => pcm.AsSpan().SequenceEqual(center));
        Assert.Contains(separated, pcm => pcm.AsSpan().SequenceEqual(extended));
    }

    [Fact]
    public async Task Mismatched_separator_output_keeps_A_and_warns_once_for_the_run()
    {
        short[] center = Alternating(4 * Rate, 1400);
        List<string> warnings = [];
        using var analysis = new AsrPhantomAnalysis((pcm, _) => new(pcm.Length, []),
            (pcm, _) => Task.FromResult<short[]?>(new short[pcm.Length - 1]), warnings.Add);

        for (int i = 0; i < 2; i++)
        {
            AsrPhantomDecision[] results = await analysis.AnalyzeAsync(center, [Interior], default, default,
                false, false, TestContext.Current.CancellationToken);

            Assert.NotEqual(AsrPhantomAction.Drop, Assert.Single(results).Action);
        }
        Assert.Single(warnings);
    }

    [Fact]
    public async Task Cancellation_from_the_separator_never_emits_a_decision()
    {
        short[] center = Alternating(4 * Rate, 1400);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var analysis = new AsrPhantomAnalysis((pcm, _) => new(pcm.Length, []), (_, token) =>
        {
            Assert.Equal(canceled.Token, token);
            canceled.Cancel();
            return Task.FromCanceled<short[]?>(token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis.AnalyzeAsync(center, [Interior],
            default, default, false, false, canceled.Token));
    }

    [Fact]
    public async Task Long_alignment_drop_can_only_authorize_the_immediately_following_cue_in_the_chunk()
    {
        short[] center = Alternating(48 * Rate, 1400);
        var longCue = new AsrPhantomCandidate(4 * Rate, 40 * Rate, .364,
            new(6, 1, 36m, 35m, .025m, .35m, [new(0, Rate)]));
        var shortCue = new AsrPhantomCandidate(40 * Rate + 800, 41 * Rate, .71,
            new(6, 1, .95m, .4m, .5m, .99m, [new(0, 8000)]));
        int separated = 0;
        using var analysis = new AsrPhantomAnalysis((pcm, _) => new(pcm.Length, []), (pcm, _) =>
        {
            separated++;
            short[] vocals = new short[pcm.Length];
            // Residual DC exceeds v5's negligible RMS bound but does not prove audible short-cue speech.
            vocals.AsSpan().Fill(8);
            return Task.FromResult<short[]?>(vocals);
        });
        CancellationToken token = TestContext.Current.CancellationToken;

        AsrPhantomDecision[] adjacent = await analysis.AnalyzeAsync(center,
            [longCue, shortCue], default, default, false, false, token);
        AsrPhantomDecision[] separatedByCue = await analysis.AnalyzeAsync(center,
            [longCue, null, shortCue], default, default, false, false, token);

        Assert.Equal(AsrPhantomReason.InvalidLongAlignment, adjacent[0].Reason);
        Assert.Equal(AsrPhantomReason.CollapsedAdjacentAlignment, adjacent[1].Reason);
        Assert.Equal(AsrPhantomAction.Drop, separatedByCue[0].Action);
        Assert.NotEqual(AsrPhantomAction.Drop, separatedByCue[2].Action);
        Assert.Equal(2, separated);
    }

    [Fact]
    public async Task Unconfirmed_process_stop_is_propagated_to_the_owner_of_the_lease()
    {
        short[] center = Alternating(4 * Rate, 1400);
        var failure = new EngineProcessDrainException("unconfirmed", new IOException("fixture"), () => default);
        using var analysis = new AsrPhantomAnalysis((pcm, _) => new(pcm.Length, []),
            (_, _) => Task.FromException<short[]?>(failure));

        Assert.Same(failure, await Assert.ThrowsAsync<EngineProcessDrainException>(() => analysis.AnalyzeAsync(center,
            [Interior], default, default, false, false, TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Right_halo_fills_the_detector_tail_without_overriding_observed_center_speech(
        bool centerSpeech, bool lostFullWindow)
    {
        short[] center = Alternating(48 * Rate + 17, 1400);
        short[] right = Alternating(2 * Rate, 1200);
        var cue = new AsrPhantomCandidate(8 * Rate, center.Length, .364,
            new(6, 1, 40.0010625m, 39m, .025m, .35m, [new(0, Rate)]));
        var observed = new AsrPhantomVoiceInterval(24 * Rate, 25 * Rate);
        int separated = 0;
        using var analysis = new AsrPhantomAnalysis((pcm, _) => new(pcm.Length / 512 * 512 -
            (lostFullWindow && pcm.Length == center.Length ? 512 : 0),
            centerSpeech && pcm.Length == center.Length ? [observed] : []), (pcm, _) =>
        {
            separated++;
            return Task.FromResult<short[]?>(new short[pcm.Length]);
        });

        AsrPhantomDecision[] decisions = await analysis.AnalyzeAsync(center, [cue], default, right, false, false,
            TestContext.Current.CancellationToken);

        bool preserve = centerSpeech || lostFullWindow;
        Assert.Equal(preserve ? AsrPhantomAction.Keep : AsrPhantomAction.Drop, Assert.Single(decisions).Action);
        Assert.Equal(preserve ? 0 : 2, separated);
    }

    private static short[] Alternating(int size, short amplitude)
    {
        short[] pcm = new short[size];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (short)((i & 1) == 0 ? -amplitude : amplitude);
        return pcm;
    }
}
