using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Pure, tolerant parser for the pipe-delimited vocabulary reply (<c>term | reading | translation | definition
/// | example</c>). Local models vary their formatting, so the parser strips list markers, markdown-table pipes,
/// code fences, separator rows and a header row, then maps cells positionally. It NEVER throws — a garbage reply
/// yields an empty list, which the UI surfaces by showing the raw reply instead.
/// </summary>
public static class VocabularyParser
{
    private static readonly Regex ListMarker = new(@"^\s*(?:\d+[.)]|[-*•])\s+", RegexOptions.Compiled);
    private static readonly string[] FieldNames = ["term", "reading", "translation", "definition", "example"];

    /// <summary>Parses a single model reply into entries. Never throws; deduplicates by Term (first wins).</summary>
    public static IReadOnlyList<VocabularyEntry> Parse(string? reply)
    {
        List<VocabularyEntry> entries = [];
        if (string.IsNullOrWhiteSpace(reply))
            return entries;

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string rawLine in reply.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("```"))
                continue;

            // Strip a leading list marker ("1.", "2)", "-", "*", bullet) then outer markdown-table pipes.
            line = ListMarker.Replace(line, "");
            if (line.StartsWith('|'))
                line = line[1..];
            if (line.EndsWith('|'))
                line = line[..^1];
            line = line.Trim();
            if (line.Length == 0)
                continue;

            string[] cells = line.Split('|');
            for (int i = 0; i < cells.Length; i++)
                cells[i] = cells[i].Trim();

            if (cells.Length == 1)
            {
                // No pipe at all: accept as a Term-only entry ONLY if it is short and not a prose sentence /
                // separator, so a stray explanatory paragraph from the model is not mistaken for a term.
                string token = cells[0];
                if (token.Length is 0 or > 40 || IsSeparatorCell(token) || EndsWithSentencePunctuation(token))
                    continue;
                Add(entries, seen, token, "", "", "", "");
                continue;
            }

            // Drop a markdown separator row (all cells like "---", ":--:") and the header row.
            if (cells.All(IsSeparatorCell) || IsHeaderRow(cells))
                continue;

            string term = cells[0];
            string reading = cells.Length > 1 ? cells[1] : "";
            string translation = cells.Length > 2 ? cells[2] : "";
            string definition = cells.Length > 3 ? cells[3] : "";
            string example = cells.Length > 4 ? cells[4] : "";

            // Require the term plus at least one more non-empty field (term + translation minimum).
            if (term.Length == 0 || cells.Count(c => c.Length > 0) < 2)
                continue;

            Add(entries, seen, term, reading, translation, definition, example);
        }

        return entries;
    }

    /// <summary>Merges per-chunk results, deduplicating by Term (first wins) and capping to <paramref name="cap"/>.</summary>
    public static IReadOnlyList<VocabularyEntry> Merge(IEnumerable<IReadOnlyList<VocabularyEntry>> parts, int cap)
    {
        List<VocabularyEntry> result = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (IReadOnlyList<VocabularyEntry>? part in parts)
        {
            if (part == null)
                continue;
            foreach (VocabularyEntry e in part)
            {
                if (e == null || string.IsNullOrEmpty(e.Term) || !seen.Add(e.Term))
                    continue;
                result.Add(e);
                if (cap > 0 && result.Count >= cap)
                    return result;
            }
        }

        return result;
    }

    /// <summary>Tab-separated rendering (header + one row per entry) for clipboard / save — handy for Anki import.</summary>
    public static string ToTsv(IReadOnlyList<VocabularyEntry> entries)
    {
        StringBuilder sb = new();
        sb.Append("Term\tReading\tTranslation\tDefinition\tExample");
        foreach (VocabularyEntry e in entries)
        {
            sb.Append('\n');
            sb.Append(Clean(e.Term)).Append('\t')
              .Append(Clean(e.Reading)).Append('\t')
              .Append(Clean(e.Translation)).Append('\t')
              .Append(Clean(e.Definition)).Append('\t')
              .Append(Clean(e.Example));
        }
        return sb.ToString();

        static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    private static void Add(List<VocabularyEntry> entries, HashSet<string> seen,
        string term, string reading, string translation, string definition, string example)
    {
        if (seen.Add(term))
            entries.Add(new VocabularyEntry(term, reading, translation, definition, example));
    }

    private static bool IsHeaderRow(string[] cells) =>
        cells[0].Equals("term", StringComparison.OrdinalIgnoreCase) &&
        cells.Skip(1).Any(c => FieldNames.Contains(c, StringComparer.OrdinalIgnoreCase));

    // A cell that is empty or made up only of markdown rule/separator characters (---, ***, ===, ___, ~~~,
    // :--:). A real vocabulary term is never all-punctuation, so this also guards the no-pipe Term-only branch.
    private static bool IsSeparatorCell(string cell)
    {
        if (cell.Length == 0)
            return true;
        foreach (char c in cell)
        {
            if (c is not ('-' or ':' or ' ' or '\t' or '*' or '=' or '_' or '~'))
                return false;
        }
        return true;
    }

    private static bool EndsWithSentencePunctuation(string token)
    {
        char last = token[^1];
        // ASCII . ! ? plus CJK full-stop (U+3002), full-width ! (U+FF01), full-width ? (U+FF1F)
        // and the horizontal ellipsis (U+2026).
        return last is '.' or '!' or '?' or '。' or '！' or '？' or '…';
    }
}
