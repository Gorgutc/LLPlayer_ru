using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Tunables for <see cref="SubtitleSegmenter"/>. All values are plain numbers so this stays free of any
/// WPF / config / Logger dependency (and is therefore safe to unit-test — see the Logger static-cctor NRE
/// gotcha that bites code touching the WPF logger from tests).
/// </summary>
public sealed class SubtitleSegmentOptions
{
    /// <summary>Max characters per line for space-separated (Latin/Cyrillic/…) scripts.</summary>
    public int MaxCharsPerLine { get; init; } = 42;

    /// <summary>Max lines per subtitle cue.</summary>
    public int MaxLinesPerCue { get; init; } = 2;

    /// <summary>Max characters per line for space-less scripts (CJK, Thai, …).</summary>
    public int MaxCjkCharsPerLine { get; init; } = 21;

    /// <summary>A cue longer than this (seconds) is split into more cues when it has enough text to do so. 0 disables.</summary>
    public double MaxCueDurationSec { get; init; } = 6.0;

    /// <summary>Never emit a cue shorter than this (seconds); a too-short sliver is merged into its neighbour. 0 disables.</summary>
    public double MinCueDurationSec { get; init; } = 1.0;
}

/// <summary>
/// Re-segments and line-wraps ASR/translation subtitle text so a cue is at most <c>MaxLinesPerCue</c> lines
/// of about <c>MaxCharsPerLine</c> characters (the industry/Netflix norm: 2 lines, ~42 chars), splitting an
/// over-long cue into several sequential cues and redistributing the time proportionally. Pure / static /
/// dependency-free.
///
/// All line breaks and cue boundaries are placed only at "break opportunities" — at spaces or between CJK
/// characters — and NEVER inside a Latin/Cyrillic word, so mixed-script text (e.g. CJK with embedded Latin
/// names) is wrapped without cutting a word in half. Output cues are substrings of the normalized input, so
/// the original spacing is preserved exactly and no text is ever lost.
/// </summary>
public static class SubtitleSegmenter
{
    private static readonly char[] SentenceEnders = ['.', '?', '!', '…', '。', '？', '！'];
    private static readonly char[] ClauseEnders = [',', ';', ':', '—', '–', '、', '，'];

    /// <summary>
    /// Wrap a single cue's text into at most <see cref="SubtitleSegmentOptions.MaxLinesPerCue"/> lines using
    /// '\n'. Returns the text unchanged when it already fits on one line. Never splits inside a word and never
    /// drops text.
    /// </summary>
    public static string WrapTwoLines(string text, SubtitleSegmentOptions opt)
    {
        string norm = Normalize(text);
        if (norm.Length == 0)
            return text;

        int perLine = IsCjkScript(norm) ? opt.MaxCjkCharsPerLine : opt.MaxCharsPerLine;
        return Wrap(norm, perLine, Math.Max(1, opt.MaxLinesPerCue));
    }

    /// <summary>
    /// Split <paramref name="text"/> spanning [<paramref name="start"/>, <paramref name="end"/>] into one or
    /// more cues, each wrapped to at most 2 lines, with times redistributed by character proportion. Returns
    /// at least one cue and never loses text.
    /// </summary>
    public static List<(string Text, TimeSpan Start, TimeSpan End)> Resegment(
        string text, TimeSpan start, TimeSpan end, SubtitleSegmentOptions opt)
    {
        if (end < start)
            end = start;

        string norm = Normalize(text);
        if (norm.Length == 0)
            return [(text, start, end)];

        int perLine = IsCjkScript(norm) ? opt.MaxCjkCharsPerLine : opt.MaxCharsPerLine;
        int maxLines = Math.Max(1, opt.MaxLinesPerCue);
        int budget = perLine * maxLines;

        double durSec = (end - start).TotalSeconds;

        // Fast path: the cue already fits (<= maxLines short lines) and is not over-long in time. Preserve the
        // original text verbatim so already-good multi-line cues (incl. "- A\n- B" dialogue) are not reflowed.
        if (FitsAsIs(text, perLine, maxLines) &&
            (opt.MaxCueDurationSec <= 0 || durSec <= opt.MaxCueDurationSec))
        {
            return [(text, start, end)];
        }

        int cuesByChars = CeilDiv(norm.Length, Math.Max(1, budget));
        int cuesByDuration = opt.MaxCueDurationSec > 0 && durSec > 0
            ? (int)Math.Ceiling(durSec / opt.MaxCueDurationSec)
            : 1;
        int cues = Math.Max(1, Math.Max(cuesByChars, cuesByDuration));

        List<string> pieces = SplitIntoCues(norm, cues);
        if (pieces.Count == 0)
            return [(Wrap(norm, perLine, maxLines), start, end)];

        // Redistribute time by character proportion (no gaps/overlaps; first.Start==start, last.End==end).
        List<(string Text, TimeSpan Start, TimeSpan End)> result = new(pieces.Count);
        int totalChars = pieces.Sum(p => p.Length);
        if (totalChars <= 0)
            totalChars = 1;

        long spanTicks = (end - start).Ticks;
        long consumed = 0;
        TimeSpan acc = start;
        for (int i = 0; i < pieces.Count; i++)
        {
            consumed += pieces[i].Length;
            TimeSpan cueEnd = i == pieces.Count - 1
                ? end
                : start + TimeSpan.FromTicks(spanTicks * consumed / totalChars);
            if (cueEnd < acc)
                cueEnd = acc;

            result.Add((Wrap(pieces[i], perLine, maxLines), acc, cueEnd));
            acc = cueEnd;
        }

        return MergeTooShort(result, opt, perLine, maxLines);
    }

    // ---- text classification / break opportunities --------------------------------------------------

    // Collapse all whitespace (incl. line breaks) to single spaces and trim.
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        StringBuilder sb = new(text.Length);
        bool prevSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace && sb.Length > 0)
                    sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    // A character that must not be broken away from its neighbours of the same kind (a Latin/Cyrillic word or
    // a number). CJK characters are deliberately NOT word chars: they may break between every character.
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) && !IsCjkChar(c);

    // True when a line break may be inserted *before* index i (i.e. text[i] starts a new line). Allowed at a
    // space boundary or a CJK boundary, but never between two word characters (that would split a word).
    private static bool CanBreakBefore(string text, int i)
    {
        if (i <= 0 || i >= text.Length)
            return false;

        char prev = text[i - 1];
        char cur = text[i];

        // Never split a Latin/Cyrillic word or a number.
        if (IsWordChar(prev) && IsWordChar(cur))
            return false;

        // Avoid orphaning trailing punctuation at the start of the next line.
        if (cur is ',' or '.' or '!' or '?' or ';' or ':' or '…' or ')' or ']' or '}' or '»' or '"' or '\'')
            return false;

        return true;
    }

    // For space-less scripts the per-line width differs; detected by Unicode range (NOT space ratio) so the
    // width choice is right for mostly-CJK text. The break logic itself is script-agnostic (see CanBreakBefore).
    private static bool IsCjkScript(string text)
    {
        int cjk = 0;
        int letters = 0;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
                continue;
            letters++;
            if (IsCjkChar(c))
                cjk++;
        }

        return letters > 0 && (double)cjk / letters >= 0.2;
    }

    private static bool IsCjkChar(char c) =>
        (c >= '぀' && c <= 'ヿ') ||   // Hiragana + Katakana
        (c >= '㐀' && c <= '鿿') ||   // CJK ideographs (Ext-A + Unified)
        (c >= '가' && c <= '힯') ||   // Hangul syllables
        (c >= '฀' && c <= '๿') ||   // Thai
        (c >= '豈' && c <= '﫿') ||   // CJK compatibility ideographs
        (c >= '　' && c <= '〿');     // CJK symbols and punctuation

    private static int CeilDiv(int a, int b) => (a + b - 1) / b;

    // ---- wrapping & cue splitting (break-opportunity based) ------------------------------------------

    // Wrap into at most `maxLines` lines, breaking only at valid break opportunities, balancing line lengths.
    private static string Wrap(string text, int perLine, int maxLines)
    {
        text = text.Trim();
        if (text.Length <= perLine || maxLines <= 1)
            return text;

        int best = -1;
        int bestScore = int.MaxValue;
        bool bestFeasible = false;
        bool bestPunct = false;

        for (int i = 1; i < text.Length; i++)
        {
            if (!CanBreakBefore(text, i))
                continue;

            int topLen = i;
            while (topLen > 0 && char.IsWhiteSpace(text[topLen - 1]))
                topLen--;
            int botStart = i;
            while (botStart < text.Length && char.IsWhiteSpace(text[botStart]))
                botStart++;
            int botLen = text.Length - botStart;

            if (topLen == 0 || botLen == 0)
                continue;

            int score = Math.Max(topLen, botLen);
            bool feasible = topLen <= perLine && botLen <= perLine;
            bool punct = IsBreaker(text[topLen - 1]);

            bool better;
            if (feasible != bestFeasible)
                better = feasible;
            else if (punct != bestPunct && score <= perLine)
                better = punct; // among same feasibility, a punctuation break wins when it still fits
            else if (score != bestScore)
                better = score < bestScore;
            else
                better = topLen <= botLen;

            if (best == -1 || better)
            {
                best = i;
                bestScore = score;
                bestFeasible = feasible;
                bestPunct = punct;
            }
        }

        if (best < 0)
            return text; // no legal break (e.g. one over-long word) — keep as a single line, never cut a word

        string top = text[..best].TrimEnd();
        string bottom = text[best..].TrimStart();
        return top + "\n" + bottom;
    }

    // Split into `cues` contiguous substrings at valid break opportunities near evenly spaced character
    // targets, snapping each boundary to sentence/clause punctuation when one is nearby.
    private static List<string> SplitIntoCues(string text, int cues)
    {
        if (cues <= 1)
            return [text];

        List<int> breaks = [];
        for (int i = 1; i < text.Length; i++)
        {
            if (CanBreakBefore(text, i))
                breaks.Add(i);
        }

        if (breaks.Count == 0)
            return [text];

        cues = Math.Min(cues, breaks.Count + 1);
        int total = text.Length;

        List<int> cuts = new(cues - 1);
        int searchFrom = 0; // index into `breaks`
        int lastCut = 0;
        for (int k = 1; k < cues; k++)
        {
            double target = (double)k * total / cues;
            int window = Math.Max(1, total / cues / 4);

            int chosen = -1;
            double chosenDist = double.MaxValue;
            int sentence = -1, clause = -1;

            for (int bi = searchFrom; bi < breaks.Count; bi++)
            {
                int pos = breaks[bi];
                if (pos <= lastCut)
                    continue;
                // leave at least one break for each remaining cut
                if (pos >= total - (cues - 1 - k))
                    break;

                double dist = Math.Abs(pos - target);
                if (dist < chosenDist)
                {
                    chosenDist = dist;
                    chosen = pos;
                    searchFrom = bi;
                }

                if (pos >= target - window && pos <= target + window)
                {
                    char prev = LastNonSpace(text, pos);
                    if (SentenceEnders.Contains(prev))
                        sentence = sentence == -1 ? pos : sentence;
                    else if (ClauseEnders.Contains(prev))
                        clause = clause == -1 ? pos : clause;
                }

                if (pos > target + window && chosen != -1)
                    break;
            }

            int cut = sentence != -1 ? sentence : (clause != -1 ? clause : chosen);
            if (cut <= lastCut)
                continue;

            cuts.Add(cut);
            lastCut = cut;
        }

        List<string> pieces = new(cuts.Count + 1);
        int from = 0;
        foreach (int cut in cuts)
        {
            string piece = text[from..cut].Trim();
            if (piece.Length > 0)
                pieces.Add(piece);
            from = cut;
        }
        string tail = text[from..].Trim();
        if (tail.Length > 0)
            pieces.Add(tail);

        return pieces.Count == 0 ? [text] : pieces;
    }

    private static char LastNonSpace(string text, int exclusiveEnd)
    {
        int i = exclusiveEnd - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;
        return i >= 0 ? text[i] : '\0';
    }

    private static bool IsBreaker(char c) => SentenceEnders.Contains(c) || ClauseEnders.Contains(c);

    // True when the ORIGINAL text already renders acceptably: at most maxLines lines, each within perLine.
    private static bool FitsAsIs(string text, int perLine, int maxLines)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return true;

        string[] lines = trimmed.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > maxLines)
            return false;

        return lines.All(line => line.Trim().Length <= perLine);
    }

    // Merge a cue shorter than MinCueDurationSec into the previous cue so we never emit a sliver.
    private static List<(string Text, TimeSpan Start, TimeSpan End)> MergeTooShort(
        List<(string Text, TimeSpan Start, TimeSpan End)> cues, SubtitleSegmentOptions opt, int perLine, int maxLines)
    {
        if (opt.MinCueDurationSec <= 0 || cues.Count <= 1)
            return cues;

        TimeSpan min = TimeSpan.FromSeconds(opt.MinCueDurationSec);
        List<(string Text, TimeSpan Start, TimeSpan End)> merged = new(cues.Count);
        foreach (var cue in cues)
        {
            if (merged.Count > 0 && (cue.End - cue.Start) < min)
            {
                var prev = merged[^1];
                string combined = StripBreaks(prev.Text) + " " + StripBreaks(cue.Text);
                merged[^1] = (Wrap(Normalize(combined), perLine, maxLines), prev.Start, cue.End);
            }
            else
            {
                merged.Add(cue);
            }
        }

        return merged;
    }

    private static string StripBreaks(string text) => text.Replace('\n', ' ');
}
