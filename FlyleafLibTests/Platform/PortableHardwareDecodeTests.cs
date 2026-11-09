using Flyleaf.FFmpeg;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafLib;

public class PortableHardwareDecodeTests
{
    [Fact]
    public void Software_request_retains_legacy_allowance_without_selecting_hardware()
    {
        var selection = PortableHardwareDecodePolicy.Resolve(PortableVideoDecodeMode.Software,
            "dxgi:remembered", true, true, false, []);
        Assert.Equal(PortableVideoDecodeMode.Software, selection.Mode);
        Assert.Null(selection.DeviceSelector);
    }

    [Fact]
    public void Automatic_selection_uses_real_catalog_mapping_for_retained_stable_id()
    {
        PortableVideoAdapterInfo[] adapters =
        [
            new("dxgi:other", "Other", PortableVideoDecodeMode.D3D11VA, "0", true, null),
            new("dxgi:stable", "Selected", PortableVideoDecodeMode.D3D11VA, "3", true, null)
        ];
        var selection = PortableHardwareDecodePolicy.Resolve(PortableVideoDecodeMode.Automatic,
            "dxgi:stable", true, true, false, adapters);
        Assert.Equal(PortableVideoDecodeMode.D3D11VA, selection.Mode);
        Assert.Equal("3", selection.DeviceSelector);
        Assert.Equal("dxgi:stable", selection.EffectiveDevice);
    }

    [Fact]
    public void Cross_platform_or_removed_selection_falls_back_without_reinterpreting_index()
    {
        PortableVideoAdapterInfo[] adapters =
            [new("vaapi:/dev/dri/renderD128", "Render", PortableVideoDecodeMode.VAAPI, "/dev/dri/renderD128", true, null)];
        var selection = PortableHardwareDecodePolicy.Resolve(PortableVideoDecodeMode.Automatic,
            "dxgi:stable", true, false, true, adapters);
        Assert.Equal(PortableVideoDecodeMode.Software, selection.Mode);
        Assert.NotNull(selection.FallbackReason);
        Assert.Null(selection.DeviceSelector);
    }

    [Fact]
    public void Codec_support_requires_matching_device_format_and_device_context_method()
    {
        Assert.True(PortableHardwareDecodePolicy.SupportsConfiguration(AVHWDeviceType.D3d11va,
            AVPixelFormat.D3d11, AVCodecHwConfigMethod.HwDeviceCtx,
            AVHWDeviceType.D3d11va, AVPixelFormat.D3d11));
        Assert.False(PortableHardwareDecodePolicy.SupportsConfiguration(AVHWDeviceType.Vaapi,
            AVPixelFormat.D3d11, AVCodecHwConfigMethod.HwDeviceCtx,
            AVHWDeviceType.D3d11va, AVPixelFormat.D3d11));
        Assert.False(PortableHardwareDecodePolicy.SupportsConfiguration(AVHWDeviceType.D3d11va,
            AVPixelFormat.D3d11, AVCodecHwConfigMethod.HwFramesCtx,
            AVHWDeviceType.D3d11va, AVPixelFormat.D3d11));
    }

    [Fact]
    public void Format_failure_rescans_original_candidates_for_software()
    {
        int attempts = 0;
        AVPixelFormat result = PortableHardwareDecodePolicy.SelectFormat(
            [AVPixelFormat.Yuv420p, AVPixelFormat.D3d11, AVPixelFormat.Nv12, AVPixelFormat.None],
            AVPixelFormat.D3d11, () => { attempts++; return false; });
        Assert.Equal(AVPixelFormat.Yuv420p, result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Negotiated_hardware_format_is_selected_only_after_frame_pool_initialization()
    {
        Assert.Equal(AVPixelFormat.D3d11, PortableHardwareDecodePolicy.SelectFormat(
            [AVPixelFormat.Yuv420p, AVPixelFormat.D3d11, AVPixelFormat.None], AVPixelFormat.D3d11, () => true));
    }

    [Fact]
    public void Hardware_codec_cache_keys_separate_device_and_pixel_format()
    {
        var d3d = new PortableHardwareCodecKey(AVCodecID.H264, AVHWDeviceType.D3d11va, AVPixelFormat.D3d11);
        var vaapi = new PortableHardwareCodecKey(AVCodecID.H264, AVHWDeviceType.Vaapi, AVPixelFormat.Vaapi);
        Assert.NotEqual(d3d, vaapi);
        Assert.NotEqual(d3d, d3d with { PixelFormat = AVPixelFormat.Nv12 });
    }

    [Fact]
    public void Missing_hardware_format_never_initializes_a_pool_and_returns_first_software_candidate()
    {
        int attempts = 0;
        Assert.Equal(AVPixelFormat.Nv12, PortableHardwareDecodePolicy.SelectFormat(
            [AVPixelFormat.Vaapi, AVPixelFormat.Nv12, AVPixelFormat.None], AVPixelFormat.D3d11,
            () => { attempts++; return true; }));
        Assert.Equal(0, attempts);
    }

    [Fact]
    public void Explicit_inaccessible_device_keeps_its_reason_instead_of_selecting_another_adapter()
    {
        PortableVideoAdapterInfo[] adapters =
        [
            new("vaapi:blocked", "Blocked", PortableVideoDecodeMode.VAAPI, "/dev/dri/renderD129", false, "Permission denied"),
            new("vaapi:other", "Other", PortableVideoDecodeMode.VAAPI, "/dev/dri/renderD128", true, null)
        ];
        var selection = PortableHardwareDecodePolicy.Resolve(PortableVideoDecodeMode.VAAPI,
            "vaapi:blocked", true, false, true, adapters);
        Assert.Equal(PortableVideoDecodeMode.Software, selection.Mode);
        Assert.Equal("Permission denied", selection.FallbackReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("dxgi:duplicate")]
    public void Ambiguous_stable_identity_never_selects_one_of_its_current_native_indices(string requestedDevice)
    {
        PortableVideoAdapterInfo[] adapters =
        [
            new("dxgi:duplicate", "First", PortableVideoDecodeMode.D3D11VA, "0", true, null),
            new("dxgi:duplicate", "Second", PortableVideoDecodeMode.D3D11VA, "3", true, null)
        ];
        var selection = PortableHardwareDecodePolicy.Resolve(PortableVideoDecodeMode.Automatic,
            requestedDevice, true, true, false, adapters);
        Assert.Equal(PortableVideoDecodeMode.Software, selection.Mode);
        Assert.Null(selection.DeviceSelector);
        Assert.Contains("ambiguous", selection.FallbackReason!);
    }

    [Theory]
    [InlineData(CodecCapFlags.FrameThreads, false)]
    [InlineData(CodecCapFlags.Hybrid, false)]
    [InlineData(CodecCapFlags.Hardware | CodecCapFlags.Hybrid, false)]
    [InlineData(CodecCapFlags.Hardware, true)]
    public void Absent_hardware_configuration_preserves_named_software_and_hybrid_codecs(
        CodecCapFlags capabilities, bool mustReplaceCodec)
        => Assert.Equal(mustReplaceCodec, PortableHardwareDecodePolicy.RequiresSoftwareDecoderFallback(capabilities, false));

    [Fact]
    public void Dedicated_hardware_codec_with_matching_configuration_keeps_selected_codec()
        => Assert.False(PortableHardwareDecodePolicy.RequiresSoftwareDecoderFallback(CodecCapFlags.Hardware, true));
}
