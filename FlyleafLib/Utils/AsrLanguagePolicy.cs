namespace FlyleafLib;

#nullable enable

/// <summary>
/// Pure policy for the ASR language-pinning trade-off: F-17 anti-drift (pin the first detected language for the
/// whole transcript) versus T-10 per-segment detection (let each segment/chunk auto-detect its own language for
/// genuinely mixed-language audio). Extracted from the ASR services so the decision is unit-testable without a
/// Whisper engine. With the per-segment toggle off, both helpers reduce to the original F-17 behavior.
/// </summary>
public static class AsrLanguagePolicy
{
    /// <summary>
    /// Whether to force (pin) the already-detected <paramref name="detectedLanguage"/> onto the current
    /// segment/chunk. Pinning (F-17) reuses the first detected language to prevent the foreign-token drift Whisper
    /// exhibits on uncertain audio; per-segment mode (T-10) never pins, so each segment/chunk auto-detects its own.
    /// </summary>
    /// <param name="perSegmentLanguage">The <c>Subtitles.ASRPerSegmentLanguage</c> toggle.</param>
    /// <param name="isLanguageDetect">Whether language auto-detection is active for this run (false for a
    /// model/user-fixed language, in which case pinning never applied).</param>
    /// <param name="detectedLanguage">The language already detected/remembered, or null/empty if none yet.</param>
    public static bool ShouldPinLanguage(bool perSegmentLanguage, bool isLanguageDetect, string? detectedLanguage)
        => !perSegmentLanguage && isLanguageDetect && !string.IsNullOrEmpty(detectedLanguage);

    /// <summary>
    /// Whether to clear the remembered language at the start of a chunk so the next chunk re-detects its own
    /// (used by the external faster-whisper process, which detects once per invocation). Only meaningful while
    /// language auto-detection is active and per-segment detection is on.
    /// </summary>
    public static bool ShouldResetPerChunk(bool perSegmentLanguage, bool isLanguageDetect)
        => perSegmentLanguage && isLanguageDetect;
}
