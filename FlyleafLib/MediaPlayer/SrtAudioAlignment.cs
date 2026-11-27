using System.Net;
using System.Text.RegularExpressions;
using RegexMatch = System.Text.RegularExpressions.Match;

namespace FlyleafLib.MediaPlayer;

#nullable enable

public enum SrtAlignmentRejection
{
    None,
    NoText,
    MissingSpeech,
    Unmatched,
    Ambiguous,
    InvalidTiming,
    ConflictingOrder
}

public sealed record SrtAlignmentProposal(
    SrtTimingCue Cue, TimeSpan? Start, TimeSpan? End, double Coverage, SrtAlignmentRejection Rejection)
{
    public bool IsExactAnchor { get; init; }
}

/// <summary>Matches authored subtitle text to timed speech words without modifying the original cues.</summary>
public static partial class SrtAudioAlignment
{
    private const int MaxCandidateLocations = 512;
    private const int MaxApproximateCueWords = 96;
    private const int MaxLocalContext = 64;
    private static readonly TimeSpan MaxWordGap = TimeSpan.FromSeconds(2);

    private readonly record struct SpeechToken(string Text, TimeSpan Start, TimeSpan End, int Word, int Gaps);
    private readonly record struct Match(int First, int Last, int Matched);
    private readonly record struct SearchResult(Match? Match, SrtAlignmentRejection Rejection)
    {
        public static SearchResult Found(Match match) => new(match, SrtAlignmentRejection.None);
        public static SearchResult Rejected(SrtAlignmentRejection rejection) => new(null, rejection);
    }

    public static IReadOnlyList<SrtAlignmentProposal> Align(IReadOnlyList<SrtTimingCue> cues,
        IReadOnlyList<WordTiming> words, TimeSpan duration, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(words);
        token.ThrowIfCancellationRequested();
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        List<SpeechToken>? speech = BuildSpeech(words, duration, token);
        if (speech is null || speech.Count == 0)
        {
            var rejection = speech is null ? SrtAlignmentRejection.InvalidTiming : SrtAlignmentRejection.MissingSpeech;
            var unavailable = new SrtAlignmentProposal[cues.Count];
            for (int i = 0; i < cues.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                unavailable[i] = new(cues[i], null, null, 0, rejection);
            }
            return unavailable;
        }

        var index = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < speech.Count; i++)
        {
            if ((i & 255) == 0)
                token.ThrowIfCancellationRequested();
            if (!index.TryGetValue(speech[i].Text, out var positions))
                index.Add(speech[i].Text, positions = []);
            positions.Add(i);
        }

        var texts = new string[cues.Count][];
        var matches = new SearchResult[cues.Count];
        for (int i = 0; i < cues.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            string[] text = texts[i] = CueWords(cues[i].Text);
            matches[i] = text.Length switch
            {
                0 => SearchResult.Rejected(SrtAlignmentRejection.NoText),
                1 => SearchResult.Rejected(SrtAlignmentRejection.Unmatched),
                _ => FindExact(text, speech, index, 0, speech.Count, token)
            };
        }
        RemoveConflicts(matches, speech, token);

        // Anchor only at unique whole-phrase matches. New proposals can never enlarge a later search window.
        var nextAnchors = new Match?[cues.Count];
        var exactAnchors = new bool[cues.Count];
        Match? next = null;
        for (int i = matches.Length - 1; i >= 0; i--)
        {
            nextAnchors[i] = next;
            exactAnchors[i] = matches[i].Match.HasValue;
            next = matches[i].Match ?? next;
        }
        Match? previous = null;
        for (int i = 0; i < matches.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (matches[i].Match is { } anchor)
            {
                previous = anchor;
                continue;
            }

            string[] text = texts[i];
            if (text.Length == 0 || matches[i].Rejection == SrtAlignmentRejection.ConflictingOrder)
                continue;
            int first = previous is { } left ? left.Last + 1 : 0;
            int limit = nextAnchors[i]?.First ?? speech.Count;
            bool hasLocalContext = previous is not null && nextAnchors[i] is not null && limit - first <= MaxLocalContext;
            if (text.Length == 1 && !hasLocalContext)
                continue;

            SearchResult exact = first != 0 || limit != speech.Count
                ? FindExact(text, speech, index, first, limit, token)
                : matches[i];
            matches[i] = exact.Rejection == SrtAlignmentRejection.Unmatched
                ? FindApproximate(text, speech, index, first, limit, hasLocalContext, token)
                : exact;
        }
        RemoveConflicts(matches, speech, token);

        var proposals = new SrtAlignmentProposal[cues.Count];
        for (int i = 0; i < proposals.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            SearchResult result = matches[i];
            proposals[i] = result.Match is { } match
                ? new(cues[i], Milliseconds(speech[match.First].Start), Milliseconds(speech[match.Last].End),
                    (double)match.Matched / texts[i].Length, SrtAlignmentRejection.None) { IsExactAnchor = exactAnchors[i] }
                : new(cues[i], null, null, 0, result.Rejection);
        }
        return proposals;
    }

    private static List<SpeechToken>? BuildSpeech(IReadOnlyList<WordTiming> words, TimeSpan duration,
        CancellationToken token)
    {
        var speech = new List<SpeechToken>(words.Count);
        TimeSpan previousStart = TimeSpan.MinValue;
        for (int i = 0; i < words.Count; i++)
        {
            if ((i & 255) == 0)
                token.ThrowIfCancellationRequested();

            WordTiming word = words[i];
            if (word.Start < TimeSpan.Zero || word.Start < previousStart || word.End <= word.Start || word.End > duration)
                return null;
            previousStart = word.Start;
            foreach (RegexMatch wordToken in WordPattern().Matches(word.Word.Replace('’', '\'')))
            {
                int gaps = speech.Count == 0 ? 0
                    : speech[^1].Gaps + (word.Start - speech[^1].End > MaxWordGap ? 1 : 0);
                speech.Add(new(wordToken.Value.ToLowerInvariant(), word.Start, word.End, i, gaps));
            }
        }
        return speech;
    }

    private static string[] CueWords(string text)
    {
        string normalized = WebUtility.HtmlDecode(text);
        normalized = SubtitleMarkup().Replace(normalized, "");
        normalized = LeadingSpeaker().Replace(normalized, "").Replace('’', '\'');
        MatchCollection matches = WordPattern().Matches(normalized);
        var result = new string[matches.Count];
        for (int i = 0; i < matches.Count; i++)
            result[i] = matches[i].Value.ToLowerInvariant();
        return result;
    }

    private static SearchResult FindExact(string[] cue, List<SpeechToken> speech,
        Dictionary<string, List<int>> index, int first, int limit, CancellationToken token)
    {
        if (cue.Length > limit - first)
            return SearchResult.Rejected(SrtAlignmentRejection.Unmatched);

        List<int> locations = [];
        int anchorOffset = 0, anchorStart = 0, anchorCount = int.MaxValue;
        for (int offset = 0; offset < cue.Length; offset++)
        {
            if (!index.TryGetValue(cue[offset], out var positions))
                return SearchResult.Rejected(SrtAlignmentRejection.Unmatched);
            int start = LowerBound(positions, first + offset);
            int count = LowerBound(positions, limit - cue.Length + 1 + offset) - start;
            if (count < anchorCount)
            {
                locations = positions;
                anchorOffset = offset;
                anchorStart = start;
                anchorCount = count;
            }
            if (anchorCount <= 1)
                break;
        }
        if (anchorCount > MaxCandidateLocations)
            return SearchResult.Rejected(SrtAlignmentRejection.Ambiguous);

        Match? match = null;
        for (int i = anchorStart; i < anchorStart + anchorCount; i++)
        {
            token.ThrowIfCancellationRequested();
            int start = locations[i] - anchorOffset, end = start + cue.Length - 1;
            if (!ValidInterval(speech, start, end) || !EqualsWords(cue, speech, start))
                continue;
            if (match is not null)
                return SearchResult.Rejected(SrtAlignmentRejection.Ambiguous);
            match = new(start, end, cue.Length);
        }
        return match is { } found ? SearchResult.Found(found) : SearchResult.Rejected(SrtAlignmentRejection.Unmatched);
    }

    private static SearchResult FindApproximate(string[] cue, List<SpeechToken> speech,
        Dictionary<string, List<int>> index, int first, int limit, bool hasLocalContext, CancellationToken token)
    {
        // Both boundary words are required. Within this bounded window, insertion/deletion and word-order
        // differences count against the source cue's matched-word fraction.
        if (cue.Length is < 5 or > MaxApproximateCueWords)
            return SearchResult.Rejected(SrtAlignmentRejection.Unmatched);
        int minimumWords = cue.Length - cue.Length / 5;
        int maximumWords = cue.Length + cue.Length / 4;
        if (limit - first < minimumWords)
            return SearchResult.Rejected(SrtAlignmentRejection.Unmatched);
        int begin = first, end = limit - minimumWords + 1;
        List<int>? positions = null;
        if (!hasLocalContext)
        {
            if (!index.TryGetValue(cue[0], out positions))
                return SearchResult.Rejected(SrtAlignmentRejection.Unmatched);
            begin = LowerBound(positions, first);
            end = LowerBound(positions, end);
        }
        if (end - begin > MaxCandidateLocations)
            return SearchResult.Rejected(SrtAlignmentRejection.Ambiguous);

        Match? best = null;
        bool tied = false;
        for (int i = begin; i < end; i++)
        {
            token.ThrowIfCancellationRequested();
            int start = positions is null ? i : positions[i];
            bool exactStart = speech[start].Text == cue[0];
            if (!exactStart && !DiffersByAdjacentDoubleLetter(speech[start].Text, cue[0]))
                continue;
            int last = Math.Min(start + maximumWords - 1, limit - 1);
            MatchApproximateWindow(cue, speech, start, start + minimumWords - 1, last, exactStart, hasLocalContext,
                ref best, ref tied);
        }
        return tied ? SearchResult.Rejected(SrtAlignmentRejection.Ambiguous)
            : best is { } match ? SearchResult.Found(match) : SearchResult.Rejected(SrtAlignmentRejection.Unmatched);
    }

    private static void MatchApproximateWindow(string[] cue, List<SpeechToken> speech, int start,
        int minimumEnd, int maximumEnd, bool exactStart, bool hasLocalContext, ref Match? best, ref bool tied)
    {
        if (speech[start].Word == (start == 0 ? -1 : speech[start - 1].Word))
            return;
        while (maximumEnd >= minimumEnd && speech[maximumEnd].Gaps != speech[start].Gaps)
            maximumEnd--;
        if (maximumEnd < minimumEnd)
            return;

        Span<int> common = stackalloc int[maximumEnd - start];
        common.Clear();
        for (int cueIndex = 1; cueIndex < cue.Length - 1; cueIndex++)
        {
            int diagonal = 0;
            for (int offset = 1; offset < common.Length; offset++)
            {
                int above = common[offset];
                common[offset] = cue[cueIndex] == speech[start + offset].Text
                    ? diagonal + 1 : Math.Max(above, common[offset - 1]);
                diagonal = above;
            }
        }

        int required = cue.Length - cue.Length / 5;
        for (int end = minimumEnd; end <= maximumEnd; end++)
        {
            bool exactEnd = cue[^1] == speech[end].Text;
            if (!exactEnd && (!hasLocalContext || !exactStart || !DiffersByAdjacentDoubleLetter(cue[^1], speech[end].Text)))
                continue;
            if (!ValidInterval(speech, start, end))
                continue;
            // A spelling variation can locate a single boundary between exact anchors but is not exact evidence.
            int matched = common[end - start - 1] + (exactStart ? 1 : 0) + (exactEnd ? 1 : 0);
            if (matched < required)
                continue;
            if (best is null || matched > best.Value.Matched)
            {
                best = new(start, end, matched);
                tied = false;
            }
            else if (matched == best.Value.Matched)
            {
                tied = true;
            }
        }
    }

    private static bool DiffersByAdjacentDoubleLetter(string left, string right)
    {
        string shorter = left.Length < right.Length ? left : right;
        string longer = left.Length < right.Length ? right : left;
        if (shorter.Length < 6 || longer.Length != shorter.Length + 1)
            return false;

        int commonPrefix = 0;
        while (commonPrefix < shorter.Length && shorter[commonPrefix] == longer[commonPrefix])
            commonPrefix++;
        return commonPrefix > 0 && longer[commonPrefix - 1] == longer[commonPrefix]
            && shorter.AsSpan(commonPrefix).SequenceEqual(longer.AsSpan(commonPrefix + 1));
    }

    private static void RemoveConflicts(SearchResult[] matches, List<SpeechToken> speech, CancellationToken token)
    {
        // Preserve all evidence until both directions have considered it. Picking either side of a
        // crossing would silently assign its words at the expense of another authored cue.
        var conflict = new bool[matches.Length];
        int farthestLast = -1;
        TimeSpan latestEnd = TimeSpan.MinValue;
        for (int i = 0; i < matches.Length; i++)
        {
            if ((i & 255) == 0)
                token.ThrowIfCancellationRequested();
            if (matches[i].Match is not { } match)
                continue;
            conflict[i] = match.First <= farthestLast || speech[match.First].Start < latestEnd;
            farthestLast = Math.Max(farthestLast, match.Last);
            if (speech[match.Last].End > latestEnd)
                latestEnd = speech[match.Last].End;
        }

        int earliestFirst = speech.Count;
        TimeSpan earliestStart = TimeSpan.MaxValue;
        for (int i = matches.Length - 1; i >= 0; i--)
        {
            if ((i & 255) == 0)
                token.ThrowIfCancellationRequested();
            if (matches[i].Match is not { } match)
                continue;
            if (conflict[i] || match.Last >= earliestFirst || speech[match.Last].End > earliestStart)
                matches[i] = SearchResult.Rejected(SrtAlignmentRejection.ConflictingOrder);
            earliestFirst = Math.Min(earliestFirst, match.First);
            if (speech[match.First].Start < earliestStart)
                earliestStart = speech[match.First].Start;
        }
    }

    private static bool ValidInterval(List<SpeechToken> speech, int first, int last)
        => speech[first].Gaps == speech[last].Gaps
            && (first == 0 || speech[first - 1].Word != speech[first].Word)
            && (last == speech.Count - 1 || speech[last + 1].Word != speech[last].Word)
            && Milliseconds(speech[last].End) > Milliseconds(speech[first].Start);

    private static bool EqualsWords(string[] cue, List<SpeechToken> speech, int start)
    {
        for (int i = 0; i < cue.Length; i++)
            if (cue[i] != speech[start + i].Text)
                return false;
        return true;
    }

    private static int LowerBound(List<int> positions, int value)
    {
        int index = positions.BinarySearch(value);
        return index < 0 ? ~index : index;
    }

    private static TimeSpan Milliseconds(TimeSpan time)
        => TimeSpan.FromTicks(time.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond);

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+(?:'[\p{L}\p{M}\p{N}]+)*")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"</?[A-Za-z][^<>\r\n]*>|\{\\[^{}\r\n]*\}")]
    private static partial Regex SubtitleMarkup();

    [GeneratedRegex(@"^[\t ]*(?:[-–—][\t ]+)?(?:\[[^\]\r\n]{1,80}\][\t ]*)+", RegexOptions.Multiline)]
    private static partial Regex LeadingSpeaker();
}
