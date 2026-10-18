#nullable enable

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// Built-in Windows image encoding for the portable renderer when the tracked, decoder-only FFmpeg cannot encode
/// snapshots. Accepts the already transformed/filtered BGRA picture; no rendering or platform UI is performed here.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsSnapshotEncoder
{
    const int PixelFormat24bppRgb = 0x21808;

    internal static void Save(string filename, string extension, ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        Guid encoder = extension switch
        {
            "bmp" => new("557cf400-1a04-11d3-9a73-0000f81ef32e"),
            "jpg" or "jpeg" => new("557cf401-1a04-11d3-9a73-0000f81ef32e"),
            "png" => new("557cf406-1a04-11d3-9a73-0000f81ef32e"),
            _ => throw new ArgumentException("The snapshot format must be bmp, png, jpg or jpeg.", nameof(extension)),
        };
        if (width <= 0 || height <= 0)
            throw new ArgumentException("The snapshot dimensions must be positive.");
        int sourceRowBytes = checked(width * 4);
        if (stride < sourceRowBytes || bgra.Length < checked((long)(height - 1) * stride + sourceRowBytes))
            throw new ArgumentException("The BGRA buffer or stride does not contain the complete image.", nameof(bgra));

        // GDI+ BGR24 keeps BMP output 24-bit, as with FFmpeg, and makes PNG/JPEG colour ordering explicit. Padding
        // belongs to each destination row; the source may independently have an aligned renderer stride.
        int outputStride = checked((checked(width * 3) + 3) & ~3);
        byte[] bgr = new byte[checked(outputStride * height)];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> source = bgra.Slice(y * stride, sourceRowBytes);
            Span<byte> output = bgr.AsSpan(y * outputStride, outputStride);
            for (int x = 0; x < width; x++)
            {
                output[x * 3] = source[x * 4];
                output[x * 3 + 1] = source[x * 4 + 1];
                output[x * 3 + 2] = source[x * 4 + 2];
            }
        }

        StartupInput input = new() { Version = 1, DebugCallback = 0, SuppressBackgroundThread = 0, SuppressExternalCodecs = 0 };
        fixed (byte* pixels = bgr)
        {
            Check(GdiplusStartup(out nuint token, ref input, 0), "start image encoding");
            nint bitmap = 0;
            try
            {
                Check(GdipCreateBitmapFromScan0(width, height, outputStride, PixelFormat24bppRgb, pixels, out bitmap), "create snapshot bitmap");
                Check(GdipSaveImageToFile(bitmap, filename, ref encoder, 0), "save snapshot");
            }
            finally
            {
                try
                {
                    if (bitmap != 0)
                        Check(GdipDisposeImage(bitmap), "dispose snapshot bitmap");
                }
                finally { GdiplusShutdown(token); }
            }
        }
    }

    static void Check(int status, string operation)
    {
        if (status != 0)
            throw new IOException($"Windows image encoder could not {operation} (GDI+ status {status}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInput
    {
        public uint Version;
        public nint DebugCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int GdiplusStartup(out nuint token, ref StartupInput input, nint output);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern void GdiplusShutdown(nuint token);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, byte* pixels, out nint bitmap);

    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int GdipSaveImageToFile(nint image, string filename, ref Guid encoder, nint parameters);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int GdipDisposeImage(nint image);
}
