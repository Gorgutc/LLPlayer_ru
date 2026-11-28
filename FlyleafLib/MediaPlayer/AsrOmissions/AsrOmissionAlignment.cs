using System.Globalization;
using System.Linq;
using System.Text;

#nullable enable

namespace FlyleafLib.MediaPlayer.AsrOmissions;

public static partial class AsrOmissionDetector
{
    private const int AnchorWords = 8;
    private const int AnchorTolerance = 750;
    private const int StableRun = 3;
    private const int BoundaryDrift = 120;
    private const int MinimumWords = 5;
    private const int MinimumSpan = 2_000;

    private sealed record Word(string Text, long Start, long End, double? Probability, int Cue,
        string RawKey, string Key);

    private enum Operation { Equal, Insert, Delete, Replace }

    private readonly record struct Edit(Operation Operation, int AStart, int AEnd, int BStart, int BEnd);

    private readonly record struct Match(int A, int B, int Count);

    private readonly record struct Window(int AStart, int AEnd, int BStart, int BEnd);

    private static Word[] Flatten(IReadOnlyList<AsrOmissionCue> cues)
    {
        List<Word> words = [];
        for (int i = 0; i < cues.Count; i++)
        {
            foreach (AsrOmissionWord word in cues[i].Words)
            {
                string rawKey = Token(word.Text);
                words.Add(new(word.Text, word.StartMs, word.EndMs, word.Probability, i,
                    rawKey, rawKey.Normalize(NormalizationForm.FormKC)));
            }
        }
        return words.ToArray();
    }

    private static bool Lexical(Rune rune) => Rune.IsLetterOrDigit(rune) ||
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;

    private static string Fold(string value) => value.ToLowerInvariant()
        .Replace("ß", "ss", StringComparison.Ordinal).Replace('ς', 'σ');

    private static string Token(string text)
    {
        string folded = Fold(text);
        int offset = 0, first = -1, last = 0;
        foreach (Rune rune in folded.EnumerateRunes())
        {
            if (Lexical(rune) || rune.Value == '_')
            {
                if (first < 0)
                    first = offset;
                last = offset + rune.Utf16SequenceLength;
            }
            offset += rune.Utf16SequenceLength;
        }
        return first < 0 ? string.Empty : folded[first..last];
    }

    private static Edit[] Align(Word[] a, Word[] b, bool legacyKeys, CancellationToken cancellationToken)
    {
        string[] left = a.Select(w => legacyKeys ? w.RawKey : w.Key).ToArray();
        string[] right = b.Select(w => legacyKeys ? w.RawKey : w.Key).ToArray();
        Dictionary<string, List<int>> positions = new(StringComparer.Ordinal);
        for (int j = 0; j < right.Length; j++)
        {
            if (!positions.TryGetValue(right[j], out List<int>? indexes))
                positions.Add(right[j], indexes = []);
            indexes.Add(j);
        }

        List<Match> matches = [];
        Stack<Window> windows = new();
        windows.Push(new(0, left.Length, 0, right.Length));
        while (windows.TryPop(out Window window))
        {
            Match match = LongestMatch(left, positions, window, cancellationToken);
            if (match.Count == 0)
                continue;
            matches.Add(match);
            if (window.AStart < match.A && window.BStart < match.B)
                windows.Push(window with { AEnd = match.A, BEnd = match.B });
            if (match.A + match.Count < window.AEnd && match.B + match.Count < window.BEnd)
                windows.Push(window with { AStart = match.A + match.Count, BStart = match.B + match.Count });
        }
        matches.Sort((x, y) => x.A.CompareTo(y.A));

        List<Edit> edits = [];
        int i = 0, jNext = 0;
        matches.Add(new(left.Length, right.Length, 0));
        foreach (Match match in matches)
        {
            if (i < match.A || jNext < match.B)
            {
                Operation operation = i == match.A ? Operation.Insert :
                    jNext == match.B ? Operation.Delete : Operation.Replace;
                edits.Add(new(operation, i, match.A, jNext, match.B));
            }
            i = match.A + match.Count;
            jNext = match.B + match.Count;
            if (match.Count == 0)
                continue;
            if (edits.Count > 0 && edits[^1] is { Operation: Operation.Equal } prior &&
                prior.AEnd == match.A && prior.BEnd == match.B)
                edits[^1] = prior with { AEnd = i, BEnd = jNext };
            else
                edits.Add(new(Operation.Equal, match.A, i, match.B, jNext));
        }
        return edits.ToArray();
    }

    // Longest contiguous block, with early A and B ties taking priority. Recursing into the
    // unmatched windows preserves the offline detector's order without an unbounded call stack.
    private static Match LongestMatch(string[] left, Dictionary<string, List<int>> positions, Window window,
        CancellationToken cancellationToken)
    {
        Match best = new(window.AStart, window.BStart, 0);
        Dictionary<int, int> prior = [];
        Dictionary<int, int> current = [];
        for (int i = window.AStart; i < window.AEnd; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current.Clear();
            if (positions.TryGetValue(left[i], out List<int>? indexes))
            {
                int next = indexes.BinarySearch(window.BStart);
                if (next < 0)
                    next = ~next;
                for (; next < indexes.Count && indexes[next] < window.BEnd; next++)
                {
                    int j = indexes[next];
                    int length = prior.GetValueOrDefault(j - 1) + 1;
                    current.Add(j, length);
                    if (length > best.Count)
                        best = new(i + 1 - length, j + 1 - length, length);
                }
            }
            (prior, current) = (current, prior);
        }
        return best;
    }

    private static bool Close(long a, long b, long tolerance) => Math.Abs((decimal)a - b) <= tolerance;

    private static bool StableAnchor(Edit edit, Word[] a, Word[] b, bool tail = false, bool nonzero = false)
    {
        if (edit.Operation != Operation.Equal)
            return false;
        int consecutive = 0, inspected = 0;
        for (int n = 0; n < edit.AEnd - edit.AStart && inspected < AnchorWords; n++)
        {
            Word left = a[tail ? edit.AEnd - 1 - n : edit.AStart + n];
            Word right = b[tail ? edit.BEnd - 1 - n : edit.BStart + n];
            if (nonzero && (left.End <= left.Start || right.End <= right.Start))
                continue;
            inspected++;
            consecutive = Close(left.Start, right.Start, AnchorTolerance) ? consecutive + 1 : 0;
            if (consecutive >= StableRun)
                return true;
        }
        return false;
    }

    private static bool IsSentenceEnd(Word word, bool legacy = false)
    {
        string text = word.Text.TrimEnd().TrimEnd(legacy ? "\"')]}".ToCharArray() : "\"')]}”’»」』】）".ToCharArray());
        return text.Length > 0 && (text[^1] is '.' or '!' or '?' ||
            !legacy && text[^1] is '。' or '！' or '？' or '؟' or '।');
    }

    private static bool Score(double? probability, double min = 0) => probability is { } value &&
        value >= min && value <= 1;
}
