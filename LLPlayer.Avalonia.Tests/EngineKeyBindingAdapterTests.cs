using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Real-engine round-trip for the Avalonia engine key-binding apply seam (env-gated like other engine ITs).</summary>
[Collection(FlyleafGlobalsCollection.Name)]
public sealed class EngineKeyBindingAdapterTests
{
    static (Player Player, FlyleafPlaybackController Controller) Create()
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpeg), "Portable controller integration requires existing FFmpeg shared libraries.");
        AvaloniaUIDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
        if (!Engine.IsLoaded)
            EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg!, new(Path.GetTempPath(), Path.GetTempPath())));
        Player player = new(EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize()));
        return (player, new FlyleafPlaybackController(player, dispatcher, bridge));
    }

    [Fact]
    public void Real_adapter_apply_get_roundtrip_preserves_defaults_edits_and_custom_rows()
    {
        (Player player, FlyleafPlaybackController controller) = Create();
        IReadOnlyList<EngineKeyBindingInfo> defaults = EngineKeyBindings.Defaults();

        controller.ApplyEngineKeyBindings(defaults);
        controller.GetEngineKeyBindings().Should().Equal(defaults);

        List<EngineKeyBindingInfo> edited = [.. defaults];
        edited[0] = edited[0] with { Key = WpfKey.Enter, IsEnabled = false, IsKeyUp = true };
        controller.ApplyEngineKeyBindings(edited);
        IReadOnlyList<EngineKeyBindingInfo> applied = controller.GetEngineKeyBindings();
        applied.Should().HaveCount(edited.Count);
        applied[0].Key.Should().Be(WpfKey.Enter);
        applied[0].IsEnabled.Should().BeFalse();
        applied[0].IsKeyUp.Should().BeTrue();

        // Engine-owned custom rows (none in the Avalonia app today) must survive a replacement apply.
        player.Config.Player.KeyBindings.Keys.Add(new KeyBinding
        {
            Action = KeyBindingAction.Custom,
            ActionName = "ProbeCustom",
            Key = WpfKey.F9,
            IsKeyUp = true,
        });
        controller.ApplyEngineKeyBindings(defaults);
        controller.GetEngineKeyBindings().Should().HaveCount(defaults.Count + 1);
        controller.GetEngineKeyBindings().Should().Contain(binding => binding.Action == KeyBindingAction.Custom
            && binding.Key == WpfKey.F9 && binding.IsKeyUp);
    }
}
