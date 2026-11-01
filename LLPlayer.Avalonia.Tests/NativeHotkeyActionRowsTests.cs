using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Actual native delegates after editor Add/Clone/reassign/save, without physical keyboard/desktop claims.</summary>
[Collection(FlyleafGlobalsCollection.Name)]
public sealed class NativeHotkeyActionRowsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Added_unbound_AB_action_survives_disk_reload_and_sets_the_real_paused_position(bool onRelease)
    {
        string ffmpeg = RequireFFmpeg();
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrEmpty(media) || !File.Exists(media),
            "Real A-B shortcut integration requires existing LLPLAYER_TEST_MEDIA.");
        using TempDir state = new();
        var (player, controller) = Create(ffmpeg, state.Path);
        try
        {
            PrefsStore store = new(Path.Combine(state.Path, "preferences.json"));
            using (MainWindowViewModel original = new(controller, new AppPrefs().Normalize(), store.Save,
                       new FakeTranslator(), new FakeShell()))
            {
                SettingsViewModel draft = original.CreateSettings();
                Assert.DoesNotContain(draft.EngineKeyRows, row => row.Action == KeyBindingAction.ABLoopSetStart);
                draft.AddEngineKeyCommand.Execute(null);
                EngineKeyEditRow added = draft.EngineKeyRows[^1];
                added.Action = KeyBindingAction.ABLoopSetStart;
                added.Chord = "F6";
                added.IsKeyUp = onRelease;
                draft.SaveCommand.Execute(null);
                Assert.Null(draft.Error);
            }

            AppPrefs reloadedPrefs = store.Load();
            using MainWindowViewModel reloaded = new(controller, reloadedPrefs, store.Save,
                new FakeTranslator(), new FakeShell());
            EngineKeyEditRow restored = Assert.Single(reloaded.CreateSettings().EngineKeyRows,
                row => row.Action == KeyBindingAction.ABLoopSetStart);
            Assert.Equal("F6", restored.Chord);
            Assert.Equal(onRelease, restored.IsKeyUp);

            controller.Open(media!);
            Assert.True(await WaitUntil(() => controller.IsOpened, TimeSpan.FromSeconds(30)), "fixture did not open");
            controller.Pause();
            controller.SeekTo(TimeSpan.FromSeconds(1));
            Assert.True(await WaitUntil(() => controller.CurTime >= TimeSpan.FromSeconds(1).Ticks,
                TimeSpan.FromSeconds(15)), "fixture did not reach the known position");
            Dispatcher.UIThread.RunJobs();
            long position = player.CurTime;
            Assert.True(player.CanPlay);
            Assert.True(position >= TimeSpan.FromSeconds(1).Ticks);
            Assert.Equal(-1, player.ABLoopA);

            Assert.True(reloaded.HandleKeyDown(WpfKey.F6, false, false, false, false));
            if (onRelease)
            {
                Assert.Equal(-1, player.ABLoopA);
                Assert.True(reloaded.HandleKeyDown(WpfKey.F6, false, false, false, false));
                Assert.Equal(-1, player.ABLoopA);
                reloaded.HandleKeyUp(WpfKey.F7, false);
                Assert.Equal(-1, player.ABLoopA);
            }
            else Assert.Equal(position, player.ABLoopA);
            reloaded.HandleKeyUp(WpfKey.F6, false);
            Assert.Equal(position, player.ABLoopA);
            Assert.Equal(position, controller.ABLoopA);
            reloaded.HandleKeyUp(WpfKey.F6, false);
            Assert.Equal(position, player.ABLoopA);
        }
        finally
        {
            try { controller.Dispose(); }
            finally { player.Dispose(); }
        }
    }

    [AvaloniaFact]
    public void Editor_clone_reassign_app_save_and_reset_keep_native_slot_actions_and_the_original_custom_delegate()
    {
        string ffmpeg = RequireFFmpeg();
        using TempDir state = new();
        var (player, controller) = Create(ffmpeg, state.Path);
        try
        {
            using MainWindowViewModel main = new(controller, new AppPrefs().Normalize(), _ => { },
                new FakeTranslator(), new FakeShell());
            int customCalls = 0;
            Action callback = () => customCalls++;
            KeyBinding custom = new() { Action = KeyBindingAction.Custom, ActionName = "NativeActionRowsProbe", Key = WpfKey.F9 };
            custom.SetAction(callback, true);
            player.Config.Player.KeyBindings.Keys.Add(custom);

            SettingsViewModel draft = main.CreateSettings();
            draft.AddEngineKeyCommand.Execute(null);
            EngineKeyEditRow primary = draft.EngineKeyRows[^1];
            primary.Action = KeyBindingAction.ToggleSubtitlesVisibilityPrimary;
            primary.Chord = "F6";
            primary.CloneCommand.Execute(null);
            EngineKeyEditRow secondary = Assert.Single(draft.EngineKeyRows,
                row => !ReferenceEquals(row, primary) && row.Action == primary.Action && row.Chord == "F6");
            secondary.Action = KeyBindingAction.ToggleSubtitlesVisibilitySecondary;
            secondary.Chord = "F7";
            secondary.IsKeyUp = true;
            Assert.Equal(KeyBindingAction.ToggleSubtitlesVisibilityPrimary, primary.Action);
            Assert.False(primary.IsKeyUp);
            draft.SaveCommand.Execute(null);
            Assert.Null(draft.Error);
            AssertCustomIdentityAndRelease();

            Assert.True(player.Config.Subtitles[0].Visible);
            Assert.True(player.Config.Subtitles[1].Visible);
            Assert.True(main.HandleKeyDown(WpfKey.F6, false, false, false, false));
            main.HandleKeyUp(WpfKey.F6, false);
            Assert.False(player.Config.Subtitles[0].Visible);
            Assert.True(player.Config.Subtitles[1].Visible);
            Assert.True(main.HandleKeyDown(WpfKey.F7, false, false, false, false));
            Assert.True(main.HandleKeyDown(WpfKey.F7, false, false, false, false));
            Assert.True(player.Config.Subtitles[1].Visible);
            main.HandleKeyUp(WpfKey.F7, false);
            Assert.False(player.Config.Subtitles[1].Visible);
            main.HandleKeyUp(WpfKey.F7, false);
            Assert.False(player.Config.Subtitles[1].Visible);

            SettingsViewModel reassign = main.CreateSettings();
            EngineKeyEditRow rebound = Assert.Single(reassign.EngineKeyRows, row => row.Chord == "F6");
            rebound.Action = KeyBindingAction.ToggleSubtitlesVisibilitySecondary;
            reassign.SaveCommand.Execute(null);
            Assert.Null(reassign.Error);
            AssertCustomIdentityAndRelease();
            player.Config.Subtitles[0].Visible = true;
            player.Config.Subtitles[1].Visible = true;
            main.HandleKeyDown(WpfKey.F6, false, false, false, false);
            main.HandleKeyUp(WpfKey.F6, false);
            Assert.True(player.Config.Subtitles[0].Visible);
            Assert.False(player.Config.Subtitles[1].Visible);

            SettingsViewModel appDraft = main.CreateSettings();
            appDraft.AddAppKeyCommand.Execute(null);
            ShortcutEditRow appRow = appDraft.ShortcutRows[^1];
            appRow.ActionName = AppShortcutMap.ToggleSidebar;
            appRow.Chord = "F8";
            appRow.IsKeyUp = true;
            appDraft.SaveCommand.Execute(null);
            Assert.Null(appDraft.Error);
            AssertCustomIdentityAndRelease();
            bool sidebarBefore = main.SidebarVisible;
            main.HandleKeyDown(WpfKey.F8, false, false, false, false);
            Assert.Equal(sidebarBefore, main.SidebarVisible);
            main.HandleKeyUp(WpfKey.F8, false);
            Assert.Equal(!sidebarBefore, main.SidebarVisible);

            SettingsViewModel reset = main.CreateSettings();
            reset.ResetEngineKeysCommand.Execute(null);
            reset.ResetAppKeysCommand.Execute(null);
            reset.SaveCommand.Execute(null);
            Assert.Null(reset.Error);
            AssertCustomIdentityAndRelease();

            void AssertCustomIdentityAndRelease()
            {
                Assert.Same(custom, Assert.Single(player.Config.Player.KeyBindings.Keys,
                    row => row.Action == KeyBindingAction.Custom));
                Assert.Same(callback, custom.ActionInternal);
                int previousCalls = customCalls;
                Assert.True(main.HandleKeyDown(WpfKey.F9, false, false, false, false));
                Assert.True(main.HandleKeyDown(WpfKey.F9, false, false, false, false));
                Assert.Equal(previousCalls, customCalls);
                main.HandleKeyUp(WpfKey.F9, false);
                Assert.Equal(previousCalls + 1, customCalls);
                main.HandleKeyUp(WpfKey.F9, false);
                Assert.Equal(previousCalls + 1, customCalls);
            }
        }
        finally
        {
            try { controller.Dispose(); }
            finally { player.Dispose(); }
        }
    }

    static string RequireFFmpeg()
    {
        string? directory = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(directory), "Native action-row integration requires existing FFmpeg shared libraries.");
        return directory!;
    }

    static (Player Player, FlyleafPlaybackController Controller) Create(string ffmpeg, string state)
    {
        AvaloniaUIDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
        if (!Engine.IsLoaded)
            EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg,
                new(Path.Combine(state, "config"), Path.Combine(state, "state"))));
        Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
        config.Subtitles.SearchLocal = false;
        config.Subtitles.LanguageAutoDetect = false;
        Player player = new(config);
        try { return (player, new FlyleafPlaybackController(player, dispatcher, bridge)); }
        catch { player.Dispose(); throw; }
    }

    static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed >= timeout) return false;
            await Task.Delay(25);
        }
        return true;
    }
}
