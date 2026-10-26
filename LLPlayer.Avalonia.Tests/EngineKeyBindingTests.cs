using System.Text.Json;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Engine key-binding editor: defaults mapping, prefs persistence, detached draft validation and collision policy.</summary>
public class EngineKeyBindingTests
{
    [Fact]
    public void Engine_defaults_map_every_builtin_binding_and_skip_custom_actions()
    {
        IReadOnlyList<EngineKeyBindingInfo> defaults = EngineKeyBindings.Defaults();
        defaults.Should().NotBeEmpty();
        defaults.Should().OnlyContain(b => b.Action != KeyBindingAction.Custom && b.Key != WpfKey.None);
        defaults.Should().Contain(b => b.Action == KeyBindingAction.TogglePlayPause && b.Key == WpfKey.Space && !b.Ctrl && !b.Alt && !b.Shift);
        defaults.Should().Contain(b => b.Action == KeyBindingAction.OpenFromFileDialog && b.Key == WpfKey.O && b.Ctrl);
        defaults.Should().Contain(b => b.Action == KeyBindingAction.ToggleFullScreen && b.Key == WpfKey.F);
        EngineKeyBindings.Describe(KeyBindingAction.TogglePlayPause).Should().Be("Toggle play playback");
    }

    [Fact]
    public void Prefs_roundtrip_engine_key_overrides_and_absent_keys_keep_engine_defaults()
    {
        PrefsStore store = new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "prefs.json"));
        AppPrefs prefs = new()
        {
            EngineKeysCustomized = true,
            EngineKeyBindings =
            [
                new EngineKeyBindingPref { Action = "TogglePlayPause", Key = "Enter", IsEnabled = true },
                new EngineKeyBindingPref { Action = "ToggleMute", Key = "M", Shift = true, IsEnabled = false },
            ],
        };
        store.Save(prefs);
        AppPrefs loaded = store.Load();
        loaded.EngineKeysCustomized.Should().BeTrue();
        loaded.EngineKeyBindings.Should().HaveCount(2);
        loaded.EngineKeyBindings[0].Action.Should().Be("TogglePlayPause");
        loaded.EngineKeyBindings[0].Key.Should().Be("Enter");
        loaded.EngineKeyBindings[1].Shift.Should().BeTrue();
        loaded.EngineKeyBindings[1].IsEnabled.Should().BeFalse();

        AppPrefs legacy = new();
        legacy.Normalize();
        legacy.EngineKeysCustomized.Should().BeFalse();
        legacy.EngineKeyBindings.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_drops_unknown_engine_actions_and_unparsable_keys_but_keeps_order()
    {
        AppPrefs prefs = new()
        {
            EngineKeysCustomized = true,
            EngineKeyBindings =
            [
                new EngineKeyBindingPref { Action = "TogglePlayPause", Key = "Space" },
                new EngineKeyBindingPref { Action = "NotAnAction", Key = "A" },
                new EngineKeyBindingPref { Action = "ToggleMute", Key = "NotAKey" },
                new EngineKeyBindingPref { Action = "ToggleFullScreen", Key = "F", IsKeyUp = true },
            ],
        };
        prefs.Normalize();
        prefs.EngineKeyBindings.Select(b => b.Action).Should().Equal("TogglePlayPause", "ToggleFullScreen");
        prefs.EngineKeyBindings[1].IsKeyUp.Should().BeTrue();
    }

    [Fact]
    public void Draft_loads_engine_defaults_or_persisted_overrides()
    {
        var (defaultMain, _, _, _, _) = TestVm.Create();
        using var ownedDefault = defaultMain;
        SettingsViewModel defaultDraft = defaultMain.CreateSettings();
        defaultDraft.EngineKeyRows.Should().HaveSameCount(EngineKeyBindings.Defaults());
        defaultDraft.EngineKeysCustomized.Should().BeFalse();

        AppPrefs prefs = new()
        {
            EngineKeysCustomized = true,
            EngineKeyBindings = [new EngineKeyBindingPref { Action = "TogglePlayPause", Key = "Enter" }],
        };
        FakePlaybackController player = new();
        using var main = new MainWindowViewModel(player, prefs.Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        draft.EngineKeyRows.Should().ContainSingle();
        draft.EngineKeyRows[0].ActionName.Should().Be("TogglePlayPause");
        draft.EngineKeyRows[0].Chord.Should().Be("Enter");
    }

    [Fact]
    public void Startup_applies_persisted_engine_keys_only_when_customized()
    {
        FakePlaybackController player = new();
        AppPrefs customized = new()
        {
            EngineKeysCustomized = true,
            EngineKeyBindings = [new EngineKeyBindingPref { Action = "TogglePlayPause", Key = "Enter" }],
        };
        using (var main = new MainWindowViewModel(player, customized.Normalize(), _ => { }, new FakeTranslator(), new FakeShell()))
        {
            player.AppliedEngineKeyBindings.Should().ContainSingle();
            player.AppliedEngineKeyBindings[0].Action.Should().Be(KeyBindingAction.TogglePlayPause);
            player.AppliedEngineKeyBindings[0].Key.Should().Be(WpfKey.Enter);
        }

        FakePlaybackController plain = new();
        using var plainMain = new MainWindowViewModel(plain, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        plain.AppliedEngineKeyBindings.Should().BeEmpty("a non-customized profile keeps the engine defaults");
    }

    [Fact]
    public void Save_persists_edited_engine_keys_and_applies_them_live()
    {
        var (main, player, _, prefs, saved) = TestVm.Create(); using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        EngineKeyEditRow pause = draft.EngineKeyRows.First(r => r.Action == KeyBindingAction.TogglePlayPause);
        pause.Chord = "Enter";
        pause.IsEnabled = true;
        draft.SaveCommand.Execute(null);

        Assert.Null(draft.Error); Assert.Single(saved);
        saved[0].EngineKeysCustomized.Should().BeTrue();
        saved[0].EngineKeyBindings.Should().Contain(b => b.Action == "TogglePlayPause" && b.Key == "Enter");
        player.AppliedEngineKeyBindings.Should().Contain(b => b.Action == KeyBindingAction.TogglePlayPause && b.Key == WpfKey.Enter);
    }

    [Fact]
    public void Duplicate_enabled_engine_chords_block_save_but_disabled_duplicates_are_allowed()
    {
        var (main, player, _, prefs, saved) = TestVm.Create(); using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        EngineKeyEditRow first = draft.EngineKeyRows.First(r => r.Action == KeyBindingAction.TogglePlayPause);
        EngineKeyEditRow second = draft.EngineKeyRows.First(r => r.Action == KeyBindingAction.ToggleMute);
        second.Chord = first.Chord;
        second.IsEnabled = true;
        draft.SaveCommand.Execute(null);
        Assert.NotNull(draft.Error); Assert.Empty(saved);
        player.AppliedEngineKeyBindings.Should().BeEmpty();

        second.IsEnabled = false;
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error); Assert.Single(saved);
    }

    [Fact]
    public void App_vs_engine_collision_blocks_both_directions_and_ignores_disabled_engine_rows()
    {
        var (main, player, _, prefs, saved) = TestVm.Create(); using var owned = main;

        // Engine -> app collision: Ctrl+F is the default ActivateSubsSearch app chord.
        SettingsViewModel engineDraft = main.CreateSettings();
        EngineKeyEditRow fs = engineDraft.EngineKeyRows.First(r => r.Action == KeyBindingAction.ToggleFullScreen);
        fs.Chord = "Ctrl+F";
        engineDraft.SaveCommand.Execute(null);
        Assert.NotNull(engineDraft.Error); Assert.Empty(saved); player.AppliedEngineKeyBindings.Should().BeEmpty();

        // Disabled engine rows are ignored by the collision policy.
        fs.IsEnabled = false;
        engineDraft.SaveCommand.Execute(null);
        Assert.Null(engineDraft.Error); Assert.Single(saved);

        // App -> engine collision: changing an app shortcut to the engine's Space must be rejected too.
        var (main2, player2, _, prefs2, saved2) = TestVm.Create(); using var owned2 = main2;
        SettingsViewModel appDraft = main2.CreateSettings();
        ShortcutEditRow toggleSidebar = appDraft.ShortcutRows.First(r => r.ActionName == AppShortcutMap.ToggleSidebar);
        toggleSidebar.Chord = "Space";
        appDraft.SaveCommand.Execute(null);
        Assert.NotNull(appDraft.Error); Assert.Empty(saved2);
    }

    [Fact]
    public void Reset_restores_engine_defaults_and_clears_the_customized_flag()
    {
        AppPrefs prefs = new()
        {
            EngineKeysCustomized = true,
            EngineKeyBindings = [new EngineKeyBindingPref { Action = "TogglePlayPause", Key = "Enter" }],
        };
        FakePlaybackController player = new();
        using var main = new MainWindowViewModel(player, prefs.Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        draft.ResetEngineKeysCommand.Execute(null);
        draft.SaveCommand.Execute(null);

        Assert.Null(draft.Error);
        prefs.EngineKeysCustomized.Should().BeFalse();
        prefs.EngineKeyBindings.Should().BeEmpty();
        player.CurrentEngineKeyBindings.Should().HaveSameCount(EngineKeyBindings.Defaults());
        player.CurrentEngineKeyBindings.Should().Contain(b => b.Action == KeyBindingAction.TogglePlayPause && b.Key == WpfKey.Space);
    }

    [Fact]
    public void Write_failure_does_not_apply_engine_keys_live()
    {
        FakePlaybackController player = new();
        bool fail = true;
        using var main = new MainWindowViewModel(player, new AppPrefs(), _ => { if (fail) throw new IOException("disk full"); }, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        draft.EngineKeyRows.First(r => r.Action == KeyBindingAction.TogglePlayPause).Chord = "Enter";
        draft.SaveCommand.Execute(null);
        Assert.Contains("disk full", draft.Error!);
        player.AppliedEngineKeyBindings.Should().BeEmpty();
        player.CurrentEngineKeyBindings.Should().BeEmpty();
    }
}
