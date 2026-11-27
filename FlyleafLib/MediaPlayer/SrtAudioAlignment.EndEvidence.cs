namespace FlyleafLib.MediaPlayer;

#nullable enable

public static partial class SrtAudioAlignment
{
    public static SpeechEndTrack? ReadSpeechEnds(IReadOnlyList<SrtAlignmentProposal> proposals,
        IReadOnlyList<WordTiming> words, TimeSpan duration, CancellationToken token)
    {
        return SpeechEndTrack.Create(proposals, words, duration, token);
    }

    /// <summary>A nonliteral occurrence can disprove a candidate without supplying a replacement timing.</summary>
    public static bool HasRepeatChallenger(SrtTimingCue cue, IReadOnlyList<WordTiming> words, TimeSpan duration,
        CancellationToken token)
    {
        string[] text = CueWords(cue.Text);
        if (text.Length < 5 || BuildSpeech(words, duration, token) is not { } speech)
            return false;

        for (int omitted = 1; omitted <= 2; omitted++)
        {
            int shared = text.Length - omitted;
            if (shared < 4 || shared * 3 < text.Length * 2)
                continue;
            for (int i = 0; i + shared < speech.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (!ValidInterval(speech, i, i + shared))
                    continue;
                if ((NearLexicalEdge(text[shared], speech[i + shared].Text) && EqualsWords(text.AsSpan(0, shared), speech, i)) ||
                    (NearLexicalEdge(text[omitted - 1], speech[i].Text) && EqualsWords(text.AsSpan(omitted), speech, i + 1)))
                    return true;
            }
        }
        return false;
    }

    private static bool NearLexicalEdge(string authored, string observed)
    {
        string shorter = authored.Length < observed.Length ? authored : observed;
        string longer = authored.Length < observed.Length ? observed : authored;
        return shorter.Length >= 4 && longer.Length - shorter.Length is > 0 and <= 2 &&
            longer.StartsWith(shorter, StringComparison.Ordinal);
    }

    public sealed class SpeechEndTrack
    {
        private const int MaxEdgeWords = 64;
        private static readonly TimeSpan EdgeTolerance = TimeSpan.FromMilliseconds(750);
        private readonly IReadOnlyList<SrtAlignmentProposal> proposals;
        private readonly List<SpeechToken> speech;
        private readonly Dictionary<string, List<int>> index;
        private readonly string[][] texts;
        private readonly SearchResult[] literal;

        private SpeechEndTrack(IReadOnlyList<SrtAlignmentProposal> proposals,
            List<SpeechToken> speech, CancellationToken token)
        {
            this.proposals = proposals;
            this.speech = speech;
            index = IndexSpeech(speech, token);
            texts = new string[proposals.Count][];
            literal = new SearchResult[proposals.Count];
            for (int i = 0; i < proposals.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string[] text = texts[i] = CueWords(proposals[i].Cue.Text);
                literal[i] = text.Length >= 2 ? FindExact(text, speech, index, 0, speech.Count, token)
                    : SearchResult.Rejected(SrtAlignmentRejection.Unmatched);
            }

            var matches = (SearchResult[])literal.Clone();
            RemoveConflicts(matches, speech, token);
            var next = new Match?[matches.Length];
            Match? nextAnchor = null;
            for (int i = matches.Length - 1; i >= 0; i--)
            {
                next[i] = nextAnchor;
                nextAnchor = matches[i].Match ?? nextAnchor;
            }
            AddElidedRepeatAnchors(texts, matches, speech, index, next, token);
            AddInteriorInsertions(matches, index, next, token);
            RemoveConflicts(matches, speech, token);

            List<SrtSpeechEndEvidence> evidence = [];
            for (int i = 0; i < matches.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                if (matches[i].Match is not { } match || proposals[i].Rejection == SrtAlignmentRejection.ConflictingOrder)
                    continue;
                int onset = match.First;
                while (onset < match.Last && speech[onset].Word == speech[match.First].Word)
                    onset++;
                var first = speech[match.First];
                var second = speech[onset];
                TimeSpan onsetEnd = first.End - first.Start >= EdgeTolerance || second.End - second.Start >= EdgeTolerance
                    ? second.End : first.Start;
                evidence.Add(new(i, Milliseconds(first.Start), Milliseconds(onsetEnd), Milliseconds(speech[match.Last].End)));
            }
            Anchors = evidence.AsReadOnly();
        }

        public IReadOnlyList<SrtSpeechEndEvidence> Anchors { get; }

        internal static SpeechEndTrack? Create(IReadOnlyList<SrtAlignmentProposal> proposals,
            IReadOnlyList<WordTiming> words, TimeSpan duration, CancellationToken token)
        {
            var speech = BuildSpeech(words, duration, token);
            return speech is { Count: > 0 } ? new SpeechEndTrack(proposals, speech, token) : null;
        }

        private void AddInteriorInsertions(SearchResult[] matches, Dictionary<string, List<int>> index,
            Match?[] nextLiteral, CancellationToken token)
        {
            Match? prior = null;
            for (int i = 0; i < matches.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                if (matches[i].Match is { } existing)
                {
                    prior = existing;
                    continue;
                }
                string[] text = texts[i];
                if (matches[i].Rejection != SrtAlignmentRejection.Unmatched || text.Length is < 5 or > MaxApproximateCueWords ||
                    !index.TryGetValue(text[0], out var starts) || starts.Count > MaxCandidateLocations)
                    continue;

                Match? unique = null;
                foreach (int start in starts)
                {
                    token.ThrowIfCancellationRequested();
                    if (FindInteriorInsertion(text, start) is not { } match)
                        continue;
                    if (unique != null)
                    {
                        unique = null;
                        break;
                    }
                    unique = match;
                }
                if (unique is { } found && (prior is not { } before || ComesBefore(before, found, speech)) &&
                    (nextLiteral[i] is not { } after || ComesBefore(found, after, speech)))
                    matches[i] = SearchResult.Found(found);
            }
        }

        private Match? FindInteriorInsertion(string[] text, int start)
        {
            // A clock anchor can tolerate one extra recognized interior word if every authored word
            // survives in order. Both outer words must be literal, with intact acoustic boundaries.
            int position = start + 1;
            bool inserted = false;
            for (int i = 1; i < text.Length - 1 && position < speech.Count; i++, position++)
            {
                string observed = speech[position].Text;
                if (observed == text[i])
                    continue;
                if (inserted)
                    return null;
                inserted = true;
                // In "city it's ...", the recognizer put the extra pronoun and copula in one word.
                if (observed == "it's" && text[i] == "is")
                    continue;
                if (++position >= speech.Count || speech[position].Text != text[i])
                    return null;
            }
            return inserted && position < speech.Count && speech[position].Text == text[^1] &&
                ValidInterval(speech, start, position) ? new Match(start, position, text.Length) : null;
        }

        public bool CorroboratesEdge(int first, int last, IReadOnlyList<(TimeSpan Start, TimeSpan End)> intervals,
            CancellationToken token)
        {
            TimeSpan start = intervals[first].Start;
            TimeSpan end = intervals[last].End;
            var group = new List<string>();
            var cueEnds = new List<int>();
            for (int i = first; i <= last; i++)
            {
                token.ThrowIfCancellationRequested();
                bool sharedBefore = i > first && SharesBoundary(i - 1, start, end, token);
                if (proposals[i].Rejection == SrtAlignmentRejection.ConflictingOrder &&
                    !sharedBefore && !(i < last && SharesBoundary(i, start, end, token)))
                    return false;
                int priorCount = group.Count;
                foreach (string word in texts[i].AsSpan(sharedBefore ? 1 : 0))
                {
                    if (word is "um" or "uh")
                        continue;
                    if (group.Count == MaxEdgeWords)
                        return false;
                    group.Add(word);
                }
                if (group.Count - priorCount < 2)
                    return false;
                cueEnds.Add(group.Count);
            }

            int firstWord = speech.FindIndex(word => word.Start >= start - EdgeTolerance);
            if (firstWord < 0 || group.Count < 4)
                return false;
            int wordCount = 0;
            while (firstWord + wordCount < speech.Count && speech[firstWord + wordCount].End <= end + EdgeTolerance)
            {
                if (++wordCount > MaxEdgeWords * 2)
                    return false;
            }

            // Trace ordered support so each recognized token is spent once and every edge cue contributes.
            int stride = wordCount + 1;
            Span<byte> common = stackalloc byte[(group.Count + 1) * stride];
            common.Clear();
            for (int cue = 0, i = 1; cue < cueEnds.Count; cue++)
            {
                token.ThrowIfCancellationRequested();
                var interval = intervals[first + cue];
                TimeSpan earliestEnd = interval.Start - EdgeTolerance;
                TimeSpan latestStart = interval.End + EdgeTolerance;
                for (; i <= cueEnds[cue]; i++)
                {
                    for (int j = 1; j <= wordCount; j++)
                    {
                        var word = speech[firstWord + j - 1];
                        int at = i * stride + j;
                        common[at] = group[i - 1] == word.Text && word.End >= earliestEnd && word.Start <= latestStart
                            ? (byte)(common[at - stride - 1] + 1) : Math.Max(common[at - stride], common[at - 1]);
                    }
                }
            }
            if (common[^1] < 4 || common[^1] * 3 < group.Count * 2)
                return false;

            int remaining = wordCount;
            for (int cue = cueEnds.Count - 1; cue >= 0; cue--)
            {
                int begin = cue == 0 ? 0 : cueEnds[cue - 1];
                int matched = 0;
                for (int i = cueEnds[cue]; i > begin; i--)
                {
                    while (remaining > 0 && common[i * stride + remaining] == common[i * stride + remaining - 1])
                        remaining--;
                    int at = i * stride + remaining;
                    if (remaining > 0 && common[at] > common[at - stride])
                    {
                        remaining--;
                        matched++;
                    }
                }
                if (matched < 2 || matched * 2 < cueEnds[cue] - begin)
                    return false;
            }
            return true;
        }

        private bool SharesBoundary(int earlier, TimeSpan start, TimeSpan end, CancellationToken token)
        {
            if (literal[earlier].Match is not { } left || literal[earlier + 1].Match is not { } right ||
                left.Last != right.First || left.Last - left.First < 3 || right.Last - right.First < 3)
                return false;
            return speech[left.First].Start >= start - EdgeTolerance && speech[right.Last].End <= end + EdgeTolerance &&
                FindExact(texts[earlier][..^1], speech, index, 0, speech.Count, token).Match is { } leftInterior &&
                leftInterior.First == left.First && leftInterior.Last == left.Last - 1 &&
                FindExact(texts[earlier + 1][1..], speech, index, 0, speech.Count, token).Match is { } rightInterior &&
                rightInterior.First == right.First + 1 && rightInterior.Last == right.Last;
        }
    }
}
