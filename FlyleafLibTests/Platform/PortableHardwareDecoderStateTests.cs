using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafLib.Platform;

[Collection("PortableEngine")]
public unsafe class PortableHardwareDecoderStateTests
{
    static VideoDecoder CreateDecoder()
    {
        if (Engine.Config == null)
            typeof(Engine).GetProperty(nameof(Engine.Config))!.SetValue(null, new EngineConfig { LogLevel = LogLevel.Quiet });
        return new(new Config(true));
    }

    [Fact]
    public void Software_setup_and_snapshot_getters_never_enumerate_or_create_hardware()
    {
        var decoder = CreateDecoder();
        var renderer = decoder.Renderer;
        try
        {
            int calls = 0;
            renderer.HardwareAdapterCatalog = () => { calls++; throw new InvalidOperationException("Unexpected discovery"); };
            renderer.HardwareDeviceFactory = (type, selector, device) => { calls++; throw new InvalidOperationException("Unexpected creation"); };
            renderer.PrepareHardwareDecoding();
            renderer.UpdateDecodeRequest();
            Assert.False(renderer.DecodeStatus.HardwareActive);
            Assert.Equal(PortableVideoDecodeMode.Software, renderer.DecodeStatus.EffectiveMode);
            Assert.False(renderer.DecodeStatus.IsPending);
            Assert.True(decoder.Config.Video.VideoAcceleration);
            Assert.Equal(0, calls);
        }
        finally { renderer.Dispose(); }
    }

    [Fact]
    public void Saving_request_only_publishes_pending_until_next_actual_setup()
    {
        var decoder = CreateDecoder();
        var renderer = decoder.Renderer;
        try
        {
            renderer.PrepareHardwareDecoding();
            int calls = 0;
            renderer.HardwareAdapterCatalog = () => { calls++; throw new InvalidOperationException("Unexpected discovery"); };
            renderer.HardwareDeviceFactory = (type, selector, device) => { calls++; throw new InvalidOperationException("Unexpected creation"); };
            decoder.Config.Video.PortableDecodeMode = PortableVideoDecodeMode.Automatic;
            decoder.Config.Video.PortableHardwareDevice = "dxgi:retained";
            renderer.UpdateDecodeRequest();
            Assert.Equal(PortableVideoDecodeMode.Automatic, renderer.DecodeStatus.RequestedMode);
            Assert.Equal("dxgi:retained", renderer.DecodeStatus.RequestedDevice);
            Assert.Equal(PortableVideoDecodeMode.Software, renderer.DecodeStatus.EffectiveMode);
            Assert.False(renderer.DecodeStatus.HardwareActive);
            Assert.True(renderer.DecodeStatus.IsPending);
            Assert.Equal(0, calls);

            decoder.Config.Video.PortableDecodeMode = PortableVideoDecodeMode.Software;
            decoder.Config.Video.PortableHardwareDevice = "";
            renderer.UpdateDecodeRequest();
            Assert.False(renderer.DecodeStatus.IsPending);
        }
        finally { renderer.Dispose(); }
    }

    [Fact]
    public void Legacy_disallow_does_not_enumerate_even_when_hardware_is_requested()
    {
        var decoder = CreateDecoder();
        var renderer = decoder.Renderer;
        try
        {
            decoder.Config.Video.PortableDecodeMode = PortableVideoDecodeMode.Automatic;
            decoder.Config.Video.VideoAcceleration = false;
            renderer.HardwareAdapterCatalog = () => throw new InvalidOperationException("Unexpected discovery");
            renderer.HardwareDeviceFactory = (type, selector, device) => throw new InvalidOperationException("Unexpected creation");
            renderer.PrepareHardwareDecoding();
            Assert.Equal(PortableVideoDecodeMode.Software, renderer.DecodeStatus.EffectiveMode);
            Assert.Contains("allowance", renderer.DecodeStatus.FallbackReason!);
        }
        finally { renderer.Dispose(); }
    }
}
