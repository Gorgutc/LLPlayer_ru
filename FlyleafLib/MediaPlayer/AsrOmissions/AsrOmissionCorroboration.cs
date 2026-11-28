using System.Linq;
using System.Text;

#nullable enable

namespace FlyleafLib.MediaPlayer.AsrOmissions;

public static partial class AsrOmissionDetector
{
    private const int CorroborationTolerance = 700;
    private const int MaximumConfirmedSpan = 8_000;
    private const int MaximumWordPause = 650;

    private readonly record struct Atom(string Text, double Time, double Probability);

    private readonly record struct Prefix(int Start, int Count);

    private readonly record struct Confirmed(int Start, int End, long Offset, bool WholeCue);

    private sealed class ConfirmationTrack
    {
        private readonly Dictionary<string, List<int>> _indices = new(StringComparer.Ordinal);
        private readonly List<Atom> _atoms = [];

        public ConfirmationTrack(AsrOmissionConfirmation confirmation, long duration)
        {
            Offset = confirmation.OffsetMs;
            foreach (Word word in Flatten(confirmation.Cues))
            {
                string[] terms = LexicalAtoms(word.Text);
                if (terms.Length == 0)
                    continue;
                if (!Score(word.Probability) || word.Start < 0 || word.End < word.Start || word.End > duration - Offset)
                {
                    // A word with unusable timing or confidence cannot join text on its two sides.
                    _atoms.Add(new(string.Empty, 0, 0));
                    continue;
                }

                foreach (Atom atom in ClockAtoms(word, terms, Offset))
                {
                    if (!_indices.TryGetValue(atom.Text, out List<int>? indices))
                        _indices.Add(atom.Text, indices = []);
                    indices.Add(_atoms.Count);
                    _atoms.Add(atom);
                }
            }
        }

        public long Offset { get; }

        public IEnumerable<Prefix> MatchingPrefixes(IReadOnlyList<Atom> donor)
        {
            if (!_indices.TryGetValue(donor[0].Text, out List<int>? starts))
                yield break;
            foreach (int start in starts)
            {
                if (Math.Abs(donor[0].Time - _atoms[start].Time) > CorroborationTolerance)
                    continue;
                int count = 0;
                while (count < donor.Count && start + count < _atoms.Count)
                {
                    Atom probe = _atoms[start + count], word = donor[count];
                    if (word.Text != probe.Text || Math.Abs(word.Time - probe.Time) > CorroborationTolerance)
                        break;
                    count++;
                }
                if (count >= 3)
                    yield return new(start, count);
            }
        }

        public double Mean(int first, int count)
        {
            double sum = 0;
            for (int i = first; i < first + count; i++)
                sum += _atoms[i].Probability;
            return sum / count;
        }
    }

    private static string[] LexicalAtoms(string text)
    {
        List<string> units = [];
        StringBuilder current = new();
        void Flush()
        {
            if (current.Length == 0)
                return;
            units.Add(current.ToString());
            current.Clear();
        }

        foreach (Rune rune in Fold(text.Normalize(NormalizationForm.FormKC)).EnumerateRunes())
        {
            if (!Lexical(rune))
                Flush();
            else if (Unspaced(rune.Value))
            {
                Flush();
                units.Add(rune.Value is >= 0x30a1 and <= 0x30f6 ? new Rune(rune.Value - 0x60).ToString() : rune.ToString());
            }
            else
                current.Append(rune.ToString());
        }
        Flush();
        return units.ToArray();
    }

    private static bool Unspaced(int value) => value is >= 0x3040 and <= 0x309f or >= 0x30a1 and <= 0x30fa or
        >= 0x30fd and <= 0x30ff or >= 0x31f0 and <= 0x31ff or >= 0x1b000 and <= 0x1b16f or
        >= 0x3400 and <= 0x4dbf or >= 0x4e00 and <= 0x9fff or >= 0xf900 and <= 0xfaff or
        >= 0x20000 and <= 0x323af or >= 0x1100 and <= 0x11ff or >= 0x3130 and <= 0x318f or
        >= 0xa960 and <= 0xa97f or >= 0xac00 and <= 0xd7ff;

    private static Atom[] ClockAtoms(Word word, string[] units, long offset = 0)
    {
        Atom[] atoms = new Atom[units.Length];
        for (int i = 0; i < units.Length; i++)
            atoms[i] = new(units[i], offset + word.Start + (word.End - word.Start) * (i + .5) / units.Length,
                word.Probability!.Value);
        return atoms;
    }

    private static List<AsrOmissionInterval> Merge(IEnumerable<AsrOmissionInterval> intervals)
    {
        List<AsrOmissionInterval> result = [];
        foreach (AsrOmissionInterval interval in intervals.Where(x => x.EndMs > x.StartMs).OrderBy(x => x.StartMs))
        {
            if (result.Count > 0 && interval.StartMs <= result[^1].EndMs)
                result[^1] = result[^1] with { EndMs = Math.Max(result[^1].EndMs, interval.EndMs) };
            else
                result.Add(interval);
        }
        return result;
    }

    private static bool Intersects(long start, long end, IEnumerable<AsrOmissionInterval> intervals) =>
        intervals.Any(x => x.StartMs < end && start < x.EndMs);

    private static void Corroborate(Frame frame, List<Candidate> selected, CancellationToken token)
    {
        if (frame.Chunk.Confirmations is not { Count: > 0 } confirmations ||
            frame.Chunk.VadIntervals is not { Count: > 0 } nativeVad)
            return;
        long duration = frame.Chunk.DurationMs;
        List<AsrOmissionInterval> vad = Merge(nativeVad.Where(x => x.StartMs >= 0 && x.EndMs <= duration));
        if (vad.Count == 0)
            return;
        ConfirmationTrack[] tracks = confirmations.Where(c => c.OffsetMs >= 0 && c.OffsetMs < duration)
            .Select(c => new ConfirmationTrack(c, duration)).ToArray();
        if (tracks.Length == 0)
            return;

        List<AsrOmissionInterval> occupied = Merge(frame.A.Where(w => w.Start >= 0 && w.End <= duration)
            .Select(w => new AsrOmissionInterval(w.Start, w.End))
            .Concat(frame.Chunk.Baseline.Where(c => c.Words.Count == 0).Select(c => new AsrOmissionInterval(c.StartMs, c.EndMs)))
            .Concat(selected.Select(c => new AsrOmissionInterval(c.Start, c.End))));
        Dictionary<int, long> moves = selected.Where(c => c.Review is null && c.FollowingCue.HasValue && c.FollowingStart.HasValue)
            .GroupBy(c => c.FollowingCue!.Value).ToDictionary(g => g.Key, g => g.Last().FollowingStart!.Value);
        AsrOmissionInterval[] shown = frame.Chunk.Baseline.Select((c, i) =>
            new AsrOmissionInterval(moves.GetValueOrDefault(i, c.StartMs), c.EndMs)).ToArray();

        Word[] b = frame.B;
        List<AsrOmissionInterval> confirmed = [];
        foreach ((int lo, int hi) in FreeRuns(frame, occupied))
        {
            token.ThrowIfCancellationRequested();
            foreach (Confirmed candidate in FindConfirmations(b, lo, hi, tracks, vad, token))
            {
                long start = b[candidate.Start].Start, end = b[candidate.End - 1].End;
                if (Intersects(start, end, occupied) || Intersects(start, end, confirmed))
                    continue;
                AsrOmissionReviewReason? review = Intersects(start, end, shown) ? AsrOmissionReviewReason.BaselineCueAlreadyOnScreen :
                    !candidate.WholeCue ? AsrOmissionReviewReason.PartialDonorCue : null;
                selected.Add(new(AsrOmissionKind.ContextConfirmedGap, candidate.Start, candidate.End, start, end,
                    Review: review, ConfirmationOffset: candidate.Offset));
                confirmed.Add(new(start, end));
            }
        }
    }

    private static IEnumerable<(int Lo, int Hi)> FreeRuns(Frame frame, List<AsrOmissionInterval> occupied)
    {
        int lo = -1;
        Word[] words = frame.B;
        for (int i = 0; i < words.Length; i++)
        {
            Word word = words[i];
            bool usable = word.Start >= 0 && word.End >= word.Start && word.End <= frame.Chunk.DurationMs &&
                Score(word.Probability) && !Intersects(word.Start, word.End, occupied) && LexicalAtoms(word.Text).Length > 0;
            if (lo >= 0 && (!usable || word.Start - words[i - 1].End is < 0 or > MaximumWordPause))
            {
                yield return (lo, i);
                lo = -1;
            }
            if (usable && lo < 0)
                lo = i;
        }
        if (lo >= 0)
            yield return (lo, words.Length);
    }

    private static IEnumerable<Confirmed> FindConfirmations(Word[] words, int lo, int hi,
        ConfirmationTrack[] tracks, List<AsrOmissionInterval> vad, CancellationToken token)
    {
        Atom[][] units = words[lo..hi].Select(w => ClockAtoms(w, LexicalAtoms(w.Text))).ToArray();
        for (int first = lo; first < hi;)
        {
            token.ThrowIfCancellationRequested();
            long start = words[first].Start;
            List<int> lengths = [];
            List<Atom> atoms = [];
            for (int next = first; next < hi && words[next].End - start <= MaximumConfirmedSpan; next++)
            {
                atoms.AddRange(units[next - lo]);
                lengths.Add(atoms.Count);
            }
            if (atoms.Count == 0)
            {
                first++;
                continue;
            }
            Confirmed? whole = null, part = null;
            bool startsCue = first == 0 || words[first - 1].Cue != words[first].Cue;
            foreach (ConfirmationTrack track in tracks)
            {
                foreach (Prefix prefix in track.MatchingPrefixes(atoms))
                {
                    for (int n = lengths.Count - 1; n >= 0; n--)
                    {
                        int size = lengths[n], endWord = first + n + 1;
                        bool complete = startsCue && (endWord == words.Length || words[endWord - 1].Cue != words[endWord].Cue);
                        if (size < 3 || size > prefix.Count || !complete && words[first].Cue != words[endWord - 1].Cue ||
                            (complete ? whole : part) is { End: var bestEnd } && endWord <= bestEnd)
                            continue;
                        long end = words[endWord - 1].End;
                        if (end - start < 350 || atoms.Take(size).Sum(a => a.Text.EnumerateRunes().Count()) < 4 ||
                            vad.Sum(v => Overlap(start, end, v.StartMs, v.EndMs)) < .75 * (end - start) ||
                            words[first..endWord].Average(w => w.Probability!.Value) < .55 || track.Mean(prefix.Start, size) < .60)
                            continue;
                        if (complete)
                            whole = new(first, endWord, track.Offset, true);
                        else
                            part = new(first, endWord, track.Offset, false);
                    }
                }
            }
            if ((whole ?? part) is { } chosen)
            {
                yield return chosen;
                first = chosen.End;
            }
            else
                first++;
        }
    }
}
