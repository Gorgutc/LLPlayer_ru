#nullable enable

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// F-13 portable renderer: orientation of the presented frame relative to the upright decoded (cropped) frame.
/// <para>
/// Matches the Windows FlyleafVP vertex shader (Renderer.VP.FL.cs <c>FLSetRotationFlip</c>): the quad is mirrored first
/// (HFlip / VFlip in the frame's own orientation) and then rotated clockwise by <c>Rotation</c> degrees.
/// </para>
/// <para>
/// Canonical form used by the software path: <c>Output = FlipXY(Transpose ? T(Frame) : Frame)</c> where
/// <c>T(F)(x, y) = F(y, x)</c> and <see cref="FlipX"/>/<see cref="FlipY"/> mirror the output columns/rows.
/// </para>
/// </summary>
internal readonly struct VideoTransform : IEquatable<VideoTransform>
{
    public readonly bool Transpose;
    public readonly bool FlipX;
    public readonly bool FlipY;

    public VideoTransform(bool transpose, bool flipX, bool flipY)
    {
        Transpose   = transpose;
        FlipX       = flipX;
        FlipY       = flipY;
    }

    public static readonly VideoTransform Identity = new(false, false, false);

    /// <summary>
    /// Builds the transform for a clockwise <paramref name="rotation"/> (any multiple of 90, other values are rounded
    /// down to one) applied after the <paramref name="hflip"/>/<paramref name="vflip"/> mirroring.
    /// </summary>
    public static VideoTransform From(uint rotation, bool hflip, bool vflip)
        => (rotation % 360 / 90) switch
        {
            1 => new(true,  !vflip, hflip),     // 90 CW:  O(x, y) = F(y, H-1-x)
            2 => new(false, !hflip, !vflip),    // 180:    both mirrors toggled
            3 => new(true,  vflip,  !hflip),    // 270 CW: O(x, y) = F(W-1-y, x)
            _ => new(false, hflip,  vflip),
        };

    public bool IsIdentity => !Transpose && !FlipX && !FlipY;

    /// <summary>
    /// Vertical mirror that can be applied for free on the conversion input (negative source line sizes): the output
    /// row flip when not transposed, otherwise the output column flip (which reads source rows backwards).
    /// </summary>
    public bool SourceVFlip => Transpose ? FlipX : FlipY;

    /// <summary>Whether an in-memory pass is still required after the conversion (horizontal mirror or transpose).</summary>
    public bool NeedsPass => Transpose || FlipX;

    /// <summary>Output size for a frame of <paramref name="width"/> x <paramref name="height"/>.</summary>
    public (int Width, int Height) OutputSize(int width, int height)
        => Transpose ? (height, width) : (width, height);

    /// <summary>
    /// Reference mapping (for tests / documentation): the source pixel shown at output (<paramref name="x"/>, <paramref name="y"/>)
    /// for a source of <paramref name="width"/> x <paramref name="height"/>.
    /// </summary>
    public (int X, int Y) MapToSource(int x, int y, int width, int height)
    {
        var (ow, oh) = OutputSize(width, height);
        int px = FlipX ? ow - 1 - x : x;
        int py = FlipY ? oh - 1 - y : y;

        return Transpose ? (py, px) : (px, py);
    }

    public bool Equals(VideoTransform other) => Transpose == other.Transpose && FlipX == other.FlipX && FlipY == other.FlipY;
    public override bool Equals(object? obj) => obj is VideoTransform other && Equals(other);
    public override int GetHashCode() => (Transpose ? 1 : 0) | (FlipX ? 2 : 0) | (FlipY ? 4 : 0);
    public static bool operator ==(VideoTransform a, VideoTransform b) => a.Equals(b);
    public static bool operator !=(VideoTransform a, VideoTransform b) => !a.Equals(b);
    public override string ToString() => $"[T: {Transpose}, FX: {FlipX}, FY: {FlipY}]";
}

/// <summary>F-13 portable renderer: pure size math of the software presentation path.</summary>
internal static class SoftwareVideoGeometry
{
    /// <summary>
    /// Size (upright, i.e. after rotation) of the frame handed to the host: never larger than the native visible size
    /// (<paramref name="uprightWidth"/> x <paramref name="uprightHeight"/>) and never larger than the viewport (device
    /// pixels) it is drawn into. Each axis is capped independently (the host stretches into the viewport anyway, e.g.
    /// for anamorphic or Fill/Custom aspect ratios). A viewport without size (no host control yet) keeps the native size.
    /// </summary>
    public static (int Width, int Height) PresentationSize(int uprightWidth, int uprightHeight, double viewportWidth, double viewportHeight)
    {
        if (uprightWidth <= 0 || uprightHeight <= 0)
            return (0, 0);

        if (!(viewportWidth >= 1) || !(viewportHeight >= 1)) // also NaN
            return (uprightWidth, uprightHeight);

        int w = viewportWidth  >= uprightWidth  ? uprightWidth  : Math.Max(1, (int)Math.Ceiling(viewportWidth));
        int h = viewportHeight >= uprightHeight ? uprightHeight : Math.Max(1, (int)Math.Ceiling(viewportHeight));

        return (w, h);
    }

    /// <summary>
    /// Snapshot size (upright). Both 0: native visible size. One of them 0: keeps the native ratio. Values are even and
    /// at least 2 (encoder requirement); same semantics as the stage-1 portable snapshot, extended to rotated frames.
    /// </summary>
    public static (int Width, int Height) SnapshotSize(int uprightWidth, int uprightHeight, uint width, uint height)
    {
        if (width == 0 && height == 0)
            { width = (uint)uprightWidth; height = (uint)uprightHeight; }
        else if (width == 0)
            width  = (uint)(uprightWidth  * (height / (double)uprightHeight));
        else if (height == 0)
            height = (uint)(uprightHeight * (width  / (double)uprightWidth));

        return ((int)Math.Max(2, width & ~1u), (int)Math.Max(2, height & ~1u));
    }

    /// <summary>Clamps a crop rectangle so that at least one pixel remains (oversized user crop).</summary>
    public static CropRect ClampCrop(CropRect crop, int width, int height)
    {
        uint left   = Math.Min(crop.Left,   (uint)width  - 1);
        uint top    = Math.Min(crop.Top,    (uint)height - 1);
        uint right  = Math.Min(crop.Right,  (uint)width  - 1 - left);
        uint bottom = Math.Min(crop.Bottom, (uint)height - 1 - top);

        return new(top, left, bottom, right);
    }
}
