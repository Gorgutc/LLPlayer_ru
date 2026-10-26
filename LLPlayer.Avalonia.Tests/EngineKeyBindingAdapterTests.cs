using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Real-engine apply and direct-dispatch checks, without OS keyboard or native desktop acceptance.</summary>
[Collection(FlyleafGlobalsCollection.Name)]
public sealed class EngineKeyBindingAdapterTests
{
    static string RequireFFmpeg()
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpeg), "Portable controller integration requires existing FFmpeg shared libraries.");
        return ffmpeg!;
    }

    static (Player Player, FlyleafPlaybackController Controller) Create()
    {
        string ffmpeg = RequireFFmpeg();
        AvaloniaUIDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
        if (!Engine.IsLoaded)
            EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg, new(Path.GetTempPath(), Path.GetTempPath())));
        Player player = new(EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize()));
        try
        {
            return (player, new FlyleafPlaybackController(player, dispatcher, bridge));
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }

    static void WithPlayer(Action<Player, FlyleafPlaybackController> scenario)
    {
        (Player player, FlyleafPlaybackController controller) = Create();
        try
        {
            scenario(player, controller);
        }
        finally
        {
            try { controller.Dispose(); }
            finally { player.Dispose(); }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_adapter_scenario_releases_its_player_and_stops_notifications_even_after_an_assertion_fails(bool failAssertion)
    {
        // Keep the native prerequisite skip outside Record.Exception so it remains a skipped test.
        RequireFFmpeg();
        Player? ownedPlayer = null;
        FlyleafPlaybackController? ownedController = null;
        List<string?> raised = [];
        Exception? failure = Record.Exception(() => WithPlayer((player, controller) =>
        {
            ownedPlayer = player;
            ownedController = controller;
            player.IsDisposed.Should().BeFalse();
            lock (Engine.Players)
                Engine.Players.Should().Contain(player);

            controller.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            player.Config.Subtitles[0].Visible = false;
            Dispatcher.UIThread.RunJobs();
            raised.Should().Contain(nameof(IPlaybackController.SubtitlesVisible), "the live controller observes its player");
            raised.Clear();
            if (failAssertion)
                Assert.Fail("Deliberate assertion failure inside the owned scenario.");
        }));

        try
        {
            if (failAssertion)
            {
                failure.Should().BeAssignableTo<Xunit.Sdk.XunitException>();
                failure!.Message.Should().Contain("Deliberate assertion failure inside the owned scenario.");
            }
            else
                failure.Should().BeNull();
            ownedPlayer.Should().NotBeNull();
            ownedPlayer!.IsDisposed.Should().BeTrue("the scenario must release its native player on every exit");
            lock (Engine.Players)
                Engine.Players.Should().NotContain(ownedPlayer);
            ownedPlayer.Config.Subtitles[0].Visible = true;
            Dispatcher.UIThread.RunJobs();
            raised.Should().BeEmpty("the disposed controller must stop publishing player changes");
        }
        finally
        {
            // Keep a failing lifecycle regression from leaking into the rest of the engine suite.
            try { ownedController?.Dispose(); }
            finally { ownedPlayer?.Dispose(); }
        }
    }

    [AvaloniaFact]
    public void Real_adapter_apply_get_roundtrip_preserves_defaults_edits_and_custom_rows()
    {
        WithPlayer((player, controller) =>
        {
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
        });
    }

    [AvaloniaFact]
    public void Real_adapter_remapped_builtin_executes_the_new_chord_and_retires_the_old_chord()
    {
        WithPlayer((player, controller) =>
        {
            EngineKeyBindingInfo original = new(KeyBindingAction.ToggleSubtitlesVisibilityPrimary, WpfKey.H);
            controller.ApplyEngineKeyBindings([original]);
            player.Config.Subtitles[0].Visible.Should().BeTrue();
            player.Config.Subtitles[1].Visible.Should().BeTrue();
            controller.KeyDown(WpfKey.H).Should().BeTrue();
            player.Config.Subtitles[0].Visible.Should().BeFalse("the original binding must execute before it is remapped");
            controller.KeyUp(WpfKey.H).Should().BeFalse();

            player.Config.Subtitles[0].Visible = true;
            controller.ApplyEngineKeyBindings([original with { Key = WpfKey.Enter }]);
            controller.KeyDown(WpfKey.H).Should().BeFalse();
            controller.KeyUp(WpfKey.H).Should().BeFalse();
            player.Config.Subtitles[0].Visible.Should().BeTrue("the retired chord must not execute");
            controller.KeyDown(WpfKey.Enter).Should().BeTrue();
            player.Config.Subtitles[0].Visible.Should().BeFalse("the new binding must carry the real built-in action");
            player.Config.Subtitles[1].Visible.Should().BeTrue("the action must target the selected subtitle slot");
            controller.KeyUp(WpfKey.Enter).Should().BeFalse();
        });
    }

    [AvaloniaFact]
    public void Real_adapter_key_up_builtin_executes_once_on_matching_release()
    {
        WithPlayer((player, controller) =>
        {
            controller.ApplyEngineKeyBindings([new(KeyBindingAction.ToggleSubtitlesVisibilityPrimary, WpfKey.F8, IsKeyUp: true)]);
            player.Config.Subtitles[0].Visible.Should().BeTrue();
            controller.KeyUp(WpfKey.F8).Should().BeFalse("release without a preceding press must not execute");
            controller.KeyDown(WpfKey.F8).Should().BeTrue();
            controller.KeyDown(WpfKey.F8).Should().BeTrue("auto-repeat must not execute a release binding");
            player.Config.Subtitles[0].Visible.Should().BeTrue("key-down must only arm the release binding");
            controller.KeyUp(WpfKey.F9).Should().BeFalse();
            player.Config.Subtitles[0].Visible.Should().BeTrue("an unrelated release must not execute the pending binding");
            controller.KeyUp(WpfKey.F8).Should().BeTrue();
            player.Config.Subtitles[0].Visible.Should().BeFalse();
            controller.KeyUp(WpfKey.F8).Should().BeFalse();
            player.Config.Subtitles[0].Visible.Should().BeFalse("the same release must not execute twice");
            controller.KeyDown(WpfKey.F8).Should().BeTrue();
            controller.KeyUp(WpfKey.F8).Should().BeTrue();
            player.Config.Subtitles[0].Visible.Should().BeTrue("a subsequent press/release must execute once again");
        });
    }

    [AvaloniaFact]
    public void Real_adapter_reapply_keeps_the_original_custom_delegate_executable()
    {
        WithPlayer((player, controller) =>
        {
            int calls = 0;
            Action probe = () => calls++;
            KeyBinding custom = new() { Action = KeyBindingAction.Custom, ActionName = "ProbeCustom", Key = WpfKey.F9 };
            custom.SetAction(probe, true);
            player.Config.Player.KeyBindings.Keys.Add(custom);

            controller.ApplyEngineKeyBindings(EngineKeyBindings.Defaults());
            player.Config.Player.KeyBindings.Keys.Single(binding => binding.Action == KeyBindingAction.Custom).Should().BeSameAs(custom);
            custom.ActionInternal.Should().BeSameAs(probe);
            controller.KeyDown(WpfKey.F9).Should().BeTrue();
            calls.Should().Be(0);
            controller.KeyUp(WpfKey.F9).Should().BeTrue();
            calls.Should().Be(1, "the preserved custom row must invoke its original delegate");
            controller.KeyUp(WpfKey.F9).Should().BeFalse();
            calls.Should().Be(1);

            controller.ApplyEngineKeyBindings(EngineKeyBindings.Defaults());
            controller.KeyDown(WpfKey.F9).Should().BeTrue();
            controller.KeyUp(WpfKey.F9).Should().BeTrue();
            calls.Should().Be(2, "reapplying must preserve the existing custom delegate without duplicating it");
        });
    }
}
