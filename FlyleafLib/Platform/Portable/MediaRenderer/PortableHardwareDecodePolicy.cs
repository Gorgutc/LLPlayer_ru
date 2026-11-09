#nullable enable
namespace FlyleafLib.MediaFramework.MediaRenderer;

internal readonly record struct PortableHardwareSelection(
    PortableVideoDecodeMode Mode, string? DeviceSelector, string? EffectiveDevice, string? FallbackReason);

internal readonly record struct PortableHardwareCodecKey(
    AVCodecID CodecId, AVHWDeviceType DeviceType, AVPixelFormat PixelFormat);

/// <summary>Native-independent decisions shared by device setup and codec format negotiation.</summary>
internal static class PortableHardwareDecodePolicy
{
    internal static PortableHardwareSelection Resolve(
        PortableVideoDecodeMode requestedMode, string requestedDevice, bool legacyAllowance,
        bool isWindows, bool isLinux, IReadOnlyList<PortableVideoAdapterInfo> adapters)
    {
        if (requestedMode == PortableVideoDecodeMode.Software)
            return new(PortableVideoDecodeMode.Software, null, null, null);
        if (!legacyAllowance)
            return Software("Hardware decoding is disabled by the decoder allowance.");

        var mode = requestedMode == PortableVideoDecodeMode.Automatic
            ? isWindows ? PortableVideoDecodeMode.D3D11VA : isLinux ? PortableVideoDecodeMode.VAAPI : PortableVideoDecodeMode.Software
            : requestedMode;
        if (mode == PortableVideoDecodeMode.Software ||
            mode == PortableVideoDecodeMode.D3D11VA && !isWindows ||
            mode == PortableVideoDecodeMode.VAAPI && !isLinux)
            return Software($"{requestedMode} decoding is unavailable on this operating system.");

        PortableVideoAdapterInfo? selected = null;
        foreach (var adapter in adapters)
        {
            if (adapter.Mode != mode)
                continue;
            if (!string.IsNullOrEmpty(requestedDevice))
            {
                if (string.Equals(adapter.Id, requestedDevice, StringComparison.Ordinal))
                {
                    selected = adapter;
                    break;
                }
            }
            else if (adapter.IsAvailable)
            {
                selected = adapter;
                break;
            }
        }

        if (selected is null)
            return Software(string.IsNullOrEmpty(requestedDevice)
                ? $"No accessible {mode} adapter was found."
                : $"The selected hardware device '{requestedDevice}' is unavailable on this host.");
        int identityMatches = 0;
        foreach (var adapter in adapters)
            if (adapter.Mode == mode && string.Equals(adapter.Id, selected.Id, StringComparison.Ordinal))
                identityMatches++;
        if (identityMatches != 1)
            return Software($"The selected hardware device identity '{selected.Id}' is ambiguous.");
        if (!selected.IsAvailable)
            return Software(selected.UnavailableReason ?? "The selected hardware device is inaccessible.");
        return new(mode, selected.DeviceSelector, selected.Id, null);
    }

    static PortableHardwareSelection Software(string reason)
        => new(PortableVideoDecodeMode.Software, null, null, reason);

    internal static bool SupportsConfiguration(AVHWDeviceType actualDevice, AVPixelFormat actualFormat,
        AVCodecHwConfigMethod methods, AVHWDeviceType requestedDevice, AVPixelFormat requestedFormat)
        => actualDevice == requestedDevice && actualFormat == requestedFormat &&
            (methods & AVCodecHwConfigMethod.HwDeviceCtx) != 0;

    internal static bool RequiresSoftwareDecoderFallback(CodecCapFlags capabilities, bool matchingHardwareConfiguration)
        => !matchingHardwareConfiguration && (capabilities & CodecCapFlags.Hardware) != 0 &&
            (capabilities & CodecCapFlags.Hybrid) == 0;

    internal static AVPixelFormat SelectFormat(ReadOnlySpan<AVPixelFormat> candidates,
        AVPixelFormat hardwareFormat, Func<bool> initializeFrames,
        Func<AVPixelFormat, bool>? isSoftwareFormat = null)
    {
        foreach (var candidate in candidates)
        {
            if (candidate == AVPixelFormat.None)
                break;
            if (candidate == hardwareFormat)
            {
                if (initializeFrames())
                    return hardwareFormat;
                break;
            }
        }

        // Start again at the original candidate, including software formats preceding the hardware entry.
        foreach (var candidate in candidates)
        {
            if (candidate == AVPixelFormat.None)
                break;
            bool software = isSoftwareFormat?.Invoke(candidate) ?? candidate is not (
                AVPixelFormat.D3d11 or AVPixelFormat.D3d11vaVld or AVPixelFormat.Dxva2Vld or
                AVPixelFormat.Vaapi or AVPixelFormat.Vdpau or AVPixelFormat.Qsv or
                AVPixelFormat.Cuda or AVPixelFormat.Videotoolbox or AVPixelFormat.DrmPrime or
                AVPixelFormat.Opencl or AVPixelFormat.Vulkan or AVPixelFormat.D3d12);
            if (software && candidate != hardwareFormat)
                return candidate;
        }
        return AVPixelFormat.None;
    }
}
