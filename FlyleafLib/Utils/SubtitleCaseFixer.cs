namespace FlyleafLib;

#nullable enable

/// <summary>
/// Normalizes an ALL-CAPS subtitle cue to sentence-case. Random ALL-CAPS output is a known faster-whisper(-XXL)
/// artifact (often on previews/trailers/merged lines) — see F-18. Conservative on purpose: a cue is only rewritten
/// when its letters are PREDOMINANTLY uppercase AND it has at least two words, so a short acronym, a single
/// shouted word, a proper noun, or normal mixed/title-case text is left untouched. Pure / static / dependency-free
/// (mirrors <see cref="SubtitleSegmenter"/>), so it is safe to unit-test and to run on the ASR text path.
/// </summary>
public static class SubtitleCaseFixer
{
    // Share of cased letters that must be uppercase before a cue is treated as all-caps. A normal sentence is
    // ~5-15% uppercase (sentence starts / a name); an ASR caps artifact is ~100%, so 0.6 separates them cleanly.
    private const double UpperRatioThreshold = 0.6;

    private static readonly char[] SentenceEnders = ['.', '?', '!', '…', '。', '？', '！'];

    /// <summary>
    /// Returns <paramref name="text"/> with an ALL-CAPS cue rewritten to sentence-case, or unchanged when the
    /// cue is not predominantly uppercase (or is a single word). Line breaks (<c>\n</c>) are preserved and each
    /// starts a new sentence. Acronyms/proper nouns inside a genuinely all-caps cue are lower-cased — an accepted
    /// trade-off, since such a cue is an ASR artifact (the feature is user-toggleable).
    /// </summary>
    public static string FixAllCaps(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        // Never reflow a cue that contains a URL / scheme: its path component is case-sensitive and the tokens
        // (HTTPS, EXAMPLE, COM, PATH) would otherwise count as words and be lower-cased, corrupting the link.
        if (text.Contains("://", System.StringComparison.Ordinal))
            return text;

        return IsPredominantlyUpper(text) ? ToSentenceCase(text) : text;
    }

    private static bool IsPredominantlyUpper(string text)
    {
        int upper = 0;
        int lower = 0;
        int words = 0;
        bool inWord = false;

        foreach (char c in text)
        {
            bool isLetterOrDigit = char.IsLetterOrDigit(c);
            if (isLetterOrDigit && !inWord)
            {
                inWord = true;
                words++;
            }
            else if (!isLetterOrDigit)
            {
                inWord = false;
            }

            if (char.IsUpper(c))
                upper++;
            else if (char.IsLower(c))
                lower++;
        }

        int cased = upper + lower;
        return cased > 0 && words >= 2 && (double)upper / cased >= UpperRatioThreshold;
    }

    private static string ToSentenceCase(string text)
    {
        System.Text.StringBuilder sb = new(text.Length);
        bool capitalizeNext = true;

        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
                if (c == '\n' || c == '\r' || System.Array.IndexOf(SentenceEnders, c) >= 0)
                    capitalizeNext = true;
            }
        }

        return sb.ToString();
    }
}
