using System.Linq;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.Translation;

#nullable enable

/// <summary>
/// Pure, side-effect-free per-cue rules shared by the interactive (<see cref="SubTranslator"/>) and batch
/// (<see cref="Batch.BatchSubtitleTranslator"/>) translation paths, so their behaviour cannot silently diverge.
/// Each rule below used to be duplicated in both files and kept in sync only by "Parity with interactive"
/// comments — editing one path without the other would quietly desync batch vs interactive translation
/// context/quality (HC-42). Centralising them here makes the parity structural instead of comment-enforced.
/// </summary>
internal static class TranslationCueRules
{
    /// <summary>
    /// A reply that is empty/whitespace must never be cached as a successful translation: leaving
    /// <c>TranslatedText</c> unset keeps <c>IsTranslated</c> false, so the display/writer falls back to the
    /// source line (instead of emitting a blank line) and the cue is retried on a later pass.
    /// </summary>
    public static bool ShouldAcceptReply(string? translated) => !string.IsNullOrWhiteSpace(translated);

    /// <summary>
    /// When re-segmentation is on, keep the translation within the same at-most-N-line wrapped shape as the
    /// (already re-segmented) source cue; otherwise the reply is returned unchanged.
    /// </summary>
    public static string PostProcess(string translated, bool resegment, SubtitleSegmentOptions segmentOptions)
        => resegment ? SubtitleSegmenter.WrapLines(translated, segmentOptions) : translated;

    /// <summary>
    /// Clamps the configured ContextWindow sizes so a negative config value can never underflow the window.
    /// </summary>
    public static (int before, int after) ClampWindow(int before, int after)
        => (Math.Max(0, before), Math.Max(0, after));

    /// <summary>
    /// Builds the LLM ContextWindow request from the focal text and the surrounding raw source lines. Callers
    /// pass already whitespace-filtered neighbours in playback order (batch reads them from the in-order list,
    /// interactive from <c>SubManager.GetContextWindow</c> — both skip whitespace-only cues). Flattening is
    /// applied here so both call sites shape the window identically.
    /// </summary>
    public static SubtitleTranslationContext BuildContext(
        string focalText, IEnumerable<string> rawBefore, IEnumerable<string> rawAfter)
        => new()
        {
            Text = focalText,
            Before = rawBefore.Select(SubtitleTextUtil.FlattenText).ToList(),
            After = rawAfter.Select(SubtitleTextUtil.FlattenText).ToList(),
        };
}
