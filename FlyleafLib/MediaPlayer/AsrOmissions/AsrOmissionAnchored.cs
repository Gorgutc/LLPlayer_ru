using System.Linq;

#nullable enable

namespace FlyleafLib.MediaPlayer.AsrOmissions;

public static partial class AsrOmissionDetector
{
    private enum Decision { Other, ShortInsertion, UnstableAnchors, IncompleteClause, NoLongGapOrStretchedCue }

    private static List<Candidate> Anchored(Frame frame, Frame? previous, CancellationToken token)
    {
        var (legacy, ignored) = Legacy(frame, token);
        List<Candidate> selected = [];
        HashSet<int> occupied = [];
        void Accept(Candidate? candidate)
        {
            if (candidate is { FollowingCue: { } index } && DonorSupported(frame, candidate) && occupied.Add(index))
                selected.Add(candidate);
        }

        foreach (Candidate candidate in legacy)
            Accept(candidate);
        foreach (Candidate candidate in LocalAdditions(frame, ignored))
            Accept(candidate);
        foreach (Candidate candidate in CollapsedTiming(frame))
            Accept(candidate);
        foreach (Candidate candidate in ShortCompletions(frame, ignored))
            Accept(candidate);
        if (previous is not null)
            Accept(Seam(previous, frame));

        for (int k = 1; k < frame.Ops.Length - 1; k++)
        {
            token.ThrowIfCancellationRequested();
            Edit edit = frame.Ops[k];
            Decision reason = ignored.GetValueOrDefault((edit.BStart, edit.BEnd));
            if (edit.Operation != Operation.Insert || reason is not (Decision.ShortInsertion or Decision.IncompleteClause) ||
                !Internal(frame, edit) || !Adjacent(frame.A, edit.AStart) || occupied.Contains(frame.A[edit.AStart].Cue) ||
                !StableAnchor(frame.Ops[k - 1], frame.A, frame.B, tail: true, nonzero: true) ||
                !StableAnchor(frame.Ops[k + 1], frame.A, frame.B, nonzero: true))
                continue;

            if (reason == Decision.ShortInsertion)
            {
                Accept(CompletionAtCueHead(frame, edit, frame.Ops[k + 1]));
                continue;
            }

            Candidate[] complete = CompleteCuesInGap(frame, edit).ToArray();
            if (complete.Length > 0)
            {
                selected.AddRange(complete);
                occupied.Add(frame.A[edit.AStart].Cue);
            }
        }

        for (int k = 1; k < frame.Ops.Length - 1; k++)
        {
            token.ThrowIfCancellationRequested();
            Edit edit = frame.Ops[k];
            Decision reason = ignored.GetValueOrDefault((edit.BStart, edit.BEnd));
            if (edit.Operation == Operation.Insert && reason is (Decision.ShortInsertion or Decision.IncompleteClause) &&
                Internal(frame, edit) && Adjacent(frame.A, edit.AStart) && !occupied.Contains(frame.A[edit.AStart].Cue) &&
                StableAnchor(frame.Ops[k - 1], frame.A, frame.B, tail: true, nonzero: true) &&
                StableAnchor(frame.Ops[k + 1], frame.A, frame.B, nonzero: true))
                Accept(EarlyOnsetRepair(frame, edit, frame.Ops[k + 1]));
        }
        return selected;
    }

    private static bool Internal(Frame f, Edit e) => e.AStart > 0 && e.AStart < f.A.Length &&
        e.BStart > 0 && e.BStart < e.BEnd && e.BEnd < f.B.Length;

    private static bool Adjacent(Word[] words, int i) => i > 0 && i < words.Length && words[i].Cue == words[i - 1].Cue + 1;

    private static long Overlap(long left, long right, long start, long end) => Math.Max(0, Math.Min(right, end) - Math.Max(left, start));

    private static (List<Candidate>, Dictionary<(int, int), Decision>) Legacy(Frame f, CancellationToken token)
    {
        Edit[] ops = Align(f.A, f.B, legacyKeys: true, token);
        List<Candidate> candidates = [];
        Dictionary<(int, int), Decision> ignored = [];
        int consumedThrough = -1;
        for (int k = 0; k < ops.Length; k++)
        {
            token.ThrowIfCancellationRequested();
            Edit e = ops[k];
            if (e.Operation != Operation.Insert || !Internal(f, e))
                continue;
            var range = (e.BStart, e.BEnd);
            if (!HasClauseSize(f.B, e.BStart, e.BEnd))
            {
                ignored[range] = Decision.ShortInsertion;
                continue;
            }
            if (!StableAnchor(ops[k - 1], f.A, f.B, tail: true) || !StableAnchor(ops[k + 1], f.A, f.B))
            {
                ignored[range] = Decision.UnstableAnchors;
                continue;
            }
            (int lo, int hi) = Clause(f.A, f.B, e, legacy: true);
            if (hi == 0)
            {
                ignored[range] = Decision.IncompleteClause;
                continue;
            }
            if (!HasClauseSize(f.B, lo, hi) || !Adjacent(f.A, e.AStart) || f.A[e.AStart - 1].Cue <= consumedThrough)
                continue;

            Candidate? safe = BuildCandidate(f, AsrOmissionKind.Gap, e.AStart, lo, hi, f.B[e.BEnd]);
            if (safe is null)
                continue;
            AsrOmissionKind? kind = SpanKind(f, e, lo, hi, allowMixed: false);
            if (!kind.HasValue)
            {
                ignored[range] = Decision.NoLongGapOrStretchedCue;
                continue;
            }
            candidates.Add(safe with { Kind = kind.Value });
            consumedThrough = f.A[e.AStart].Cue;
        }
        return (candidates, ignored);
    }

    private static AsrOmissionKind? SpanKind(Frame f, Edit e, int lo, int hi, bool allowMixed)
    {
        AsrOmissionCue before = f.Chunk.Baseline[f.A[e.AStart - 1].Cue];
        AsrOmissionCue after = f.Chunk.Baseline[f.A[e.AStart].Cue];
        long first = f.B[lo].Start, end = f.B[hi - 1].End;
        long inGap = Overlap(first, end, before.EndMs, after.StartMs);
        long inCues = Overlap(first, end, before.StartMs, before.EndMs) + Overlap(first, end, after.StartMs, after.EndMs);
        long span = end - first;
        long delay = f.B[e.BEnd].Start - after.StartMs;
        if (after.StartMs - before.EndMs >= MinimumSpan && inGap >= .80 * span)
            return AsrOmissionKind.Gap;
        if (inCues >= .80 * span && delay >= MinimumSpan)
            return AsrOmissionKind.Covered;
        return allowMixed && inGap >= MinimumSpan && inCues > 0 && delay >= MinimumSpan ? AsrOmissionKind.Mixed : null;
    }

    private static bool HasClauseSize(Word[] words, int lo, int hi) => hi - lo >= MinimumWords &&
        (decimal)words[hi - 1].End - words[lo].Start >= MinimumSpan;

    private static (int Lo, int Hi) Clause(Word[] a, Word[] b, Edit edit, bool legacy)
    {
        int first = -1, last = -1;
        for (int i = edit.BStart; i < edit.BEnd; i++)
        {
            if (!IsSentenceEnd(b[i], legacy))
                continue;
            if (first < 0)
                first = i;
            last = i;
        }
        if (last < 0 || edit.BEnd - last - 1 > 2)
            return default;
        int lo = first - edit.BStart < 2 && !IsSentenceEnd(a[edit.AStart - 1], legacy) ? first + 1 : edit.BStart;
        return (lo, last + 1);
    }

    private static Candidate? BuildCandidate(Frame f, AsrOmissionKind kind, int position, int lo, int hi,
        Word next, long? allowedEnd = null, bool splitForDisplay = true)
    {
        if (lo >= hi || position >= f.A.Length || position > 0 && !Adjacent(f.A, position) ||
            position == 0 && f.A[0].Cue != 0 || !DonorRangeValid(f, lo, hi))
            return null;
        int following = f.A[position].Cue;
        AsrOmissionCue after = f.Chunk.Baseline[following];
        long beforeEnd = following == 0 ? 0 : f.Chunk.Baseline[following - 1].EndMs;
        long first = f.B[lo].Start, last = f.B[hi - 1].End, start = Math.Max(first, beforeEnd);
        long nextStart = last <= after.StartMs ? after.StartMs : next.Start;
        if (start - first > BoundaryDrift || start >= last || nextStart < last || nextStart >= after.EndMs ||
            allowedEnd.HasValue && last > allowedEnd.Value || splitForDisplay && !Displayable(f.B, lo, hi, start))
            return null;
        return new(kind, lo, hi, start, last, following, nextStart);
    }

    private static bool Displayable(Word[] donor, int lo, int hi, long start)
    {
        int sentence = lo;
        long following = start;
        while (sentence < hi)
        {
            int end = sentence + 1;
            while (end < hi && !IsSentenceEnd(donor[end - 1], legacy: true))
                end++;
            bool split = donor[end - 1].End - donor[sentence].Start > 8_000;
            for (int group = sentence; group < end;)
            {
                int stop = end;
                if (split)
                {
                    stop = group + 1;
                    while (stop < end && donor[group].Cue == donor[stop].Cue)
                        stop++;
                }
                long groupStart = group == lo ? start : donor[group].Start;
                long groupEnd = donor[stop - 1].End;
                if (groupStart < following || groupEnd <= groupStart)
                    return false;
                following = groupEnd;
                group = stop;
            }
            sentence = end;
        }
        return true;
    }

    private static IEnumerable<Candidate> LocalAdditions(Frame f, Dictionary<(int, int), Decision> ignored)
    {
        for (int k = 1; k < f.Ops.Length - 1; k++)
        {
            Edit e = f.Ops[k];
            if (e.Operation != Operation.Insert || !Internal(f, e) ||
                !Adjacent(f.A, e.AStart) || ignored.GetValueOrDefault((e.BStart, e.BEnd)) is not
                    (Decision.UnstableAnchors or Decision.NoLongGapOrStretchedCue or Decision.IncompleteClause) ||
                !StableAnchor(f.Ops[k + 1], f.A, f.B) ||
                !(StableAnchor(f.Ops[k - 1], f.A, f.B, tail: true) || LeftAcrossOneDisagreement(f, k)))
                continue;
            (int lo, int hi) = Clause(f.A, f.B, e, legacy: false);
            if (!HasClauseSize(f.B, lo, hi) || !DonorRangeValid(f, lo, hi))
                continue;
            if (SpanKind(f, e, lo, hi, allowMixed: true) is { } kind &&
                BuildCandidate(f, kind, e.AStart, lo, hi, f.B[e.BEnd]) is { } candidate)
                yield return candidate;
        }
    }

    private static bool LeftAcrossOneDisagreement(Frame f, int k)
    {
        if (k < 3)
            return false;
        Edit near = f.Ops[k - 1], mismatch = f.Ops[k - 2];
        if (near.Operation != Operation.Equal || near.AEnd - near.AStart is <= 0 or >= StableRun ||
            mismatch.Operation != Operation.Replace || mismatch.AEnd - mismatch.AStart != 1 ||
            mismatch.BEnd - mismatch.BStart != 1 || !Close(f.A[mismatch.AStart].Start, f.B[mismatch.BStart].Start, AnchorTolerance))
            return false;
        for (int n = 0; n < near.AEnd - near.AStart; n++)
            if (!Close(f.A[near.AStart + n].Start, f.B[near.BStart + n].Start, AnchorTolerance))
                return false;
        return StableAnchor(f.Ops[k - 3], f.A, f.B, tail: true);
    }

    private static bool ZeroAt(Word[] a, int lo, int hi, long boundary, int cue) => hi > lo &&
        a[lo..hi].All(w => w.Cue == cue && w.Start == boundary && w.End == boundary);

    private static bool IsCollapsed(Frame f, Edit e, long boundary, int cue) => e.Operation == Operation.Replace &&
        ZeroAt(f.A, e.AStart, e.AEnd, boundary, cue) && (decimal)f.B[e.BEnd - 1].End - f.B[e.BStart].Start >= MinimumSpan;

    private static bool AnchoredBeforeCollapse(Frame f, int index, long boundary, int cue)
    {
        for (int k = index - 1; k >= 0; k--)
        {
            Edit e = f.Ops[k];
            if (e.Operation == Operation.Equal)
            {
                if (f.A[e.AStart..e.AEnd].Any(w => w.End > w.Start))
                    return StableAnchor(e, f.A, f.B, tail: true, nonzero: true);
                if (ZeroAt(f.A, e.AStart, e.AEnd, boundary, cue))
                    continue;
            }
            if (e.Operation == Operation.Insert && e.AStart > 0 && e.AStart < f.A.Length &&
                ZeroAt(f.A, e.AStart - 1, e.AStart + 1, boundary, cue) && e.BEnd - e.BStart <= 2 &&
                (decimal)f.B[e.BEnd - 1].End - f.B[e.BStart].Start <= 1000)
                continue;
            return false;
        }
        return false;
    }

    private static IEnumerable<Candidate> CollapsedTiming(Frame f)
    {
        for (int k = 1; k < f.Ops.Length - 1; k++)
        {
            Edit e = f.Ops[k];
            if (e.AStart >= f.A.Length)
                yield break;
            int cue = f.A[e.AStart].Cue;
            long boundary = f.Chunk.Baseline[cue].EndMs;
            if (!IsCollapsed(f, e, boundary, cue))
                continue;
            int first = k;
            while (k + 2 < f.Ops.Length - 1 && f.Ops[k + 1] is { Operation: Operation.Equal } equal &&
                ZeroAt(f.A, equal.AStart, equal.AEnd, boundary, cue) && IsCollapsed(f, f.Ops[k + 2], boundary, cue))
                k += 2;
            int position = f.Ops[k].AEnd, hi = f.Ops[k].BEnd;
            int lo = e.BStart;
            if (position >= f.A.Length || hi >= f.B.Length || f.A[position].Cue != cue + 1 || hi - lo < MinimumWords ||
                !IsSentenceEnd(f.B[hi - 1]) || !StableAnchor(f.Ops[k + 1], f.A, f.B, nonzero: true) ||
                !AnchoredBeforeCollapse(f, first, boundary, cue) || !DonorRangeValid(f, lo, hi))
                continue;
            long start = f.B[lo].Start, end = f.B[hi - 1].End;
            long gap = Overlap(start, end, boundary, f.Chunk.Baseline[cue + 1].StartMs);
            if (start >= boundary && gap >= MinimumSpan && gap >= (2.0 / 3) * (end - start) &&
                BuildCandidate(f, AsrOmissionKind.CollapsedTiming, position, lo, hi, f.B[hi]) is { } candidate)
                yield return candidate;
        }
    }

    private static Candidate? Seam(Frame previous, Frame f)
    {
        if (f.A.Length == 0 || f.Ops.Length < 2 || previous.Ops.Length == 0 || previous.Chunk.Baseline.Count == 0 ||
            !Close(previous.Chunk.OffsetMs + previous.Chunk.DurationMs, f.Chunk.OffsetMs, 2))
            return null;
        Edit e = f.Ops[0];
        if (e.Operation != Operation.Insert || e.AStart != 0 || e.BStart != 0 || e.BEnd < StableRun || e.BEnd >= f.B.Length ||
            !DonorRangeValid(f, 0, e.BEnd) || f.B[e.BEnd - 1].End - f.B[0].Start < 1000 ||
            !IsSentenceEnd(f.B[e.BEnd - 1]) || f.B[e.BEnd - 1].End > f.Chunk.Baseline[0].StartMs ||
            !StableAnchor(f.Ops[1], f.A, f.B, nonzero: true) ||
            previous.Chunk.OffsetMs + previous.Chunk.Baseline[^1].EndMs > f.Chunk.OffsetMs + f.B[0].Start ||
            !StableAnchor(previous.Ops[^1], previous.A, previous.B, tail: true, nonzero: true))
            return null;
        return BuildCandidate(f, AsrOmissionKind.Seam, 0, 0, e.BEnd, f.B[e.BEnd], f.Chunk.Baseline[0].StartMs);
    }

    private static IEnumerable<Candidate> ShortCompletions(Frame f, Dictionary<(int, int), Decision> ignored)
    {
        for (int k = 1; k < f.Ops.Length - 1; k++)
        {
            Edit e = f.Ops[k];
            if (e.Operation != Operation.Insert || !Internal(f, e) || e.BEnd - e.BStart < 2 ||
                ignored.GetValueOrDefault((e.BStart, e.BEnd)) != Decision.ShortInsertion ||
                !StableAnchor(f.Ops[k - 1], f.A, f.B, tail: true, nonzero: true) ||
                !StableAnchor(f.Ops[k + 1], f.A, f.B, nonzero: true))
                continue;
            Word left = f.A[e.AStart - 1], preceding = f.B[e.BStart - 1], last = f.B[e.BEnd - 1];
            Word[] words = f.B[e.BStart..e.BEnd];
            if (IsSentenceEnd(left) || IsSentenceEnd(preceding) || !IsSentenceEnd(last) ||
                words[..^1].Any(w => IsSentenceEnd(w)) || words.Any(w => w.Key.Length == 0 || w.End <= w.Start ||
                    w.Cue != preceding.Cue || !Score(w.Probability, .80)) ||
                !Close(left.End, preceding.End, BoundaryDrift) || !Close(words[0].Start, preceding.End, BoundaryDrift) ||
                (decimal)last.End - words[0].Start > MinimumSpan)
                continue;
            Candidate? p = BuildCandidate(f, AsrOmissionKind.ShortCompletion, e.AStart, e.BStart, e.BEnd, f.B[e.BEnd]);
            if (p is { FollowingCue: { } cue, FollowingStart: { } next } &&
                next - f.Chunk.Baseline[cue].StartMs is >= MinimumSpan and <= 3000)
                yield return p;
        }
    }

    private static bool OrderedWords(Word[] words) => words.All(w => w.End > w.Start && w.Key.Length > 0) &&
        words.Zip(words.Skip(1), (left, right) => left.End <= right.Start).All(valid => valid);

    private static double MedianScore(Word[] words)
    {
        double[] values = words.Select(w => w.Probability!.Value).Order().ToArray();
        int half = values.Length / 2;
        return values.Length % 2 == 0 ? (values[half - 1] + values[half]) / 2 : values[half];
    }

    private static Candidate? CompletionAtCueHead(Frame f, Edit e, Edit right)
    {
        Word[] words = f.B[e.BStart..e.BEnd];
        Word left = f.A[e.AStart - 1], preceding = f.B[e.BStart - 1], following = f.B[e.BEnd];
        if (words.Length is < 2 or > 4 || right.AEnd - right.AStart < 5 ||
            (decimal)words[^1].End - words[0].Start is < 200 or > 1000 || preceding.Cue + 1 != following.Cue ||
            words.Any(w => w.Cue != following.Cue || !Score(w.Probability, .45)) ||
            IsSentenceEnd(left) || IsSentenceEnd(preceding) || !IsSentenceEnd(words[^1]) ||
            words[..^1].Any(w => IsSentenceEnd(w)) || !OrderedWords(words) ||
            !Close(left.End, preceding.End, BoundaryDrift) || !Close(words[0].Start, preceding.End, BoundaryDrift) ||
            MedianScore(words) < .90 || !Score(words[^1].Probability, .90))
            return null;
        for (int n = 0; n < 5; n++)
        {
            Word a = f.A[e.AStart + n], b = f.B[e.BEnd + n];
            if (n < 2 ? (decimal)b.Start - a.Start < 200 : !Close(a.Start, b.Start, BoundaryDrift) || !Close(a.End, b.End, BoundaryDrift))
                return null;
        }
        Candidate? p = BuildCandidate(f, AsrOmissionKind.CueHeadCompletion, e.AStart, e.BStart, e.BEnd, following,
            splitForDisplay: false);
        return p is { FollowingCue: { } cue, FollowingStart: { } next } &&
            next - f.Chunk.Baseline[cue].StartMs is >= 200 and <= 1200 ? p : null;
    }

    private static IEnumerable<Candidate> CompleteCuesInGap(Frame f, Edit e)
    {
        long before = f.Chunk.Baseline[f.A[e.AStart - 1].Cue].EndMs, after = f.Chunk.Baseline[f.A[e.AStart].Cue].StartMs;
        if (after - before < MinimumSpan)
            yield break;
        long lastEnd = before;
        for (int lo = e.BStart; lo < e.BEnd;)
        {
            int hi = lo + 1;
            while (hi < e.BEnd && f.B[hi].Cue == f.B[lo].Cue)
                hi++;
            int first = lo;
            lo = hi;
            if (f.B[first - 1].Cue == f.B[first].Cue || f.B[hi - 1].Cue == f.B[hi].Cue || !DonorRangeValid(f, first, hi))
                continue;
            long start = f.B[first].Start, end = f.B[hi - 1].End;
            Word[] lexical = f.B[first..hi].Where(w => w.Key.Length > 0).ToArray();
            if (start < lastEnd || end > after || end - start < 500 || lexical.Length < StableRun ||
                lexical.Any(w => !Score(w.Probability)) || lexical.Average(w => w.Probability!.Value) < .80 ||
                WordTiming(f.B, first, hi).Coverage < .80)
                continue;
            if (BuildCandidate(f, AsrOmissionKind.CompleteCueInGap, e.AStart, first, hi, f.B[e.BEnd], after,
                splitForDisplay: false) is { } candidate)
            {
                lastEnd = candidate.End;
                yield return candidate;
            }
        }
    }

    private static Candidate? EarlyOnsetRepair(Frame f, Edit e, Edit right)
    {
        Word[] words = f.B[e.BStart..e.BEnd];
        Word left = f.A[e.AStart - 1], leftB = f.B[e.BStart - 1], nextA = f.A[e.AStart], nextB = f.B[e.BEnd];
        long seam = f.Chunk.Baseline[left.Cue].EndMs, first = words[0].Start, end = words[^1].End;
        if (words.Length is < 3 or > 10 || IsSentenceEnd(left) || IsSentenceEnd(leftB) || !Score(nextA.Probability) ||
            nextA.Probability > .05 || words.Any(w => !Score(w.Probability, .45)) || MedianScore(words) < .90 ||
            (decimal)end - first is < 500 or > 5000 || !OrderedWords(words) || !DonorRangeValid(f, e.BStart, e.BEnd) ||
            WordTiming(f.B, e.BStart, e.BEnd).Coverage < .90 || !Close(left.End, seam, BoundaryDrift) ||
            !Close(leftB.End, seam, BoundaryDrift) || !Close(first, seam, BoundaryDrift) || !Close(nextA.Start, seam, BoundaryDrift) ||
            nextA.End <= nextA.Start || nextA.End > end || end > nextB.Start || !Close(nextB.Start, end, BoundaryDrift))
            return null;
        int consecutive = 0;
        for (int n = 2; n < Math.Min(AnchorWords, right.AEnd - right.AStart); n++)
        {
            Word a = f.A[e.AStart + n], b = f.B[e.BEnd + n];
            consecutive = Close(a.Start, b.Start, BoundaryDrift) && Close(a.End, b.End, BoundaryDrift) ? consecutive + 1 : 0;
            if (consecutive >= StableRun)
                return BuildCandidate(f, AsrOmissionKind.EarlyOnsetRepair, e.AStart, e.BStart, e.BEnd, nextB, splitForDisplay: false);
        }
        return null;
    }
}
