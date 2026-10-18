namespace FlyleafLib.MediaPlayer;

/// <summary>
/// Portable image of a bitmap subtitle: premultiplied-free BGRA32 pixels (4 bytes per pixel, rows of
/// <see cref="Stride"/> bytes) plus the DPI it should be displayed at. Replaces WPF's WriteableBitmap on the
/// portable (non-Windows) build; hosts convert it to their own bitmap type.
/// </summary>
public sealed class BgraBitmap
{
    public byte[]   Data    { get; }
    public int      Width   { get; }
    public int      Height  { get; }
    public int      Stride  => Width * 4;
    public double   DpiX    { get; }
    public double   DpiY    { get; }

    public BgraBitmap(byte[] data, int width, int height, double dpiX = 96, double dpiY = 96)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (width < 0 || height < 0 || data.Length < width * height * 4)
            throw new ArgumentException("BGRA data is smaller than width * height * 4", nameof(data));

        Data    = data;
        Width   = width;
        Height  = height;
        DpiX    = dpiX;
        DpiY    = dpiY;
    }
}

// F-13 portable counterpart of the WPF members of SubsBitmap (Subtitles.cs, excluded there with #if WINDOWS).
public partial class SubsBitmap
{
    /// <summary>Displayable subtitle image (BGRA32).</summary>
    public BgraBitmap Source { get; set; }

    internal static BgraBitmap CreateWritableBitmap(byte[] data, int width, int height)
        => new(data, width, height, NativeMethods.DpiXSource, NativeMethods.DpiYSource);
}
