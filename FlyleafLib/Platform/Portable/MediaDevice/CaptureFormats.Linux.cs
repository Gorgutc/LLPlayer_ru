#nullable enable
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace FlyleafLib.MediaFramework.MediaDevice;

/// <summary>Selected-device V4L2 query only. No camera starts streaming and no format is applied.</summary>
internal static class LinuxCaptureFormats
{
    internal interface ICaptureIoctl
    {
        int OpenDevice(string path);
        void CloseDevice(int descriptor);
        int ErrorNumber { get; }
        int GetFormat(int descriptor, ref FormatDescription value);
        int GetSize(int descriptor, ref FrameSize value);
        int GetInterval(int descriptor, ref FrameInterval value);
    }
    // Linux x86-64 UAPI: https://raw.githubusercontent.com/torvalds/linux/v6.18/include/uapi/linux/videodev2.h
    // _IOWR('V', 2/74/75, structure): structures contain fixed-width uint32 values, no native pointers.
    internal const ulong EnumFormat = 0xC0405602;
    internal const ulong EnumFrameSizes = 0xC02C564A;
    internal const ulong EnumFrameIntervals = 0xC034564B;
    private const int InvalidArgument = 22;

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct FormatDescription
    {
        [FieldOffset(0)] public uint Index;
        [FieldOffset(4)] public uint BufferType;
        [FieldOffset(44)] public uint PixelFormat;
    }

    [StructLayout(LayoutKind.Explicit, Size = 44)]
    internal struct FrameSize
    {
        [FieldOffset(0)] public uint Index;
        [FieldOffset(4)] public uint PixelFormat;
        [FieldOffset(8)] public uint Type;
        [FieldOffset(12)] public uint Width;
        [FieldOffset(16)] public uint Height;
    }

    [StructLayout(LayoutKind.Explicit, Size = 52)]
    internal struct FrameInterval
    {
        [FieldOffset(0)] public uint Index;
        [FieldOffset(4)] public uint PixelFormat;
        [FieldOffset(8)] public uint Width;
        [FieldOffset(12)] public uint Height;
        [FieldOffset(16)] public uint Type;
        [FieldOffset(20)] public uint Numerator;
        [FieldOffset(24)] public uint Denominator;
    }

    internal static CaptureVideoFormatSnapshot Query(CaptureDeviceInfo device, long epoch, CancellationToken token, ICaptureIoctl? api = null)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || IntPtr.Size != 8)
            return CaptureFormats.Empty(device, epoch, CaptureAvailability.Unavailable, "V4L2 format enumeration supports the packaged Linux x64 ABI only. Use Default format.");
        if (!IsDeviceNode(device.Input))
            return CaptureFormats.Empty(device, epoch, CaptureAvailability.Unavailable, "The selected input is not a /dev/videoN device node.");
        api ??= NativeCaptureIoctl.Instance;
        token.ThrowIfCancellationRequested();
        int descriptor = api.OpenDevice(device.Input);
        if (descriptor < 0)
            return Error(device, epoch, "Open capture device", api.ErrorNumber);
        try
        {
            List<CaptureVideoFormatInfo> formats = [];
            HashSet<string> identities = new(StringComparer.Ordinal);
            bool ranges = false, unknown = false;
            // All loops have bounds against a broken driver repeatedly returning the same index.
            for (uint fi = 0; fi < 1024; fi++)
            {
                token.ThrowIfCancellationRequested();
                FormatDescription description = new() { Index = fi, BufferType = 1 }; // VIDEO_CAPTURE
                if (api.GetFormat(descriptor, ref description) < 0)
                {
                    int errno = api.ErrorNumber;
                    if (errno == InvalidArgument) break;
                    return Error(device, epoch, "Enumerate capture formats", errno);
                }
                if (!TryMapFormat(description.PixelFormat, out string? pixel, out string? codec))
                { unknown = true; continue; }
                for (uint si = 0; si < 4096; si++)
                {
                    token.ThrowIfCancellationRequested();
                    FrameSize size = new() { Index = si, PixelFormat = description.PixelFormat };
                    if (api.GetSize(descriptor, ref size) < 0)
                    {
                        int errno = api.ErrorNumber;
                        if (errno == InvalidArgument) break;
                        return Error(device, epoch, "Enumerate capture frame sizes", errno);
                    }
                    if (size.Type != 1) { ranges = true; break; }
                    if (size.Width == 0 || size.Height == 0 || size.Width > int.MaxValue || size.Height > int.MaxValue)
                    { unknown = true; continue; }
                    for (uint ii = 0; ii < 4096; ii++)
                    {
                        token.ThrowIfCancellationRequested();
                        FrameInterval interval = new() { Index = ii, PixelFormat = description.PixelFormat, Width = size.Width, Height = size.Height };
                        if (api.GetInterval(descriptor, ref interval) < 0)
                        {
                            int errno = api.ErrorNumber;
                            if (errno == InvalidArgument) break;
                            return Error(device, epoch, "Enumerate capture frame intervals", errno);
                        }
                        if (interval.Type != 1) { ranges = true; break; }
                        if (interval.Numerator == 0 || interval.Denominator == 0 || interval.Numerator > int.MaxValue || interval.Denominator > int.MaxValue)
                        { unknown = true; continue; }
                        // The kernel reports seconds/frame. FFmpeg takes frames/second: invert, never truncate.
                        var format = CaptureFormats.Create(device, (int)size.Width, (int)size.Height,
                            (int)interval.Denominator, (int)interval.Numerator, pixel, codec);
                        if (identities.Add(format.Id)) formats.Add(format);
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            string? reason = ranges ? "This device reports continuous/stepwise ranges. Range selection is unsupported; use Default format for those modes." : null;
            if (unknown)
                reason = (reason == null ? "" : reason + " ") + "Some native formats cannot be represented by this adapter; use Default format for them.";
            if (formats.Count == 0 && reason == null)
                reason = "The device returned no discrete format/frame-rate choices. Use Default format.";
            return new(epoch, device.Id, CaptureAvailability.Available, Array.AsReadOnly(formats.ToArray()), reason);
        }
        finally { api.CloseDevice(descriptor); }
    }

    internal static string? CheckDiscoveryAccess(int discoveredCount)
    {
        // FFmpeg v4l2 discovery silently skips devices it cannot open. Successful empty != no physical nodes.
        var nodes = Directory.EnumerateFileSystemEntries("/dev", "video*").Where(IsDeviceNode).ToArray();
        foreach (string node in nodes)
            if (Access(-100, node, 6, 0x200) < 0 && Marshal.GetLastPInvokeError() is 1 or 13) // AT_EACCESS, R_OK|W_OK
                return "Permission denied for " + node + ". FFmpeg discovery omits inaccessible device nodes.";
        return discoveredCount == 0 && nodes.Length != 0 ? "Video device nodes exist, but FFmpeg returned no usable input. They may be busy or unsupported." : null;
    }

    private static bool IsDeviceNode(string path)
    {
        if (!path.StartsWith("/dev/video", StringComparison.Ordinal) || path.Length == 10)
            return false;
        foreach (char c in path.AsSpan(10))
            if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    internal static bool TryMapFormat(uint fourcc, out string? pixelFormat, out string? codec)
    {
        pixelFormat = fourcc switch
        {
            0x56595559 => "yuyv422", // YUYV
            0x59565955 => "uyvy422", // UYVY
            0x3231564E => "nv12",    // NV12
            0x32315559 => "yuv420p", // YU12
            0x33424752 => "rgb24",   // RGB3
            0x33524742 => "bgr24",   // BGR3
            0x59455247 => "gray",    // GREY
            _ => null
        };
        codec = fourcc switch
        {
            0x47504A4D => "mjpeg", // MJPG
            0x4745504A => "mjpeg", // JPEG
            0x34363248 => "h264",  // H264
            0x43564548 => "hevc",  // HEVC
            _ => null
        };
        return pixelFormat != null || codec != null;
    }

    private static CaptureVideoFormatSnapshot Error(CaptureDeviceInfo device, long epoch, string operation, int errno)
        => CaptureFormats.Empty(device, epoch, errno is 1 or 2 or 13 or 16 or 19 ? CaptureAvailability.Unavailable : CaptureAvailability.Failed,
            operation + ": " + new Win32Exception(errno).Message + " (errno " + errno.ToString(CultureInfo.InvariantCulture) + ").");

    private sealed class NativeCaptureIoctl : ICaptureIoctl
    {
        internal static NativeCaptureIoctl Instance { get; } = new();
        public int ErrorNumber => Marshal.GetLastPInvokeError();
        public int OpenDevice(string path) => Open(path, 0x80800); // O_RDONLY | O_NONBLOCK | O_CLOEXEC
        public void CloseDevice(int descriptor) => _ = Close(descriptor);
        public int GetFormat(int descriptor, ref FormatDescription value) => IoctlFormat(descriptor, EnumFormat, ref value);
        public int GetSize(int descriptor, ref FrameSize value) => IoctlSize(descriptor, EnumFrameSizes, ref value);
        public int GetInterval(int descriptor, ref FrameInterval value) => IoctlInterval(descriptor, EnumFrameIntervals, ref value);
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
    [DllImport("libc", EntryPoint = "faccessat", SetLastError = true)]
    private static extern int Access(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode, int flags);
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int IoctlFormat(int descriptor, ulong request, ref FormatDescription value);
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int IoctlSize(int descriptor, ulong request, ref FrameSize value);
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int IoctlInterval(int descriptor, ulong request, ref FrameInterval value);
}
