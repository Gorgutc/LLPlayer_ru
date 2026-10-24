namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Bitmap-subtitle rectangle in video coordinates after the engine's bitmap position/scale adjustment
/// (mirrors <c>FlyleafLib.MediaPlayer.SubsBitmapPosition.Calculate</c> in the video coordinate space, so the host does
/// not need engine viewport/DPI state).
/// </summary>
public readonly record struct BitmapSubtitleGeometry(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Applies the engine's <c>SubsBitmapPosition.Calculate</c> rules in video coordinates: an optional vertical
    /// position shift for horizontal cues (mpv semantics, skipped for vertical subtitles) and a uniform scale around
    /// the cue center. Non-finite or non-positive inputs keep the raw rectangle.
    /// </summary>
    public static BitmapSubtitleGeometry Compute(
        int x, int y, int width, int height, int videoWidth, int videoHeight, double scale, double positionPercent)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || width <= 0 || height <= 0)
            return new(x, y, width, height);

        double confScale = double.IsFinite(scale) && scale > 0 ? Math.Round(scale, 2) : 1.0;
        double confPos = double.IsFinite(positionPercent) ? Math.Clamp(Math.Round(positionPercent), 0, 150) : 100;

        double adjustedX = x;
        double adjustedY = y;

        // The engine shifts only cues below the video middle (normal horizontal subtitles), not vertical ones.
        if (y >= videoHeight / 2.0)
            adjustedY -= (100.0 - confPos) / 100.0 * videoHeight;

        if (confScale != 1.0)
        {
            double ratio = (confScale - 1.0) / 2.0;
            adjustedX -= width * ratio;
            adjustedY -= height * ratio;
        }

        return new(adjustedX, adjustedY, width * confScale, height * confScale);
    }
}
