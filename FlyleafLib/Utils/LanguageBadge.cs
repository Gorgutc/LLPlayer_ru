#nullable enable

namespace FlyleafLib;

/// <summary>
/// Pure formatting/gating helpers for the per-cue language badge shown in the subtitles sidebar
/// (T-10 follow-up). Kept WPF-free so the logic is unit-testable; the sidebar converters in LLPlayer
/// are thin wrappers around this class.
/// </summary>
public static class LanguageBadge
{
    /// <summary>
    /// Lower-case language code to display for a cue's language (ISO 639-1 where one exists; for a language
    /// without a 2-letter code .NET's TwoLetterISOLanguageName falls back to the 3-letter code, e.g. haw/yue),
    /// or "" when there is nothing meaningful to show: a null language, or an unresolved/Unknown language —
    /// the Unknown branch of <see cref="Language.Get(string)"/> never assigns <see cref="Language.ISO6391"/>,
    /// so it stays null.
    /// </summary>
    public static string ToBadgeCode(Language? lang)
        => lang == null || string.IsNullOrWhiteSpace(lang.ISO6391) ? "" : lang.ISO6391.ToLowerInvariant();

    /// <summary>
    /// A badge is shown only when per-segment ASR language detection is enabled AND the cue carries a
    /// displayable code. ASR stamps <c>SubtitleData.Language</c> on every cue even with the toggle off
    /// (it then mirrors the pinned transcript language), so gating on the toggle is what keeps the
    /// default-config UI byte-identical.
    /// </summary>
    public static bool ShouldShow(bool perSegmentLanguageEnabled, Language? lang)
        => perSegmentLanguageEnabled && ToBadgeCode(lang).Length > 0;
}
