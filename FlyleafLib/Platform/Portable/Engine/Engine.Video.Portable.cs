using System.Windows.Data;

using FlyleafLib.MediaFramework.MediaDevice;

namespace FlyleafLib;

/// <summary>
/// F-13 portable (Linux) video engine. Same public surface as the Windows (DXGI/MediaFoundation) VideoEngine in
/// Engine/Engine.Video.cs. The portable renderer works in software (FFmpeg swscale -> BGRA -> host surface), so there
/// is a single <c>Software</c> adapter and no capture-device enumeration yet.
/// </summary>
public class VideoEngine
{
    /// <summary>LUID of the single software adapter.</summary>
    public const long SoftwareAdapterLuid = 0;

    /// <summary>
    /// List of Video Capture Devices (not enumerated on the portable build yet)
    /// </summary>
    public ObservableCollection<VideoDevice>
                            CapDevices          { get; set; } = [];

    /// <summary>
    /// List of GPU Adpaters <see cref="Config.VideoConfig.GPUAdapter"/> (a single "Software" entry on the portable build)
    /// </summary>
    public Dictionary<long, GPUAdapter>
                            GPUAdapters         { get; private set; }

    private readonly object lockCapDevices = new();

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
    /// Enumerates Video Capture Devices which can be retrieved from <see cref="CapDevices"/> (none on the portable build yet)
    /// </summary>
    public void RefreshCapDevices()
    {
        lock (lockCapDevices)
            CapDevices.Clear();
    }
}
