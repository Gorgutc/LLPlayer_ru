using System.Collections.Generic;
using System.Text;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Pure transcript assembly + chunking for AI insights (no HTTP/UI). Lives in FlyleafLib so it can be
/// unit-tested (LLPlayer has no test project).
/// </summary>
public static class AiTranscript
{
    /// <summary>
    /// Joins cue texts into a single transcript string, one cue per line, in order. Each cue is collapsed to a
    /// single line (internal newlines become spaces) so one cue maps to exactly one transcript line, which keeps
    /// <see cref="Chunk"/>'s line-boundary splitting cue-accurate. Null/blank cues are skipped.
    /// </summary>
    public static string Build(IReadOnlyList<string?> cueTexts)
    {
        if (cueTexts == null || cueTexts.Count == 0)
            return string.Empty;

        StringBuilder sb = new();
        bool first = true;
        foreach (string? raw in cueTexts)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string line = CollapseToSingleLine(raw);
            if (line.Length == 0)
                continue;

            if (!first)
                sb.Append('\n');
            sb.Append(line);
            first = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Splits a transcript into chunks of at most <paramref name="maxChars"/> characters, never splitting a cue
    /// (splits only at line boundaries). A single cue longer than <paramref name="maxChars"/> becomes its own
    /// (over-budget) chunk rather than being dropped or split mid-word. When the natural chunk count exceeds
    /// <see cref="AiInsightBudget.MaxChunks"/>, chunks are sampled evenly across the transcript and
    /// <paramref name="partialCoverage"/> is set true.
    /// </summary>
    public static IReadOnlyList<string> Chunk(string transcript, int maxChars, out bool partialCoverage)
    {
        partialCoverage = false;
        if (string.IsNullOrEmpty(transcript))
            return [];

        if (maxChars < 1)
            maxChars = 1;

        List<string> chunks = [];
        StringBuilder current = new();

        foreach (string line in transcript.Split('\n'))
        {
            if (current.Length == 0)
            {
                current.Append(line);
            }
            else if (current.Length + 1 + line.Length <= maxChars)
            {
                current.Append('\n').Append(line);
            }
            else
            {
                // Adding this line would overflow the budget: flush the current chunk and start a new one.
                // (A single line longer than maxChars therefore ends up alone in its own over-budget chunk.)
                chunks.Add(current.ToString());
                current.Clear();
                current.Append(line);
            }
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        if (chunks.Count > AiInsightBudget.MaxChunks)
        {
            partialCoverage = true;
            chunks = SampleEvenly(chunks, AiInsightBudget.MaxChunks);
        }

        return chunks;
    }

    private static string CollapseToSingleLine(string text) =>
        text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();

    // Picks `count` items spread evenly across `items` (always includes the first and last), preserving order.
    private static List<string> SampleEvenly(List<string> items, int count)
    {
        if (count < 1)
            return [];
        if (items.Count <= count)
            return items;
        if (count == 1)
            return [items[0]];

        List<string> result = new(count);
        for (int i = 0; i < count; i++)
        {
            int idx = (int)((long)i * (items.Count - 1) / (count - 1));
            result.Add(items[idx]);
        }
        return result;
    }
}
