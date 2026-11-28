using System.ComponentModel;
using FlyleafLib.MediaPlayer.SpeechSeparation;
using FlyleafLib.Vad;

namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>One ASR run's acoustic evidence. The caller owns the PCM clock and any required GPU admission.</summary>
internal sealed class AsrPhantomAnalysis : IDisposable
{
    internal const int HaloSamples = 2 * AsrPhantomFilter.SampleRate;
    private const int Pad = AsrPhantomFilter.PaddingSamples;
    private static readonly AsrPhantomDecision MissingContext =
        new(AsrPhantomAction.Keep, AsrPhantomReason.UnavailableContext);
    private static readonly LogHandler Log = new("[ASR phantom filter] ");

    private readonly Func<ReadOnlyMemory<short>, CancellationToken, Task<short[]?>> separateVocals;
    private readonly Func<ReadOnlyMemory<short>, CancellationToken, AsrPhantomVoice?> measureVoice;
    private readonly Action<string> warn;
    private AsrVadDetector? detector;
    private bool detectorRequested, warningSent;

    internal AsrPhantomAnalysis(
        Func<ReadOnlyMemory<short>, CancellationToken, Task<short[]?>> separateVocals, Action<string>? warn = null)
    {
        this.separateVocals = separateVocals;
        measureVoice = MeasureWithDetector;
        this.warn = warn ?? Warn;
    }

    internal AsrPhantomAnalysis(
        Func<ReadOnlyMemory<short>, CancellationToken, AsrPhantomVoice?> measureVoice,
        Func<ReadOnlyMemory<short>, CancellationToken, Task<short[]?>> separateVocals, Action<string>? warn = null)
    {
        this.measureVoice = measureVoice;
        this.separateVocals = separateVocals;
        this.warn = warn ?? Warn;
    }

    /// <summary>
    /// Returns one decision per input cue, in native order. Empty, unproven or partial halos cannot extend the
    /// central clock. A full halo supplied by the decoder comprises exactly two seconds of adjacent PCM.
    /// </summary>
    internal async Task<AsrPhantomDecision[]> AnalyzeAsync(
        ReadOnlyMemory<short> center,
        IReadOnlyList<AsrPhantomCandidate?> candidates,
        ReadOnlyMemory<short> prefix,
        ReadOnlyMemory<short> suffix,
        bool knownLeft,
        bool knownRight,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var central = new EvidenceWindow(this, center, token);
        EvidenceWindow? extended = null;
        prefix = !knownLeft && prefix.Length == HaloSamples ? prefix : default;
        suffix = !knownRight && suffix.Length == HaloSamples ? suffix : default;
        AsrPhantomDecision[] decisions = new AsrPhantomDecision[candidates.Count];
        if (center.IsEmpty || center.Length > HtDemucsSpeechSeparator.MaxAsrEvidenceSamples)
        {
            Array.Fill(decisions, MissingContext);
            return decisions;
        }
        AsrPhantomCandidate? rejectedPrevious = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var preceding = rejectedPrevious;
            rejectedPrevious = null;
            AsrPhantomDecision decision = MissingContext;
            decisions[i] = decision;
            if (candidates[i] is not { } candidate)
                continue;

            decision = AsrPhantomFilter.Evaluate(center.Span, default, candidate, null, null, null,
                knownLeft, knownRight);
            decisions[i] = decision;
            if (decision.Reason is AsrPhantomReason.InvalidClock)
                continue;

            bool longCandidate = AsrPhantomAlignmentEvidence.IsLongCandidate(candidate);
            if (!longCandidate && decision.Reason is (AsrPhantomReason.LowNoSpeech or AsrPhantomReason.UnavailableScore))
                continue;

            bool atLeft = candidate.StartSample < Pad;
            bool atRight = candidate.EndSample > center.Length - Pad;
            bool useContext = (atLeft || atRight) && (!atLeft || !prefix.IsEmpty) && (!atRight || !suffix.IsEmpty);
            int first = Math.Max(0, candidate.StartSample - Pad);
            int last = (int)Math.Min(center.Length, (long)candidate.EndSample + Pad);
            if (TouchesUnknown(atLeft, atRight, knownLeft, knownRight) && !useContext)
                continue;

            AsrPhantomVoice? originalVoice = central.Original;
            decision = AsrPhantomFilter.Evaluate(center.Span, default, candidate, originalVoice, null, null,
                knownLeft, knownRight);
            decisions[i] = decision;
            if (decision.Action is AsrPhantomAction.Drop ||
                !HasNoVoice(originalVoice, center.Length, first, last, allowDetectorTail: useContext))
                continue;

            if (decision.Reason is not AsrPhantomReason.EnhancedViewUnavailable && !useContext && !longCandidate)
                continue;

            EvidenceWindow window = central;
            AsrPhantomCandidate projected = candidate;
            if (useContext)
            {
                if (center.Length > HtDemucsSpeechSeparator.MaxAsrEvidenceSamples - prefix.Length - suffix.Length)
                    continue;
                extended ??= new(this, Join(prefix, center, suffix), token);
                projected = Project(candidate, prefix.Length);
                if (!HasNoVoice(extended.Original, extended.Pcm.Length, projected.StartSample - Pad,
                    projected.EndSample + Pad))
                    continue;
                window = extended;
            }

            if (!await central.PrepareVocalsAsync().ConfigureAwait(false))
                continue;

            decision = AsrPhantomFilter.Evaluate(center.Span, central.Vocals, candidate, originalVoice,
                central.Voices.Vocal, central.Voices.Normalized, knownLeft, knownRight);
            decisions[i] = decision;
            if (decision.Action is AsrPhantomAction.Drop)
                continue;

            if (useContext)
            {
                // The clipped center can preserve speech at the seam. Expanded PCM supplies the missing
                // measurement context for a drop and must never override observed center speech.
                if (!HasNoVoice(central.Voices.Vocal, center.Length, first, last, allowDetectorTail: true) ||
                    !HasNoVoice(central.Voices.Normalized, center.Length, first, last, allowDetectorTail: true) ||
                    !await window.PrepareVocalsAsync().ConfigureAwait(false))
                    continue;

                decision = AsrPhantomFilter.RestoreContext(decision, candidate, center.Span, central.Vocals,
                    central.Voices, window.Pcm.Span, window.Vocals, window.Voices, prefix.Length, knownLeft, knownRight);
                decisions[i] = decision;
                if (decision.Action is AsrPhantomAction.Drop)
                    continue;
            }

            if (longCandidate)
            {
                decision = AsrPhantomAlignmentEvidence.EvaluateLong(window.Vocals, projected, window.Voices,
                    pcm => Measure(pcm, token));
                if (decision.Action is AsrPhantomAction.Drop)
                    rejectedPrevious = candidate;
                decisions[i] = decision;
            }
            else if (AsrPhantomAlignmentEvidence.IsShortCandidate(window.Vocals, projected,
                    useContext && preceding is { } previous ? Project(previous, prefix.Length) : preceding))
            {
                decisions[i] = AsrPhantomAlignmentEvidence.EvaluateShort(window.Vocals, projected, window.Voices,
                    pcm => Measure(pcm, token));
            }
        }

        token.ThrowIfCancellationRequested();
        return decisions;
    }

    private static bool TouchesUnknown(bool atLeft, bool atRight, bool knownLeft, bool knownRight)
        => atLeft && !knownLeft || atRight && !knownRight;

    private static AsrPhantomCandidate Project(AsrPhantomCandidate candidate, int offset)
        => candidate with { StartSample = candidate.StartSample + offset, EndSample = candidate.EndSample + offset };

    private static short[] Join(ReadOnlyMemory<short> prefix, ReadOnlyMemory<short> pcm, ReadOnlyMemory<short> suffix)
    {
        short[] merged = new short[prefix.Length + pcm.Length + suffix.Length];
        prefix.Span.CopyTo(merged);
        pcm.Span.CopyTo(merged.AsSpan(prefix.Length));
        suffix.Span.CopyTo(merged.AsSpan(prefix.Length + pcm.Length));
        return merged;
    }

    private static bool HasNoVoice(AsrPhantomVoice? voice, int sampleCount, int first, int last,
        bool allowDetectorTail = false)
    {
        // The clipped center's final partial Silero window is measured in the independently proven extension.
        // Any other incomplete measurement still fails closed for a deletion, and center speech always vetoes.
        int inspectedEnd = allowDetectorTail && last == sampleCount
            ? sampleCount / AsrVadDetector.WindowSamples * AsrVadDetector.WindowSamples
            : last;
        if (voice is not { } evidence || evidence.Intervals is null || first < 0 || first >= last ||
            first >= inspectedEnd || evidence.CoveredSamples < inspectedEnd || evidence.CoveredSamples > sampleCount)
            return false;
        int end = 0;
        foreach (var span in evidence.Intervals)
        {
            if (span.StartSample < end || span.EndSample <= span.StartSample || span.EndSample > sampleCount ||
                span.StartSample < last && span.EndSample > first)
                return false;
            end = span.EndSample;
        }
        return true;
    }

    private AsrPhantomVoice? MeasureWithDetector(ReadOnlyMemory<short> pcm, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!detectorRequested)
        {
            detectorRequested = true;
            detector = AsrVadDetector.TryCreate();
        }
        return detector?.MeasureForPhantomFilter(pcm.Span);
    }

    private AsrPhantomVoice? Measure(ReadOnlyMemory<short> pcm, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = measureVoice(pcm, token);
        token.ThrowIfCancellationRequested();
        // The stock factory already logs the single missing-model or initialization failure.
        if (result is null && (!detectorRequested || detector is not null))
            WarnOnce("Silero could not measure the ASR filter evidence. Keeping unverified Whisper segments.");
        return result;
    }

    private async Task<short[]?> SeparateAsync(ReadOnlyMemory<short> pcm, CancellationToken token)
    {
        if (pcm.Length > HtDemucsSpeechSeparator.MaxAsrEvidenceSamples)
            return null;
        token.ThrowIfCancellationRequested();
        try
        {
            short[]? result = await separateVocals(pcm, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result?.Length == pcm.Length)
                return result;
            WarnOnce("HTDemucs did not return aligned ASR evidence. Keeping unverified Whisper segments.");
        }
        catch (Exception error) when (error is not EngineProcessDrainException &&
            error is (IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException or
                NotSupportedException or Win32Exception))
        {
            token.ThrowIfCancellationRequested();
            WarnOnce($"HTDemucs could not measure ASR filter evidence. Keeping unverified Whisper segments. {error.Message}");
        }
        return null;
    }

    private void WarnOnce(string message)
    {
        if (warningSent)
            return;
        warningSent = true;
        warn(message);
    }

    private static void Warn(string message)
    {
        if (Engine.Config is not null && Logger.CanWarn)
            Log.Warn(message);
    }

    public void Dispose() => detector?.Dispose();

    private sealed class EvidenceWindow(AsrPhantomAnalysis owner, ReadOnlyMemory<short> pcm, CancellationToken token)
    {
        private bool measuredOriginal, attemptedVocals;
        private AsrPhantomVoice? original, vocal, normalized;
        internal ReadOnlyMemory<short> Pcm => pcm;
        internal short[] Vocals { get; private set; } = [];
        internal AsrPhantomVoiceViews Voices => new(Original, vocal, normalized);
        internal AsrPhantomVoice? Original
        {
            get
            {
                if (!measuredOriginal)
                {
                    measuredOriginal = true;
                    original = owner.Measure(pcm, token);
                }
                return original;
            }
        }

        internal async Task<bool> PrepareVocalsAsync()
        {
            if (attemptedVocals)
                return Vocals.Length == pcm.Length;
            attemptedVocals = true;
            if (await owner.SeparateAsync(pcm, token).ConfigureAwait(false) is not { } result)
                return false;
            Vocals = result;
            vocal = owner.Measure(Vocals, token);
            normalized = owner.Measure(AsrPhantomFilter.NormalizeVocalForVad(Vocals), token);
            return true;
        }
    }
}
