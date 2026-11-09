namespace FlyleafLib.MediaFramework.MediaRenderer;

public enum PlanarVideoPixelFormat { Yuv420P, Nv12 }

/// <summary>Borrowed plane; bytes start at the lowest address. BottomUp reverses row order.</summary>
public readonly ref struct VideoPlaneData(ReadOnlySpan<byte> bytes, int width, int height, int rowBytes, int rowStride, bool bottomUp)
{
    public ReadOnlySpan<byte> Bytes { get; } = bytes;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int RowBytes { get; } = rowBytes;
    public int RowStride { get; } = rowStride;
    public bool BottomUp { get; } = bottomUp;
    public bool IsValid => Width > 0 && Height > 0 && RowBytes >= Width && RowStride >= RowBytes &&
        Bytes.Length >= (long)(Height - 1) * RowStride + RowBytes;
    public ReadOnlySpan<byte> GetRow(int row)
    {
        if (!IsValid || (uint)row >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(row));
        return Bytes.Slice((BottomUp ? Height - 1 - row : row) * RowStride, RowBytes);
    }
}

public readonly ref struct PlanarVideoFrame(PlanarVideoPixelFormat format, int width, int height,
    VideoPlaneData plane0, VideoPlaneData plane1, VideoPlaneData plane2)
{
    public PlanarVideoPixelFormat Format { get; } = format;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public VideoPlaneData Plane0 { get; } = plane0;
    public VideoPlaneData Plane1 { get; } = plane1;
    public VideoPlaneData Plane2 { get; } = plane2;
    public bool IsValid => Width > 0 && Height > 0 && Plane0.IsValid && Plane0.Width == Width &&
        Plane0.Height == Height && Plane0.RowBytes == Width && Plane1.IsValid &&
        Plane1.Width == (Width + 1L) / 2 && Plane1.Height == (Height + 1L) / 2 &&
        (Format == PlanarVideoPixelFormat.Yuv420P
            ? Plane1.RowBytes == Plane1.Width && Plane2.IsValid && Plane2.Width == Plane1.Width &&
                Plane2.Height == Plane1.Height && Plane2.RowBytes == Plane1.Width
            : Format == PlanarVideoPixelFormat.Nv12 && Plane1.RowBytes == Plane1.Width * 2L && Plane2.Bytes.IsEmpty);
}

public readonly record struct VideoPresentationParameters(CropRect Crop, bool Transpose, bool FlipX, bool FlipY,
    ColorSpace ColorSpace, ColorRange ColorRange);

/// <summary>Copies borrowed planes before returning. Capability reads perform no GL work or UI waits.</summary>
public interface IVideoPlanarSurface : IVideoSurface
{
    bool CanPresentPlanar(PlanarVideoPixelFormat format);
    bool TryPresentPlanar(in PlanarVideoFrame frame, in VideoPresentationParameters presentation);
}
