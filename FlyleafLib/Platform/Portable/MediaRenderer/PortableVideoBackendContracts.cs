#nullable enable
namespace FlyleafLib.MediaFramework.MediaRenderer;

public enum PortableVideoDecodeMode { Software, Automatic, D3D11VA, VAAPI }
public enum PortableVideoPresentationMode { Software, OpenGL }

public sealed record PortableVideoDecodeStatus(
    PortableVideoDecodeMode RequestedMode, string RequestedDevice,
    PortableVideoDecodeMode EffectiveMode, string? EffectiveDevice,
    bool IsInitialized, bool HardwareActive, string? FallbackReason)
{
    public bool IsPending { get; init; }
}

public sealed record PortableVideoPresentationStatus(
    PortableVideoPresentationMode RequestedMode,
    PortableVideoPresentationMode EffectiveMode,
    bool IsInitialized, bool UsesPlanarShader, string? FallbackReason);

public sealed record PortableVideoAdapterInfo(
    string Id, string Name, PortableVideoDecodeMode Mode,
    string DeviceSelector, bool IsAvailable, string? UnavailableReason);
