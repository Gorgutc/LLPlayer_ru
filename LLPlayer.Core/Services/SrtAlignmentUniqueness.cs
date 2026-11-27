using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Core.Services;

internal static class SrtAlignmentUniqueness
{
    private const int MaxWindows = 4;
    private static readonly TimeSpan MaxWindowDuration = TimeSpan.FromSeconds(28);
    private static readonly TimeSpan MinimumOverlap = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ContextPadding = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SuspectWordDuration = TimeSpan.FromSeconds(2);

    public static async Task<SrtAlignmentRejection> VerifyAsync(SrtAlignmentProposal onlyMatch, TimeSpan duration,
        Func<SrtAlignmentAudioWindow, Task<IReadOnlyList<WordTiming>>> read, CancellationToken token,
        IReadOnlyList<WordTiming>? primaryWords = null)
    {
        token.ThrowIfCancellationRequested();
        if (onlyMatch.Start is not { } start || onlyMatch.End is not { } end ||
            start < TimeSpan.Zero || end <= start || end > duration)
            return SrtAlignmentRejection.InvalidTiming;

        TimeSpan length = end - start;
        TimeSpan overlap = length >= MaxWindowDuration / 2 ? MaxWindowDuration : length * 2;
        if (overlap < MinimumOverlap) overlap = MinimumOverlap;
        TimeSpan padding = length < ContextPadding ? length : ContextPadding;

        // Establish that the whole alternate range can be checked before starting any recognizers.
        // Each split overlaps enough to contain a repeated phrase straddling its seam.
        List<Probe> probes = [];
        if ((start > TimeSpan.Zero && !AddProbes(probes, TimeSpan.Zero, start + padding, overlap, ProbeKind.Before)) ||
            (end < duration && !AddProbes(probes, end - padding, duration, overlap, ProbeKind.After)) ||
            !AddStretchedWordProbes(probes, primaryWords, start, end, overlap, token))
            return SrtAlignmentRejection.InvalidTiming;

        bool unavailableEvidence = false;
        foreach (Probe probe in probes)
        {
            token.ThrowIfCancellationRequested();
            IReadOnlyList<WordTiming> words;
            try
            {
                words = await read(probe.Window).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                token.ThrowIfCancellationRequested();
                unavailableEvidence = true;
                continue;
            }
            token.ThrowIfCancellationRequested();

            WordTiming[] bounded = words.Where(word => word.Start >= probe.Window.Start && word.End <= probe.Window.End).ToArray();
            if (probe.Kind == ProbeKind.InsideWord)
            {
                // A whole cue independently heard inside one primary word cannot be assigned to
                // the proposed timing with confidence. A long pronunciation alone proves nothing.
                var withinWord = SrtAudioAlignment.Align([onlyMatch.Cue], bounded, duration, token)[0];
                if (withinWord.Rejection == SrtAlignmentRejection.Ambiguous ||
                    (withinWord.Start is { } insideStart && withinWord.End is { } insideEnd &&
                    (insideStart - start >= ContextPadding || end - insideEnd >= ContextPadding)))
                    return SrtAlignmentRejection.Ambiguous;
                unavailableEvidence |= withinWord.Rejection == SrtAlignmentRejection.InvalidTiming;
                continue;
            }

            bool before = probe.Kind == ProbeKind.Before;
            WordTiming[] alternate = bounded.Where(word => before ? word.End <= start : word.Start >= end).ToArray();
            SrtAlignmentProposal candidate = SrtAudioAlignment.Align([onlyMatch.Cue], alternate, duration, token)[0];
            if (candidate.Start.HasValue || candidate.Rejection == SrtAlignmentRejection.Ambiguous)
                return SrtAlignmentRejection.Ambiguous;
            if (candidate.Rejection == SrtAlignmentRejection.InvalidTiming)
            {
                unavailableEvidence = true;
                continue;
            }
            if (SrtAudioAlignment.HasRepeatChallenger(onlyMatch.Cue, alternate, duration, token))
                return SrtAlignmentRejection.Ambiguous;

            if (bounded.Length != alternate.Length)
            {
                // A stretched incumbent may slightly overlap another real phrase. Under padding,
                // require a literal match whose midpoint is outside it and whose far edge is distinct.
                var padded = SrtAudioAlignment.Align([onlyMatch.Cue], bounded, duration, token)[0];
                if (IsDistinctPaddedMatch(padded, before, start, end))
                    return SrtAlignmentRejection.Ambiguous;
                if (padded.Rejection == SrtAlignmentRejection.InvalidTiming)
                    unavailableEvidence = true;
            }
        }
        return unavailableEvidence ? SrtAlignmentRejection.InvalidTiming : SrtAlignmentRejection.None;
    }

    private static bool AddStretchedWordProbes(List<Probe> probes, IReadOnlyList<WordTiming>? primaryWords,
        TimeSpan start, TimeSpan end, TimeSpan overlap, CancellationToken token)
    {
        if (primaryWords == null) return true;

        foreach (WordTiming word in primaryWords)
        {
            token.ThrowIfCancellationRequested();
            if (word.End <= start || word.Start >= end)
                continue;

            TimeSpan from = word.Start > start ? word.Start : start;
            TimeSpan to = word.End < end ? word.End : end;
            if (to - from >= SuspectWordDuration && !AddProbes(probes, from, to, overlap, ProbeKind.InsideWord))
                return false;
        }
        return true;
    }

    private static bool IsDistinctPaddedMatch(SrtAlignmentProposal candidate, bool before, TimeSpan start, TimeSpan end)
    {
        if (!candidate.IsExactAnchor || candidate.Start is not { } candidateStart || candidate.End is not { } candidateEnd)
            return false;

        TimeSpan midpoint = candidateStart + (candidateEnd - candidateStart) / 2;
        return before
            ? midpoint <= start && start - candidateStart >= ContextPadding
            : midpoint >= end && candidateEnd - end >= ContextPadding;
    }

    private static bool AddProbes(List<Probe> probes, TimeSpan from, TimeSpan to, TimeSpan overlap, ProbeKind kind)
    {
        while (from < to)
        {
            if (probes.Count == MaxWindows)
                return false;

            TimeSpan remaining = to - from;
            TimeSpan end = remaining <= MaxWindowDuration ? to : from + MaxWindowDuration;
            probes.Add(new(new(from, end), kind));
            if (end == to)
                return true;
            if (overlap >= MaxWindowDuration)
                return false;

            from = end - overlap;
        }
        return true;
    }

    private enum ProbeKind { Before, After, InsideWord }
    private readonly record struct Probe(SrtAlignmentAudioWindow Window, ProbeKind Kind);
}
