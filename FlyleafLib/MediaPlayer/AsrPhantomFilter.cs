using System;
using System.Collections.Generic;

namespace FlyleafLib.MediaPlayer;

#nullable enable

internal enum AsrPhantomAction { Keep, Review, Drop }

internal enum AsrPhantomReason
{
    InvalidClock,
    UnavailableScore,
    LowNoSpeech,
    UnavailableContext,
    VoiceSupport,
    EnhancedVoiceSupport,
    DigitalSilence,
    EnhancedViewUnavailable,
    NoVoiceInBothViews,
    MusicWithoutVocalSignal,
    NormalizedVadUnavailable,
    NormalizedVoiceSupport,
    NormalizedWindowUnderdriven,
    OffsetOnlyWithoutVoice,
    SavedCenterUnavailable,
    SavedCenterVeto,
    AlignmentUnverifiedPaddedWindow,
    AlignmentCoverageMissing,
    AlignmentVoiceSupport,
    AlignmentLocalCoverageMissing,
    AlignmentWordClockUnavailable,
    AlignmentVoiceNearWordClock,
    AlignmentNormalizationConsensus,
    AlignmentLocalVoiceSupport,
    InvalidLongAlignment,
    CollapsedAdjacentAlignment
}

internal readonly record struct AsrPhantomDecision(AsrPhantomAction Action, AsrPhantomReason Reason);
internal readonly record struct AsrPhantomCandidate(int StartSample, int EndSample, double NoSpeechProbability,
    AsrPhantomAlignment? Alignment = null);
internal readonly record struct AsrPhantomVoiceInterval(int StartSample, int EndSample);
internal readonly record struct AsrPhantomVoice(int CoveredSamples, IReadOnlyList<AsrPhantomVoiceInterval> Intervals);
internal readonly record struct AsrPhantomVoiceViews(
    AsrPhantomVoice? Original, AsrPhantomVoice? Vocal, AsrPhantomVoice? Normalized);

internal static class AsrPhantomFilter
{
    internal const int SampleRate = 16_000;
    internal const int PaddingSamples = 4000;
    private const double MinimumNoSpeechProbability = 0.40;
    private const int FrameSamples = SampleRate / 10;
    private const double FullScale = 32_768;
    private const double MinimumRestoredOffset = 8;
    private const double MaximumOffsetError = 2;
    private const double MinimumFrameMeanSpan = 0.5;
    private const double MaximumCenteredShare = 0.10;
    private static readonly double MinimumMixRms = FullScale * Amplitude(-45);
    private static readonly double MaximumSilentVocalRms = FullScale * Amplitude(-75);
    private static readonly double MaximumRawOffsetRms = FullScale * Amplitude(-60);
    private static readonly double MaximumCenteredRms = FullScale * Amplitude(-85);
    private static readonly double MaximumSilentVocalShare = Amplitude(-45);
    private static readonly double MaximumCenteredMixShare = Amplitude(-55);
    private static readonly double MinimumLocalGain = Amplitude(-15);
    private static readonly double NormalizedPeak = Math.Round(FullScale * Amplitude(-6));
    private static readonly double DcDecay = Math.Exp(-Math.Tau * 30 / SampleRate);

    private enum VoiceEvidence { Unavailable, NoSpeech, Speech }

    internal static AsrPhantomDecision Evaluate(
        ReadOnlySpan<short> original, ReadOnlySpan<short> vocals, AsrPhantomCandidate candidate,
        AsrPhantomVoice? originalVoice, AsrPhantomVoice? vocalVoice, AsrPhantomVoice? normalizedVoice,
        bool knownLeft = false, bool knownRight = false)
        => EvaluateCore(original, vocals, candidate, new(originalVoice, vocalVoice, normalizedVoice),
            knownLeft, knownRight, allowDigitalSilence: true);

    /// <summary>Reevaluate only a missing-cut-context decision with independently proven adjacent PCM.</summary>
    internal static AsrPhantomDecision RestoreContext(
        AsrPhantomDecision previous, AsrPhantomCandidate candidate,
        ReadOnlySpan<short> center, ReadOnlySpan<short> centerVocals, AsrPhantomVoiceViews centerVoices,
        ReadOnlySpan<short> extendedOriginal, ReadOnlySpan<short> extendedVocals, AsrPhantomVoiceViews extendedVoices,
        int prefixSamples, bool knownLeft = false, bool knownRight = false)
    {
        if (previous != Keep(AsrPhantomReason.UnavailableContext) || !IsValidClock(candidate, center.Length) ||
            !TouchesUnknownEdge(candidate, center.Length, knownLeft, knownRight) ||
            extendedOriginal.Length != extendedVocals.Length || prefixSamples < 0 ||
            prefixSamples > extendedOriginal.Length - center.Length ||
            !extendedOriginal.Slice(prefixSamples, center.Length).SequenceEqual(center))
            return previous;

        var projected = candidate with
        {
            StartSample = candidate.StartSample + prefixSamples,
            EndSample = candidate.EndSample + prefixSamples
        };
        var decision = EvaluateCore(extendedOriginal, extendedVocals, projected, extendedVoices,
            knownLeft: false, knownRight: false, allowDigitalSilence: false);
        if (decision.Action != AsrPhantomAction.Drop)
            return decision;

        if (center.Length != centerVocals.Length || centerVoices.Original is null ||
            centerVoices.Vocal is null || centerVoices.Normalized is null)
            return Review(AsrPhantomReason.SavedCenterUnavailable);

        // The clipped check only vetoes a drop already supported by the extended view.
        var centerDecision = EvaluateCore(center, centerVocals, candidate, centerVoices,
            knownLeft: true, knownRight: true, allowDigitalSilence: false);
        return centerDecision.Action == AsrPhantomAction.Drop
            ? decision
            : Review(AsrPhantomReason.SavedCenterVeto);
    }

    /// <summary>Create a separate −6 dBFS vocal VAD view using the same 30 Hz DC block as the gain check.</summary>
    internal static short[] NormalizeVocalForVad(ReadOnlySpan<short> vocal)
    {
        if (vocal.IsEmpty)
            return [];

        short[] result = new short[vocal.Length];
        double peak = GetCenteredPeaks(vocal, 0, vocal.Length).Total;
        if (peak <= 0)
            return result;

        double gain = NormalizedPeak / peak;
        double offset = vocal[0];
        for (int i = 0; i < vocal.Length; i++)
            result[i] = (short)Math.Round(RemoveDc(vocal[i], ref offset) * gain);
        return result;
    }

    private static AsrPhantomDecision EvaluateCore(
        ReadOnlySpan<short> original, ReadOnlySpan<short> vocals, AsrPhantomCandidate candidate,
        AsrPhantomVoiceViews views, bool knownLeft, bool knownRight, bool allowDigitalSilence)
        {
        if (!IsValidClock(candidate, original.Length))
            return Keep(AsrPhantomReason.InvalidClock);
        if (!double.IsFinite(candidate.NoSpeechProbability) || candidate.NoSpeechProbability is < 0 or > 1)
            return Keep(AsrPhantomReason.UnavailableScore);
        if (candidate.NoSpeechProbability < MinimumNoSpeechProbability)
            return Keep(AsrPhantomReason.LowNoSpeech);
        if (TouchesUnknownEdge(candidate, original.Length, knownLeft, knownRight))
            return Keep(AsrPhantomReason.UnavailableContext);

        int left = Math.Max(0, candidate.StartSample - PaddingSamples);
        int right = (int)Math.Min(original.Length, (long)candidate.EndSample + PaddingSamples);
        var originalEvidence = GetVoiceEvidence(views.Original, original.Length, left, right);
        if (originalEvidence == VoiceEvidence.Speech)
            return Keep(AsrPhantomReason.VoiceSupport);
        if (originalEvidence == VoiceEvidence.Unavailable)
            return Keep(AsrPhantomReason.UnavailableContext);

        var vocalEvidence = vocals.Length == original.Length
            ? GetVoiceEvidence(views.Vocal, vocals.Length, left, right)
            : VoiceEvidence.Unavailable;
        if (vocalEvidence == VoiceEvidence.Speech)
            return Keep(AsrPhantomReason.EnhancedVoiceSupport);
        if (allowDigitalSilence && original.IndexOfAnyExcept((short)0) < 0)
            return Drop(AsrPhantomReason.DigitalSilence);
        if (vocalEvidence == VoiceEvidence.Unavailable)
            return Review(AsrPhantomReason.EnhancedViewUnavailable);

        double mixtureRms = Measure(original[left..right]).Rms;
        if (mixtureRms < MinimumMixRms)
            return Review(AsrPhantomReason.NoVoiceInBothViews);
        ReadOnlySpan<short> vocalWindow = vocals[left..right];
        bool silentVocal = HasNegligibleVocalSignal(vocalWindow, mixtureRms);
        if (!silentVocal && !HasOnlyRestoredOffset(original, vocals, vocalWindow, mixtureRms))
            return Review(AsrPhantomReason.NoVoiceInBothViews);

        // The normalized detector must have actually covered the entire requested window.
        if (views.Normalized is not { } normalized || normalized.CoveredSamples < right)
            return Review(AsrPhantomReason.NormalizedVadUnavailable);
        var normalizedEvidence = GetVoiceEvidence(normalized, vocals.Length, left, right);
        if (normalizedEvidence == VoiceEvidence.Speech)
            return Keep(AsrPhantomReason.NormalizedVoiceSupport);
        if (normalizedEvidence == VoiceEvidence.Unavailable)
            return Review(AsrPhantomReason.NormalizedVadUnavailable);

        var (local, total) = GetCenteredPeaks(vocals, left, right);
        if (total <= 0 || local < total * MinimumLocalGain)
            return Review(AsrPhantomReason.NormalizedWindowUnderdriven);

        return Drop(silentVocal ? AsrPhantomReason.MusicWithoutVocalSignal : AsrPhantomReason.OffsetOnlyWithoutVoice);
    }

    private static bool IsValidClock(AsrPhantomCandidate candidate, int sampleCount)
        => candidate.StartSample >= 0 && candidate.StartSample < candidate.EndSample && candidate.EndSample <= sampleCount;

    private static bool TouchesUnknownEdge(AsrPhantomCandidate candidate, int sampleCount, bool knownLeft, bool knownRight)
        => (!knownLeft && candidate.StartSample < PaddingSamples) ||
            (!knownRight && (long)candidate.EndSample + PaddingSamples > sampleCount);

    private static VoiceEvidence GetVoiceEvidence(AsrPhantomVoice? view, int sampleCount, int left, int right)
    {
        if (view is not { } evidence || evidence.CoveredSamples <= 0 ||
            evidence.CoveredSamples > sampleCount || evidence.Intervals is null)
            return VoiceEvidence.Unavailable;

        int previousEnd = 0;
        bool overlaps = false;
        foreach (var interval in evidence.Intervals)
        {
            if (interval.StartSample < previousEnd || interval.EndSample <= interval.StartSample ||
                interval.EndSample > sampleCount)
                return VoiceEvidence.Unavailable;
            previousEnd = interval.EndSample;
            overlaps |= interval.StartSample < right && interval.EndSample > left;
        }
        if (overlaps)
            return VoiceEvidence.Speech;
        return evidence.CoveredSamples >= right ? VoiceEvidence.NoSpeech : VoiceEvidence.Unavailable;
    }

    private static bool HasNegligibleVocalSignal(ReadOnlySpan<short> window, double mixtureRms)
    {
        double limit = Math.Min(MaximumSilentVocalRms, mixtureRms * MaximumSilentVocalShare);
        for (int first = 0; first < window.Length;)
        {
            int length = Math.Min(FrameSamples, window.Length - first);
            if (Measure(window.Slice(first, length)).Rms > limit)
                return false;
            first += length;
        }
        return true;
    }

    private static bool HasOnlyRestoredOffset(
        ReadOnlySpan<short> original, ReadOnlySpan<short> vocals, ReadOnlySpan<short> window, double mixtureRms)
    {
        int length = Math.Min(FrameSamples, window.Length);
        int last = window.Length - length;
        double rawPeak = 0, centeredPeak = 0, minMean = double.PositiveInfinity, maxMean = double.NegativeInfinity;
        for (int first = 0; ; first = Math.Min(first + FrameSamples / 2, last))
        {
            var frame = Measure(window.Slice(first, length));
            rawPeak = Math.Max(rawPeak, frame.Rms);
            centeredPeak = Math.Max(centeredPeak, frame.CenteredRms);
            minMean = Math.Min(minMean, frame.Mean);
            maxMean = Math.Max(maxMean, frame.Mean);
            if (first == last)
                break;
        }
        if (rawPeak is <= 0 || rawPeak > MaximumRawOffsetRms || centeredPeak > MaximumCenteredRms ||
            centeredPeak > rawPeak * MaximumCenteredShare || centeredPeak > mixtureRms * MaximumCenteredMixShare ||
            maxMean - minMean < MinimumFrameMeanSpan)
            return false;

        double vocalMean = Measure(vocals).Mean;
        return Math.Abs(vocalMean) >= MinimumRestoredOffset &&
            Math.Abs(vocalMean - Measure(original).Mean) <= MaximumOffsetError;
    }

    private static (double Local, double Total) GetCenteredPeaks(ReadOnlySpan<short> signal, int left, int right)
    {
        double local = 0, total = 0, offset = signal[0];
        for (int i = 0; i < signal.Length; i++)
        {
            double amplitude = Math.Abs(RemoveDc(signal[i], ref offset));
            total = Math.Max(total, amplitude);
            if (i >= left && i < right)
                local = Math.Max(local, amplitude);
        }
        return (local, total);
    }

    private static double RemoveDc(short sample, ref double offset)
    {
        offset = offset * DcDecay + sample * (1 - DcDecay);
        return sample - offset;
    }

    private static SignalMoments Measure(ReadOnlySpan<short> signal)
    {
        long sum = 0, squares = 0;
        foreach (short sample in signal)
        {
            sum += sample;
            squares += sample * sample;
        }
        return new(sum, squares, signal.Length);
    }

    private readonly record struct SignalMoments(long Sum, long Squares, int Count)
    {
        internal double Mean => Sum / (double)Count;
        internal double Rms => Math.Sqrt(Squares / (double)Count);
        // Only needed for a 100 ms frame, where these exact integer products cannot overflow.
        internal double CenteredRms => Math.Sqrt(Count * Squares - Sum * Sum) / Count;
    }

    private static double Amplitude(double decibels) => Math.Pow(10, decibels / 20);
    private static AsrPhantomDecision Keep(AsrPhantomReason reason) => new(AsrPhantomAction.Keep, reason);
    private static AsrPhantomDecision Review(AsrPhantomReason reason) => new(AsrPhantomAction.Review, reason);
    private static AsrPhantomDecision Drop(AsrPhantomReason reason) => new(AsrPhantomAction.Drop, reason);
}
