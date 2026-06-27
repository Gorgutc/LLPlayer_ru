using System;
using System.Text.RegularExpressions;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Pure, reusable matcher for the subtitle-sidebar search (F-14). A query is compiled ONCE per search (not per cue)
/// under three options — match case, whole word, regex — and each cue's text is tested with <see cref="IsMatch"/>.
/// At the default options (all false) it is a case-insensitive substring search, byte-for-byte equivalent to the
/// prior behavior. Each regex match is bounded by a timeout, so a single pathological (catastrophic-backtracking)
/// match degrades to a non-match instead of running unbounded — note this bounds ONE call, not a whole filter pass
/// over every cue. An invalid pattern yields a null matcher via <see cref="TryCreate"/> so the caller can surface an
/// "invalid regex" hint. Whole-word matching uses Unicode <c>\b</c> boundaries, which (like subtitle editors) never
/// match a term bounded by punctuation, e.g. ".net" or "$5". Dependency-free / static → unit-testable without WPF.
/// </summary>
public sealed class SubtitleSearcher
{
    // Bounds a SINGLE IsMatch call (not the whole filter pass): a catastrophic user regex degrades to "no match"
    // rather than running unbounded. Subtitle cues are short, so a legitimate pattern stays well under this.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly string? _plainQuery;
    private readonly StringComparison _comparison;
    private readonly Regex? _regex;

    private SubtitleSearcher(string? plainQuery, StringComparison comparison, Regex? regex)
    {
        _plainQuery = plainQuery;
        _comparison = comparison;
        _regex = regex;
    }

    /// <summary>
    /// Build a matcher for <paramref name="query"/>. Returns null when <paramref name="useRegex"/> is set and the
    /// pattern does not compile (so the caller can flag an invalid regex). An empty query builds a match-everything
    /// matcher; callers normally treat an empty query as "no filter" before reaching here.
    /// </summary>
    public static SubtitleSearcher? TryCreate(string query, bool matchCase, bool wholeWord, bool useRegex)
    {
        if (useRegex || wholeWord)
        {
            // Regex mode uses the query verbatim; whole-word wraps the (escaped, or raw-regex) term in word
            // boundaries. \b is Unicode-aware in .NET, so it works for Cyrillic/Latin alike.
            string pattern = useRegex
                ? (wholeWord ? $@"\b(?:{query})\b" : query)
                : $@"\b{Regex.Escape(query)}\b";

            RegexOptions options = RegexOptions.CultureInvariant;
            if (!matchCase)
                options |= RegexOptions.IgnoreCase;

            try
            {
                return new SubtitleSearcher(null, default, new Regex(pattern, options, RegexTimeout));
            }
            catch (ArgumentException)
            {
                // invalid user regex (or a malformed pattern wrapped by whole-word) — let the caller report it
                return null;
            }
        }

        // Plain substring — the fast path; at the default options this is exactly the prior OrdinalIgnoreCase search.
        StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return new SubtitleSearcher(query, comparison, null);
    }

    /// <summary>
    /// True when <paramref name="text"/> matches the compiled query. Null/empty text never matches; a regex that
    /// times out on pathological input is treated as a non-match rather than throwing.
    /// </summary>
    public bool IsMatch(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        if (_regex != null)
        {
            try
            {
                return _regex.IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return text.IndexOf(_plainQuery!, _comparison) >= 0;
    }
}
