using FlyleafLib;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public class SettingsPlaybackLatencyTests
{
    [Theory]
    [InlineData(20000, 1000, 500)]
    [InlineData(1000, 30000, 500)]
    public void Latency_validation_preserves_the_engine_buffer_invariants(long latency, long maximumBuffer, long minimumBuffer)
    {
        var settings = new PlaybackSettings { MaxLatencyMs = latency, BufferDurationMs = maximumBuffer, MinBufferDurationMs = minimumBuffer };
        Assert.Throws<ArgumentException>(settings.Validate);
    }

    [Fact]
    public void Disabling_maximum_latency_keeps_low_delay_enabled_when_zero_latency_remains_enabled()
    {
        Config config = ConnectedConfig();
        config.Player.MaxLatency = TimeSpan.FromMilliseconds(5000).Ticks;
        config.Player.ZeroLatency = true;
        Assert.True(config.Decoder.LowDelay);
        var settings = PlaybackSettings.FromConfig(config, new EngineConfig());
        settings.MaxLatencyMs = 0;
        settings.ApplyTo(config, includeAudio: false);
        Assert.True(config.Player.ZeroLatency); Assert.Equal(0, config.Player.MaxLatency);
        Assert.True(config.Decoder.LowDelay);
    }

    [Fact]
    public void Valid_latency_policy_applies_a_consistent_buffer_and_low_delay_state()
    {
        Config config = ConnectedConfig();
        var settings = new PlaybackSettings { MaxLatencyMs = 20000, BufferDurationMs = 40000, MinBufferDurationMs = 1000 };
        settings.ApplyTo(config, includeAudio: false);
        Assert.Equal(TimeSpan.FromMilliseconds(20000).Ticks, config.Player.MaxLatency);
        Assert.Equal(TimeSpan.FromMilliseconds(40000).Ticks, config.Demuxer.BufferDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(1000).Ticks, config.Player.MinBufferDuration);
        Assert.True(config.Decoder.LowDelay);
    }

    static Config ConnectedConfig()
    {
        // The ordinary constructor installs these links after plugin discovery. Supply only that native-free
        // linkage so this regression exercises the real coupled setters without starting the global engine.
        Config config = new(true);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(Config.PlayerConfig).GetField("config", flags)!.SetValue(config.Player, config);
        typeof(Config.DemuxerConfig).GetField("config", flags)!.SetValue(config.Demuxer, config);
        return config;
    }
}
