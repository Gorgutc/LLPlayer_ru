using System.Text.Json;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;
using LLPlayer.Avalonia.Services;
using Xunit;

namespace LLPlayer.Avalonia.Tests;

public sealed class MediaBackendSettingsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Old_json_and_unrelated_save_preserve_legacy_true_and_software_mode(bool legacyAllowance)
    {
        var settings = JsonSerializer.Deserialize<PlaybackSettings>("{\"AudioVolumeDefault\":82}", PlaybackSettingsStore.JsonOptions)!;
        var config = new Config(test: true); config.Video.VideoAcceleration = legacyAllowance;

        // Hardware policy is independent of audio, whose filter setter requires a bootstrapped Engine.Config.
        settings.ApplyTo(config, includeAudio: false);

        Assert.False(settings.VideoHardwareAccelerationEnabled);
        Assert.Equal(PortableVideoDecodeMode.Automatic, settings.VideoHardwareDecodeMode);
        Assert.Equal("", settings.VideoHardwareDevice);
        Assert.Equal(PortableVideoPresentationMode.Software, settings.VideoPresentationMode);
        Assert.Equal(legacyAllowance, config.Video.VideoAcceleration);
        Assert.Equal(PortableVideoDecodeMode.Software, config.Video.PortableDecodeMode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Disabled_request_retains_backend_device_and_legacy_allowance(bool legacyAllowance)
    {
        var settings = new PlaybackSettings
        {
            VideoHardwareAccelerationEnabled = false,
            VideoHardwareDecodeMode = PortableVideoDecodeMode.VAAPI,
            VideoHardwareDevice = "/dev/dri/renderD129"
        };
        var config = new Config(test: true); config.Video.VideoAcceleration = legacyAllowance;
        config.Video.PortableDecodeMode = PortableVideoDecodeMode.D3D11VA;
        config.Video.PortableHardwareDevice = "old-device";

        settings.ApplyTo(config, includeAudio: false);

        Assert.Equal(PortableVideoDecodeMode.Software, config.Video.PortableDecodeMode);
        Assert.Equal("/dev/dri/renderD129", config.Video.PortableHardwareDevice);
        Assert.Equal(legacyAllowance, config.Video.VideoAcceleration);
        Assert.Equal(PortableVideoDecodeMode.VAAPI, settings.VideoHardwareDecodeMode);
        Assert.False(settings.VideoHardwareAccelerationEnabled);
    }

    [Fact]
    public void Explicit_enable_maps_the_remembered_adapter_identity_and_restores_the_legacy_allowance()
    {
        var settings = new PlaybackSettings
        {
            VideoHardwareAccelerationEnabled = true,
            VideoHardwareDecodeMode = PortableVideoDecodeMode.D3D11VA,
            VideoHardwareDevice = "dxgi:luid:1234"
        };
        var config = new Config(test: true); config.Video.VideoAcceleration = false;

        settings.ApplyTo(config, includeAudio: false);

        Assert.True(config.Video.VideoAcceleration);
        Assert.Equal(PortableVideoDecodeMode.D3D11VA, config.Video.PortableDecodeMode);
        Assert.Equal("dxgi:luid:1234", config.Video.PortableHardwareDevice);
    }

    [Fact]
    public void Software_config_readback_preserves_saved_backend_device_and_presentation_across_platforms()
    {
        var remembered = new PlaybackSettings
        {
            VideoHardwareAccelerationEnabled = false,
            VideoHardwareDecodeMode = PortableVideoDecodeMode.VAAPI,
            VideoHardwareDevice = "/dev/dri/renderD129",
            VideoPresentationMode = PortableVideoPresentationMode.OpenGL
        };
        var config = new Config(test: true); config.Video.VideoAcceleration = true;
        config.Video.PortableDecodeMode = PortableVideoDecodeMode.Software;

        PlaybackSettings current = PlaybackSettings.FromConfig(config, new EngineConfig(), rememberedPreferences: remembered);

        Assert.False(current.VideoHardwareAccelerationEnabled);
        Assert.Equal(PortableVideoDecodeMode.VAAPI, current.VideoHardwareDecodeMode);
        Assert.Equal("/dev/dri/renderD129", current.VideoHardwareDevice);
        Assert.Equal(PortableVideoPresentationMode.OpenGL, current.VideoPresentationMode);
    }

    [Fact]
    public void Hardware_draft_merges_each_edited_field_into_fresh_preferences()
    {
        var baseline = new PlaybackSettings();
        PlaybackSettings draft = baseline.Clone(); draft.VideoHardwareAccelerationEnabled = true;
        draft.VideoPresentationMode = PortableVideoPresentationMode.OpenGL;
        var fresh = new PlaybackSettings
        {
            VideoHardwareDecodeMode = PortableVideoDecodeMode.VAAPI,
            VideoHardwareDevice = "/dev/dri/renderD130", AudioVolumeDefault = 91
        };

        PlaybackSettings merged = draft.MergeEdits(baseline, fresh);

        Assert.True(merged.VideoHardwareAccelerationEnabled);
        Assert.Equal(PortableVideoPresentationMode.OpenGL, merged.VideoPresentationMode);
        Assert.Equal(PortableVideoDecodeMode.VAAPI, merged.VideoHardwareDecodeMode);
        Assert.Equal("/dev/dri/renderD130", merged.VideoHardwareDevice);
        Assert.Equal(91, merged.AudioVolumeDefault);
        Assert.False(fresh.VideoHardwareAccelerationEnabled);
    }

    [Fact]
    public void Backend_and_device_edits_preserve_a_fresher_enable_and_presentation_request()
    {
        var baseline = new PlaybackSettings();
        PlaybackSettings draft = baseline.Clone();
        draft.VideoHardwareDecodeMode = PortableVideoDecodeMode.D3D11VA; draft.VideoHardwareDevice = "3";
        var fresh = new PlaybackSettings
        { VideoHardwareAccelerationEnabled = true, VideoPresentationMode = PortableVideoPresentationMode.OpenGL };
        PlaybackSettings merged = draft.MergeEdits(baseline, fresh);
        Assert.Equal(PortableVideoDecodeMode.D3D11VA, merged.VideoHardwareDecodeMode);
        Assert.Equal("3", merged.VideoHardwareDevice);
        Assert.True(merged.VideoHardwareAccelerationEnabled);
        Assert.Equal(PortableVideoPresentationMode.OpenGL, merged.VideoPresentationMode);
    }

    [Theory]
    [InlineData(PortableVideoDecodeMode.Software)]
    [InlineData((PortableVideoDecodeMode)500)]
    public void Remembered_hardware_mode_must_be_a_hardware_choice(PortableVideoDecodeMode mode)
        => Assert.Throws<ArgumentException>(() => new PlaybackSettings { VideoHardwareDecodeMode = mode }.Validate());

    [Fact]
    public void Invalid_presentation_or_control_characters_fail_before_config_mutation()
    {
        var config = new Config(test: true); config.Video.PortableDecodeMode = PortableVideoDecodeMode.Software;
        Assert.Throws<ArgumentException>(() => new PlaybackSettings
        { VideoHardwareAccelerationEnabled = true, VideoHardwareDevice = "device\noption" }.ApplyTo(config));
        Assert.Equal(PortableVideoDecodeMode.Software, config.Video.PortableDecodeMode);
        Assert.Throws<ArgumentException>(() => new PlaybackSettings { VideoPresentationMode = (PortableVideoPresentationMode)500 }.Validate());
    }
}
