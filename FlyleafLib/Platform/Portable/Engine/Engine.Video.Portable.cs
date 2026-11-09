using System.Windows.Data;

using FlyleafLib.MediaFramework.MediaDevice;

namespace FlyleafLib;

/// <summary>
/// F-13 portable (Linux) video engine. Same public surface as the Windows (DXGI/MediaFoundation) VideoEngine in
/// Engine/Engine.Video.cs. Keeps its compatibility <c>Software</c> adapter alongside the separate native backend catalog.
/// Capture discovery is explicit and requires the All startup profile.
/// </summary>
public class VideoEngine
{
    /// <summary>LUID of the single software adapter.</summary>
    public const long SoftwareAdapterLuid = 0;

    /// <summary>
    /// Compatibility list of video capture devices; formats are queried only on explicit selection.
    /// </summary>
    public ObservableCollection<VideoDevice>
                            CapDevices          { get; set; } = [];

    /// <summary>
    /// List of GPU Adpaters <see cref="Config.VideoConfig.GPUAdapter"/> (a single "Software" entry on the portable build)
    /// </summary>
    public Dictionary<long, GPUAdapter>
                            GPUAdapters         { get; private set; }

    private readonly object lockCapDevices = new();
    private readonly object lockCaptureRefresh = new();
    private readonly CaptureDeviceCatalog captureCatalog = new(new FFmpegCaptureDeviceBackend());
    private CaptureDeviceSnapshot captureSnapshot = CaptureDeviceSnapshot.Initial;

    public CaptureDeviceSnapshot CaptureSnapshot => Volatile.Read(ref captureSnapshot);

    internal VideoEngine() // We consider from UI here
    {
        BindingOperations.EnableCollectionSynchronization(CapDevices, lockCapDevices);
        GPUAdapters = new()
        {
            [SoftwareAdapterLuid] = new()
            {
                Vendor      = GPUVendor.Unknown,
                Description = "Software (FFmpeg swscale)",
                Luid        = SoftwareAdapterLuid
            }
        };

        Engine.Log?.Info($"GPU Adapters\r\n[#1] {GPUAdapters[SoftwareAdapterLuid]}");
    }

    /// <summary>
    /// Explicitly enumerates real video inputs. Does not change the loaded FFmpeg profile or query formats.
    /// </summary>
    public void RefreshCapDevices() => RefreshCapDevices(CancellationToken.None);

    /// <summary>Cancellation is observed before/after native calls; a running driver call must finish.</summary>
    public void RefreshCapDevices(CancellationToken cancellationToken)
    {
        lock (lockCaptureRefresh)
        {
            var result = captureCatalog.Refresh(CaptureDeviceKind.Video,
                Engine.IsLoaded && Engine.FFmpeg?.LoadedProfile == LoadProfile.All,
                OperatingSystem.IsWindows() ? ["dshow"] : OperatingSystem.IsLinux() ? ["video4linux2"] : [], cancellationToken);
            lock (lockCapDevices)
            {
                CapDevices.Clear();
                foreach (var device in result.Devices)
                    CapDevices.Add(new(device.Label, device.Id, device.Url, Array.AsReadOnly(Array.Empty<VideoDeviceStream>())));
                Volatile.Write(ref captureSnapshot, result);
            }
        }
    }

    /// <summary>Queries formats only for a real device in the current immutable catalog.</summary>
    public CaptureVideoFormatSnapshot GetCaptureFormats(CaptureDeviceInfo device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (lockCaptureRefresh)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = CaptureSnapshot;
            bool found = false;
            foreach (var current in snapshot.Devices)
                if (current == device) { found = true; break; }
            if (!found || device.Kind != CaptureDeviceKind.Video)
                return CaptureFormats.Empty(device, snapshot.Epoch, CaptureAvailability.Unavailable, "This capture device is stale. Refresh and select the current device.");
            if (!Engine.IsLoaded || Engine.FFmpeg?.LoadedProfile != LoadProfile.All)
                return CaptureFormats.Empty(device, snapshot.Epoch, CaptureAvailability.Unavailable, "Capture requires the All FFmpeg startup profile and an application restart.");
            return CaptureFormats.Query(device, snapshot.Epoch, cancellationToken);
        }
    }
}
