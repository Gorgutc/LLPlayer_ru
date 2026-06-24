using System.Collections.Generic;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>Coarse progress for the dub render phase (mapped to the batch UI by the caller).</summary>
public sealed record DubbingProgress(int CompletedLines, int TotalLines, string? Phase = null);

/// <summary>
/// Renders a pre-translated subtitle set into a Russian dub audio track beside the source video.
/// Implementations own no GPU state of their own — they drive an <see cref="ITtsService"/> for neural
/// synthesis and the bundled FFmpeg for all DSP (assemble / time-stretch / duck / mix / encode).
/// </summary>
public interface IDubbingRenderer : IAsyncDisposable
{
    /// <summary>
    /// Synthesize + assemble the dub for <paramref name="mediaPath"/> from the already-translated
    /// <paramref name="translatedSubtitles"/> (Russian <c>TranslatedText</c> + source timings) and write
    /// it to <paramref name="outputPath"/>. Throws on error/cancel; the caller maps that to Failed/Canceled.
    /// </summary>
    Task RenderAsync(
        IReadOnlyList<SubtitleData> translatedSubtitles,
        string mediaPath,
        string outputPath,
        IProgress<DubbingProgress>? progress,
        CancellationToken token);
}
