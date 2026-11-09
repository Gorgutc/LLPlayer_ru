#nullable enable
using System.Linq;
using System.Threading;

namespace FlyleafLib.MediaFramework.MediaDevice;

public enum CaptureDeviceKind { Audio, Video }
public enum CaptureAvailability { NotRefreshed, Available, Unavailable, Failed }

public sealed record CaptureDeviceInfo(string Id, string Label, string Backend, string Input, CaptureDeviceKind Kind)
{
    public string Url => CaptureInputUrl.Build(this);
}

public sealed record CaptureVideoFormatInfo(string Id, string DeviceId, int Width, int Height,
    int FrameRateNumerator, int FrameRateDenominator, string? PixelFormat, string? Codec);

public sealed record CaptureDeviceSnapshot(long Epoch, CaptureAvailability Availability,
    IReadOnlyList<CaptureDeviceInfo> Devices, string? Reason)
{
    public static CaptureDeviceSnapshot Initial { get; } = new(0, CaptureAvailability.NotRefreshed,
        Array.AsReadOnly(Array.Empty<CaptureDeviceInfo>()), "Capture devices have not been refreshed.");
}

public sealed record CaptureVideoFormatSnapshot(long CatalogEpoch, string DeviceId, CaptureAvailability Availability,
    IReadOnlyList<CaptureVideoFormatInfo> Formats, string? Reason);

public static class CaptureInputUrl
{
    public static string Build(CaptureDeviceInfo device, CaptureVideoFormatInfo? format = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!FmtInputUrl.IsValidBackend(device.Backend) || string.IsNullOrEmpty(device.Id) ||
            string.IsNullOrEmpty(device.Input) || device.Input.Contains('\0'))
            throw new ArgumentException("Capture requires a backend and native device identity.", nameof(device));

        var url = new StringBuilder(FmtInputUrl.CapturePrefix).Append(device.Backend).Append('?').Append(FmtInputUrl.Escape(device.Input));
        if (format == null)
            return url.ToString();
        if (device.Kind != CaptureDeviceKind.Video || format.DeviceId != device.Id || string.IsNullOrEmpty(format.Id) ||
            format.Width <= 0 || format.Height <= 0 || format.FrameRateNumerator <= 0 || format.FrameRateDenominator <= 0 ||
            (!string.IsNullOrEmpty(format.PixelFormat) && !string.IsNullOrEmpty(format.Codec)))
            throw new ArgumentException("The format must belong to this video input and contain valid dimensions/rational frame rate.", nameof(format));

        Add("video_size", FormattableString.Invariant($"{format.Width}x{format.Height}"));
        Add("framerate", FormattableString.Invariant($"{format.FrameRateNumerator}/{format.FrameRateDenominator}"));
        if (device.Backend is "v4l2" or "video4linux2")
        {
            string? inputFormat = format.PixelFormat ?? format.Codec;
            if (!string.IsNullOrEmpty(inputFormat))
                Add("input_format", inputFormat);
        }
        else if (!string.IsNullOrEmpty(format.PixelFormat))
            Add("pixel_format", format.PixelFormat);
        else if (!string.IsNullOrEmpty(format.Codec))
            Add("vcodec", format.Codec);
        return url.ToString();

        void Add(string key, string value)
        {
            if (value.Contains('\0'))
                throw new ArgumentException("Capture options cannot contain null characters.", nameof(format));
            url.Append('&').Append(key).Append('=').Append(FmtInputUrl.Escape(value));
        }
    }
}

internal sealed record CaptureBackendDevice(string NativeId, string Label, IReadOnlyList<CaptureDeviceKind> Kinds);
internal sealed record CaptureBackendResult(string Backend, CaptureAvailability Availability,
    IReadOnlyList<CaptureBackendDevice> Devices, string? Reason);

internal interface ICaptureDeviceBackend
{
    CaptureBackendResult Enumerate(string backend);
}

internal sealed class CaptureDeviceCatalog(ICaptureDeviceBackend backend)
{
    private long epoch;
    private readonly object gate = new();

    internal CaptureDeviceSnapshot Refresh(CaptureDeviceKind kind, bool allProfile,
        IReadOnlyList<string> backends, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long revision = Interlocked.Increment(ref epoch);
            if (!allProfile)
                return Snapshot(CaptureAvailability.Unavailable, [], "Capture requires the All FFmpeg startup profile. Select All in startup settings and restart the application.");
            if (backends.Count == 0)
                return Snapshot(CaptureAvailability.Unavailable, [], "Capture is unsupported on this host.");

            List<CaptureDeviceInfo> devices = [];
            HashSet<string> identities = new(StringComparer.Ordinal);
            List<string> reasons = [];
            bool available = false, failed = false;
            foreach (string backendName in backends)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CaptureBackendResult result;
                try { result = backend.Enumerate(backendName); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { result = new(backendName, ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException or UnauthorizedAccessException ? CaptureAvailability.Unavailable : CaptureAvailability.Failed, [], ex.Message); }
                // FFmpeg/device calls have no cancellation API: finish the call before observing cancellation.
                cancellationToken.ThrowIfCancellationRequested();
                available |= result.Availability == CaptureAvailability.Available;
                failed |= result.Availability == CaptureAvailability.Failed;
                if (!string.IsNullOrWhiteSpace(result.Reason))
                    reasons.Add($"{backendName}: {result.Reason}");
                if (result.Availability != CaptureAvailability.Available)
                    continue;
                foreach (var item in result.Devices)
                {
                    bool matches = item.Kinds.Contains(kind) || (item.Kinds.Count == 0 &&
                        ((kind == CaptureDeviceKind.Video && backendName is "v4l2" or "video4linux2") ||
                         (kind == CaptureDeviceKind.Audio && backendName is "pulse" or "alsa")));
                    if (!matches || string.IsNullOrEmpty(item.NativeId))
                        continue;
                    string id = backendName + ":" + kind + ":" + Uri.EscapeDataString(item.NativeId);
                    if (!identities.Add(id))
                        continue;
                    string input = backendName == "dshow" ? (kind == CaptureDeviceKind.Video ? "video=" : "audio=") + item.NativeId : item.NativeId;
                    devices.Add(new(id, string.IsNullOrEmpty(item.Label) ? item.NativeId : item.Label, backendName, input, kind));
                }
            }
            var status = devices.Count != 0 ? CaptureAvailability.Available : failed ? CaptureAvailability.Failed :
                available ? CaptureAvailability.Available : CaptureAvailability.Unavailable;
            return Snapshot(status, devices.ToArray(), reasons.Count == 0 ? null : string.Join(Environment.NewLine, reasons));

            CaptureDeviceSnapshot Snapshot(CaptureAvailability status, CaptureDeviceInfo[] items, string? reason)
                => new(revision, status, Array.AsReadOnly(items), reason);
        }
    }
}

internal static class CaptureFormats
{
    internal static CaptureVideoFormatSnapshot Query(CaptureDeviceInfo device, long epoch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            CaptureVideoFormatSnapshot result;
#if PORTABLE_WINDOWS
            if (OperatingSystem.IsWindows() && device.Backend == "dshow")
                result = WindowsCaptureFormats.Query(device, epoch, token);
            else
#endif
            if (OperatingSystem.IsLinux() && device.Backend is "v4l2" or "video4linux2")
                result = LinuxCaptureFormats.Query(device, epoch, token);
            else
                result = Empty(device, epoch, CaptureAvailability.Unavailable, "A structured format adapter is unavailable for this input. Use Default format.");
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Empty(device, epoch, ex is UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or NotSupportedException ? CaptureAvailability.Unavailable : CaptureAvailability.Failed,
                "Format query failed: " + ex.Message + " Use Default format if the device can be opened.");
        }
    }

    internal static CaptureVideoFormatSnapshot Empty(CaptureDeviceInfo device, long epoch, CaptureAvailability availability, string reason)
        => new(epoch, device.Id, availability, Array.AsReadOnly(Array.Empty<CaptureVideoFormatInfo>()), reason);

    internal static CaptureVideoFormatInfo Create(CaptureDeviceInfo device, int width, int height, int numerator, int denominator,
        string? pixelFormat, string? codec)
    {
        string id = FormattableString.Invariant($"{device.Id}|{width}x{height}|{numerator}/{denominator}|{pixelFormat}|{codec}");
        return new(id, device.Id, width, height, numerator, denominator, pixelFormat, codec);
    }

    internal static string NormalizeDshowIdentity(string identity)
        => (identity.StartsWith("@device_pnp_", StringComparison.OrdinalIgnoreCase) ? identity[12..] : identity).Replace(':', '_');
}
