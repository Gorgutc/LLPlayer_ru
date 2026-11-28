using FlyleafLib.MediaPlayer;

namespace FlyleafLibTests.MediaPlayer;

public sealed class AsrPhantomFilterTests
{
    private const int Rate = AsrPhantomFilter.SampleRate;
    private const int Count = 4 * Rate;
    private static readonly AsrPhantomCandidate Middle = new(Rate, 3 * Rate, 0.64);
    private static readonly AsrPhantomVoice Empty = new(Count, []);
    private static readonly AsrPhantomVoiceViews EmptyViews = new(Empty, Empty, Empty);
    private static readonly AsrPhantomDecision MissingContext = new(AsrPhantomAction.Keep, AsrPhantomReason.UnavailableContext);

    [Fact]
    public void Aligned_views_drop_a_music_candidate_only_with_negligible_vocal_energy()
    {
        var mixture = Alternating(Count, -1450, 1450);
        var vocal = Alternating(Count, -1, 1);
        Assert.Equal(new(AsrPhantomAction.Drop, AsrPhantomReason.MusicWithoutVocalSignal),
            Evaluate(mixture, vocal, Middle, EmptyViews));
        Assert.Equal(new(AsrPhantomAction.Keep, AsrPhantomReason.LowNoSpeech),
            Evaluate(mixture, vocal, Middle with { NoSpeechProbability = .36422 }, EmptyViews));
        Assert.Equal(new(AsrPhantomAction.Review, AsrPhantomReason.NoVoiceInBothViews),
            Evaluate(mixture, Alternating(Count, -80, 80), Middle, EmptyViews));
    }

    [Fact]
    public void Any_aligned_speech_support_vetoes_the_corresponding_negative_views()
    {
        var mixture = Alternating(Count, -1450, 1450);
        var vocal = Alternating(Count, -1, 1);
        AsrPhantomVoice speech = new(Count, [new(Rate, 2 * Rate)]);

        Assert.Equal(new(AsrPhantomAction.Keep, AsrPhantomReason.VoiceSupport),
            Evaluate(mixture, vocal, Middle, EmptyViews with { Original = speech }));
        Assert.Equal(new(AsrPhantomAction.Keep, AsrPhantomReason.EnhancedVoiceSupport),
            Evaluate(mixture, vocal, Middle, EmptyViews with { Vocal = speech }));
        Assert.Equal(new(AsrPhantomAction.Keep, AsrPhantomReason.NormalizedVoiceSupport),
            Evaluate(mixture, vocal, Middle, EmptyViews with { Normalized = speech }));
    }

    [Fact]
    public void Missing_truncated_and_malformed_vad_are_never_silence_evidence()
    {
        var mixture = Alternating(Count, -1450, 1450);
        var vocal = Alternating(Count, -1, 1);
        AsrPhantomVoice tooShort = new(3 * Rate, []);
        AsrPhantomVoice unordered = new(Count, [new(200, 800), new(300, 400)]);
        var invalidViews = new[]
        {
            EmptyViews with { Original = null },
            EmptyViews with { Vocal = null },
            EmptyViews with { Normalized = null },
            EmptyViews with { Original = tooShort },
            EmptyViews with { Vocal = tooShort },
            EmptyViews with { Normalized = tooShort },
            EmptyViews with { Original = unordered },
            EmptyViews with { Vocal = new(Count + 1, []) },
            EmptyViews with { Normalized = new(Count, [new(0, Count + 1)]) }
        };
        foreach (var views in invalidViews)
            Assert.NotEqual(AsrPhantomAction.Drop, Evaluate(mixture, vocal, Middle, views).Action);
        Assert.NotEqual(AsrPhantomAction.Drop, Evaluate(mixture, vocal.AsSpan()[..^1], Middle, EmptyViews).Action);
    }

    [Fact]
    public void Offset_branch_checks_mean_restoration_drift_and_windowed_residual()
    {
        var original = Alternating(Count, -1473, 1427);
        short[] vocal = new short[Count];
        vocal.AsSpan(0, Count / 2).Fill(-23);
        vocal.AsSpan(Count / 2).Fill(-22);
        Assert.Equal(new(AsrPhantomAction.Drop, AsrPhantomReason.OffsetOnlyWithoutVoice),
            Evaluate(original, vocal, Middle, EmptyViews));
        Assert.Equal(new(AsrPhantomAction.Review, AsrPhantomReason.NoVoiceInBothViews),
            Evaluate(Alternating(Count, -1450, 1450), vocal, Middle, EmptyViews));

        // A localized AC peak must veto deletion even when the whole-window RMS is still faint.
        for (int i = 3 * Rate - 160; i < 3 * Rate; i++)
            vocal[i] += (short)((i & 1) == 0 ? -14 : 14);
        Assert.Equal(new(AsrPhantomAction.Review, AsrPhantomReason.NoVoiceInBothViews),
            Evaluate(original, vocal, Middle, EmptyViews));

        vocal.AsSpan().Fill(-23);
        vocal.AsSpan(2 * Rate, 160).Fill(-22);
        Assert.Equal(new(AsrPhantomAction.Review, AsrPhantomReason.NoVoiceInBothViews),
            Evaluate(original, vocal, Middle, EmptyViews));
    }

    [Fact]
    public void A_loud_neighbor_may_mask_a_quiet_candidate_in_the_normalized_view()
    {
        var mixture = Alternating(Count, -1450, 1450);
        var vocal = Alternating(Count, -1, 1);
        for (int i = 7 * Rate / 2; i < vocal.Length; i++)
            vocal[i] = (short)((i & 1) == 0 ? -8000 : 8000);
        AsrPhantomVoice normalized = new(Count, [new(57_000, 61_000)]);
        Assert.Equal(new(AsrPhantomAction.Review, AsrPhantomReason.NormalizedWindowUnderdriven),
            Evaluate(mixture, vocal, Middle, EmptyViews with { Normalized = normalized }));
    }

    [Fact]
    public void A_fully_covered_proven_digital_silence_chunk_does_not_require_a_stem()
    {
        var quiet = new short[Count];
        Assert.Equal(new(AsrPhantomAction.Drop, AsrPhantomReason.DigitalSilence),
            Evaluate(quiet, [], Middle, new(Empty, null, null)));
        quiet[0] = 1;
        Assert.NotEqual(AsrPhantomAction.Drop,
            Evaluate(quiet, [], Middle, new(Empty, null, null)).Action);
    }

    [Theory]
    [InlineData(0, 2 * Rate, 12_000, 14_000)]
    [InlineData(3 * Rate, Count, 81_000, 82_000)]
    public void Edge_context_can_prove_a_drop_or_veto_it_with_neighboring_speech(
        int start, int end, int speechStart, int speechEnd)
    {
        var center = Alternating(Count, -1450, 1450);
        var savedVocal = Alternating(Count, -1, 1);
        var extended = Alternating(Count + 2 * Rate, -1450, 1450);
        var extendedVocal = Alternating(extended.Length, -1, 1);
        var extendedEmpty = new AsrPhantomVoice(extended.Length, []);
        AsrPhantomVoiceViews extendedViews = new(extendedEmpty, extendedEmpty, extendedEmpty);
        var candidate = new AsrPhantomCandidate(start, end, .64);
        Assert.Equal(MissingContext, Evaluate(center, savedVocal, candidate, EmptyViews));
        Assert.Equal(new(AsrPhantomAction.Drop, AsrPhantomReason.MusicWithoutVocalSignal),
            AsrPhantomFilter.RestoreContext(MissingContext, candidate, center, savedVocal, EmptyViews,
                extended, extendedVocal, extendedViews, Rate));

        var realSpeech = new AsrPhantomVoice(extended.Length, [new(speechStart, speechEnd)]);
        Assert.Equal(new(AsrPhantomAction.Keep, AsrPhantomReason.VoiceSupport),
            AsrPhantomFilter.RestoreContext(MissingContext, candidate, center, savedVocal, EmptyViews,
                extended, extendedVocal, extendedViews with { Original = realSpeech }, Rate));
    }

    [Fact]
    public void Edge_approval_requires_matching_center_valid_clock_and_no_saved_center_veto()
    {
        var center = Alternating(Count, -1450, 1450);
        var savedVocal = Alternating(Count, -1, 1);
        var extended = Alternating(Count + 2 * Rate, -1450, 1450);
        var extendedVocal = Alternating(extended.Length, -1, 1);
        var extendedEmpty = new AsrPhantomVoice(extended.Length, []);
        AsrPhantomVoiceViews extendedViews = new(extendedEmpty, extendedEmpty, extendedEmpty);
        var candidate = new AsrPhantomCandidate(0, 2 * Rate, .64);

        Assert.Equal(new(AsrPhantomAction.Review, AsrPhantomReason.SavedCenterUnavailable),
            AsrPhantomFilter.RestoreContext(MissingContext, candidate, center, [], default,
                extended, extendedVocal, extendedViews, Rate));
        AsrPhantomVoice savedSpeech = new(Count, [new(0, Rate)]);
        Assert.Equal(new(AsrPhantomAction.Review, AsrPhantomReason.SavedCenterVeto),
            AsrPhantomFilter.RestoreContext(MissingContext, candidate, center, savedVocal,
                EmptyViews with { Vocal = savedSpeech }, extended, extendedVocal, extendedViews, Rate));
        Assert.Equal(MissingContext,
            AsrPhantomFilter.RestoreContext(MissingContext, candidate with { StartSample = -1 }, center,
                savedVocal, EmptyViews, extended, extendedVocal, extendedViews, Rate));
        extended[Rate + 23]++;
        Assert.Equal(MissingContext, AsrPhantomFilter.RestoreContext(MissingContext, candidate, center, savedVocal,
            EmptyViews, extended, extendedVocal, extendedViews, Rate));
    }

    [Fact]
    public void Normalizer_initializes_from_first_sample_and_scales_peak_to_minus_six_dbfs()
    {
        short[] signal = [-20, -20, -21, -22, -19, -18];
        short[] expected = [0, 0, -8119, -16142, 8403, 16423];
        Assert.Equal(expected, AsrPhantomFilter.NormalizeVocalForVad(signal));
        Assert.Equal(new short[12], AsrPhantomFilter.NormalizeVocalForVad(Enumerable.Repeat((short)-23, 12).ToArray()));
    }

    private static AsrPhantomDecision Evaluate(ReadOnlySpan<short> original, ReadOnlySpan<short> vocals,
        AsrPhantomCandidate candidate, AsrPhantomVoiceViews views)
        => AsrPhantomFilter.Evaluate(original, vocals, candidate, views.Original, views.Vocal, views.Normalized);

    private static short[] Alternating(int count, short even, short odd)
    {
        short[] result = new short[count];
        for (int i = 0; i < count; i++)
            result[i] = (i & 1) == 0 ? even : odd;
        return result;
    }
}
