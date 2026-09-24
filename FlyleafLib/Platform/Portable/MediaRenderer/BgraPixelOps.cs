#nullable enable

using System.Runtime.CompilerServices;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// F-13 portable renderer: in-memory orientation passes over 32-bit (BGRA) pixels, used after the swscale conversion
/// for the parts of a <see cref="VideoTransform"/> that swscale cannot do (horizontal mirror, transpose). The vertical
/// mirror is applied for free on the swscale input (<see cref="VideoTransform.SourceVFlip"/>).
/// </summary>
internal static unsafe class BgraPixelOps
{
    const int Tile = 16; // 16x16 x 4 bytes = 1 KiB per tile (cache friendly transpose)

    /// <summary><c>dst(x, y) = src(width - 1 - x, y)</c> (<paramref name="width"/> x <paramref name="height"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)] // per-frame hot loop: skip tier-0
    public static void MirrorRows(byte* src, int srcStride, byte* dst, int dstStride, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            uint* s = (uint*)(src + (long)y * srcStride) + width - 1;
            uint* d = (uint*)(dst + (long)y * dstStride);

            for (int x = 0; x < width; x++)
                d[x] = *(s - x);
        }
    }

    /// <summary>
    /// Transpose of a <paramref name="srcWidth"/> x <paramref name="srcHeight"/> image into a
    /// <paramref name="srcHeight"/> x <paramref name="srcWidth"/> one: <c>dst(x, y) = src(reverseRows ? srcWidth - 1 - y : y, x)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Transpose(byte* src, int srcStride, int srcWidth, int srcHeight, byte* dst, int dstStride, bool reverseRows)
    {
        int dstWidth  = srcHeight;
        int dstHeight = srcWidth;

        for (int ty = 0; ty < dstHeight; ty += Tile)
        {
            int yEnd = Math.Min(ty + Tile, dstHeight);

            for (int tx = 0; tx < dstWidth; tx += Tile)
            {
                int xEnd = Math.Min(tx + Tile, dstWidth);

                for (int y = ty; y < yEnd; y++)
                {
                    int     srcCol  = reverseRows ? srcWidth - 1 - y : y;
                    uint*   d       = (uint*)(dst + (long)y * dstStride);
                    byte*   s       = src + (long)srcCol * 4;

                    for (int x = tx; x < xEnd; x++)
                        d[x] = *(uint*)(s + (long)x * srcStride);
                }
            }
        }
    }

    /// <summary>
    /// Applies the in-memory part of <paramref name="transform"/> (see <see cref="VideoTransform.NeedsPass"/>) to a
    /// frame that was converted with <see cref="VideoTransform.SourceVFlip"/> already applied.
    /// <paramref name="srcWidth"/>/<paramref name="srcHeight"/> are the converted (pre-transform) dimensions.
    /// </summary>
    public static void ApplyPass(VideoTransform transform, byte* src, int srcStride, int srcWidth, int srcHeight, byte* dst, int dstStride)
    {
        if (transform.Transpose)
            Transpose(src, srcStride, srcWidth, srcHeight, dst, dstStride, transform.FlipY);
        else if (transform.FlipX)
            MirrorRows(src, srcStride, dst, dstStride, srcWidth, srcHeight);
    }

    /// <summary>Span overload of <see cref="ApplyPass(VideoTransform, byte*, int, int, int, byte*, int)"/> (bounds checked).</summary>
    public static void ApplyPass(VideoTransform transform, ReadOnlySpan<byte> src, int srcStride, int srcWidth, int srcHeight, Span<byte> dst, int dstStride)
    {
        var (dw, dh) = transform.OutputSize(srcWidth, srcHeight);
        CheckImage(src.Length, srcStride, srcWidth, srcHeight);
        CheckImage(dst.Length, dstStride, dw, dh);

        fixed (byte* s = src)
        fixed (byte* d = dst)
            ApplyPass(transform, s, srcStride, srcWidth, srcHeight, d, dstStride);
    }

    internal static void CheckImage(int length, int stride, int width, int height)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || length < (long)(height - 1) * stride + width * 4)
            throw new ArgumentException($"Invalid BGRA image ({width}x{height}, stride {stride}, {length} bytes)");
    }
}
