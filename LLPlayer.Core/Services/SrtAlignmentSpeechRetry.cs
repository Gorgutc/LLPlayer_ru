using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Core.Services;

/// <summary>Retries gaps bounded by confirmed, globally unique speech. First-pass proposals are kept.</summary>
internal static class SrtAlignmentSpeechRetry
{
    private const int MaxWindows = 4;
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(27);
    private static readonly TimeSpan SplitAfter = TimeSpan.FromSeconds(18);
    private static readonly TimeSpan Context = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan SplitPadding = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan CentralContext = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan TimingAgreement = TimeSpan.FromMilliseconds(250);

    public static async Task<IReadOnlyList<SrtAlignmentProposal>> RetryAsync(
        IReadOnlyList<SrtAlignmentProposal> firstPass, TimeSpan duration,
        Func<SrtAlignmentAudioWindow, Task<IReadOnlyList<WordTiming>>> read, CancellationToken token)
    {
        var proposals = firstPass.ToArray();
        int budget = MaxWindows;
        for (int i = 1; i < proposals.Length - 1 && budget > 0; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!proposals[i - 1].IsExactAnchor || !NeedsMoreSpeech(proposals[i]))
                continue;

            int first = i;
            while (i < proposals.Length - 1 && NeedsMoreSpeech(proposals[i]))
                i++;
            if (!proposals[i].IsExactAnchor || proposals[first - 1].End is not { } start ||
                proposals[i].Start is not { } end || end <= start || end - start > MaxGap)
                continue;

            SrtAlignmentAudioWindow[] windows = GetWindows(start, end, duration);
            if (windows.Length > budget)
                continue;

            budget -= windows.Length;
            var gap = proposals[first..i];
            var retryCues = gap.Select(proposal => proposal.Cue).ToArray();
            var candidates = new SrtAlignmentProposal?[gap.Length];
            var rejected = new SrtAlignmentRejection[gap.Length];
            bool unreadable = false;
            foreach (SrtAlignmentAudioWindow window in windows)
            {
                token.ThrowIfCancellationRequested();
                IReadOnlyList<WordTiming> retry;
                try
                {
                    retry = await read(window).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    // Optional evidence with an invalid audio clock or word file cannot replace the first pass.
                    unreadable = true;
                    break;
                }

                TimeSpan windowStart = start > window.Start ? start : window.Start;
                TimeSpan windowEnd = end < window.End ? end : window.End;
                var localSpeech = retry.Where(word => word.Start >= windowStart && word.End <= windowEnd).ToArray();
                var local = SrtAudioAlignment.Align(retryCues, localSpeech, duration, token);
                for (int cue = 0; cue < local.Count; cue++)
                {
                    SrtAlignmentProposal item = local[cue];
                    if (item.Rejection is SrtAlignmentRejection.Ambiguous or SrtAlignmentRejection.ConflictingOrder)
                        rejected[cue] = item.Rejection;
                    if (item.Start is not { } localStart || item.End is not { } localEnd)
                        continue;

                    if (candidates[cue] is { } previous)
                    {
                        if (localStart >= previous.End!.Value || localEnd <= previous.Start!.Value ||
                            (localStart - previous.Start.Value).Duration() > TimingAgreement ||
                            (localEnd - previous.End.Value).Duration() > TimingAgreement)
                            rejected[cue] = SrtAlignmentRejection.Ambiguous;
                    }
                    else
                        candidates[cue] = item with { IsExactAnchor = false };
                }
            }
            if (unreadable)
                continue;

            // First drop unreliable windows' candidates. The remaining intervals take part in both order scans.
            for (int cue = 0; cue < candidates.Length; cue++)
                if (rejected[cue] != SrtAlignmentRejection.None) candidates[cue] = null;

            // Cross-window candidates must agree with the authored cue order as well.
            TimeSpan furthestEnd = start;
            for (int cue = 0; cue < candidates.Length; cue++)
            {
                if (candidates[cue] is not { } item) continue;
                if (item.Start!.Value < furthestEnd) rejected[cue] = SrtAlignmentRejection.ConflictingOrder;
                if (item.End!.Value > furthestEnd) furthestEnd = item.End.Value;
            }
            TimeSpan earliestStart = end;
            for (int cue = candidates.Length - 1; cue >= 0; cue--)
            {
                if (candidates[cue] is { } item)
                {
                    if (item.End!.Value > earliestStart) rejected[cue] = SrtAlignmentRejection.ConflictingOrder;
                    if (rejected[cue] == SrtAlignmentRejection.None) proposals[first + cue] = item;
                    if (item.Start!.Value < earliestStart) earliestStart = item.Start.Value;
                }
                if (rejected[cue] != SrtAlignmentRejection.None)
                    proposals[first + cue] = gap[cue] with { Rejection = rejected[cue] };
            }
        }
        return proposals;
    }

    private static bool NeedsMoreSpeech(SrtAlignmentProposal proposal) =>
        proposal.Rejection == SrtAlignmentRejection.Unmatched;

    private static SrtAlignmentAudioWindow[] GetWindows(TimeSpan start, TimeSpan end, TimeSpan duration)
    {
        TimeSpan from = start > Context ? start - Context : TimeSpan.Zero;
        TimeSpan to = duration - end > Context ? end + Context : duration;
        if (end - start <= SplitAfter)
            return [new(from, to)];

        TimeSpan middle = start + (end - start) / 2;
        return [new(from, middle + SplitPadding), new(middle - CentralContext, middle + CentralContext),
            new(middle - SplitPadding, to)];
    }
}
