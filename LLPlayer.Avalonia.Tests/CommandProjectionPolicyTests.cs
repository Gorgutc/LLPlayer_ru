using Avalonia.Headless.XUnit;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public class CommandProjectionPolicyTests
{
    [Fact]
    public void Engine_projection_resolves_only_enabled_action_groups_and_uses_only_their_current_chords()
    {
        List<KeyBindingAction> resolved = [];
        int calls = 0;
        KeyBinding[] bindings =
        [
            new() { Action = KeyBindingAction.TogglePlayPause, Key = WpfKey.Return },
            new() { Action = KeyBindingAction.TogglePlayPause, Key = WpfKey.Return, IsKeyUp = true },
            new() { Action = KeyBindingAction.TogglePlayPause, Key = WpfKey.P, Ctrl = true },
            new() { Action = KeyBindingAction.TogglePlayPause, Key = WpfKey.Space, IsEnabled = false },
            new() { Action = KeyBindingAction.ABLoopSetStart, Key = WpfKey.F6, IsEnabled = false },
        ];
        var rows = ExecutableCommandCatalog.Build(EmptyShortcuts(), bindings,
            action => { resolved.Add(action); return () => calls++; }, new Dictionary<string, Action>());

        ExecutableCommand row = Assert.Single(rows);
        Assert.Equal("engine:TogglePlayPause", row.Id);
        Assert.Equal(["Enter", "Ctrl+P"], row.Keys);
        Assert.Equal([KeyBindingAction.TogglePlayPause], resolved);
        Assert.True(row.TryExecute());
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(KeyBindingAction.ToggleRecording)]
    [InlineData(KeyBindingAction.TakeSnapshot)]
    [InlineData(KeyBindingAction.ToggleVideoAcceleration)]
    public void Explicit_unbound_media_alias_uses_its_registered_app_owner_without_adding_a_chord(KeyBindingAction action)
    {
        int calls = 0;
        Dictionary<string, Action> owners = new() { [action.ToString()] = () => calls++ };
        AppShortcutMap shortcuts = EmptyShortcuts();
        var rows = ExecutableCommandCatalog.Build(shortcuts, [],
            _ => throw new InvalidOperationException("Unbound actions have no engine binding owner to resolve."), owners);

        ExecutableCommand row = Assert.Single(rows);
        Assert.Equal("app:" + action, row.Id);
        Assert.Empty(row.Keys);
        Assert.True(row.TryExecute());
        Assert.Equal(1, calls);
        Assert.Empty(shortcuts.Shortcuts);
    }

    [Theory]
    [InlineData(KeyBindingAction.ToggleRecording, true)]
    [InlineData(KeyBindingAction.ToggleRecording, false)]
    [InlineData(KeyBindingAction.TakeSnapshot, true)]
    [InlineData(KeyBindingAction.TakeSnapshot, false)]
    [InlineData(KeyBindingAction.ToggleVideoAcceleration, true)]
    [InlineData(KeyBindingAction.ToggleVideoAcceleration, false)]
    public void Any_current_engine_binding_suppresses_the_fallback_alias_including_disabled_only_rows(KeyBindingAction action, bool enabled)
    {
        int engineCalls = 0, appCalls = 0;
        List<KeyBindingAction> resolved = [];
        var rows = ExecutableCommandCatalog.Build(EmptyShortcuts(),
            [new() { Action = action, Key = WpfKey.F7, Ctrl = true, IsEnabled = enabled }],
            current => { resolved.Add(current); return () => engineCalls++; },
            new Dictionary<string, Action> { [action.ToString()] = () => appCalls++ });

        Assert.DoesNotContain(rows, row => row.Id == "app:" + action);
        if (enabled)
        {
            ExecutableCommand row = Assert.Single(rows);
            Assert.Equal("engine:" + action, row.Id);
            Assert.Equal(["Ctrl+F7"], row.Keys);
            Assert.True(row.TryExecute());
            Assert.Equal(1, engineCalls);
            Assert.Equal([action], resolved);
        }
        else
        {
            Assert.Empty(rows);
            Assert.Empty(resolved);
            Assert.Equal(0, engineCalls);
        }
        Assert.Equal(0, appCalls);
    }

    [Fact]
    public void Unbound_implemented_app_commands_remain_in_palette_but_keyboard_help_requires_current_enabled_chords()
    {
        Dictionary<string, Action> owners = new()
        {
            [AppShortcutMap.OpenWindowSettings] = () => { },
            [AppShortcutMap.ToggleSidebar] = () => { },
            [AppShortcutMap.ToggleTheme] = () => { },
            [nameof(KeyBindingAction.TakeSnapshot)] = () => { },
        };
        AppShortcutMap shortcuts = new(owners, explicitRows:
        [
            new() { ActionName = AppShortcutMap.ToggleSidebar, Key = "F8", Ctrl = true },
            new() { ActionName = AppShortcutMap.ToggleTheme, Key = "F9", IsEnabled = false },
        ]);
        var rows = ExecutableCommandCatalog.Build(shortcuts, [], _ => null, owners);
        CommandPaletteViewModel palette = new(rows);
        Assert.Contains(palette.Rows, row => row.Id == "app:OpenWindowSettings" && row.Keys.Count == 0);
        Assert.Contains(palette.Rows, row => row.Id == "app:TakeSnapshot" && row.Keys.Count == 0);

        CheatSheetViewModel help = new(rows, new AppPrefs());
        Assert.Equal(1, help.TotalCount);
        ShortcutRowViewModel bound = Assert.Single(Assert.Single(help.Groups).Rows);
        Assert.Equal(AppShortcutMap.ToggleSidebar, bound.ActionName);
        Assert.Equal(["Ctrl", "F8"], Assert.Single(bound.Chords).Keys);
        help.FilterText = "TakeSnapshot";
        Assert.Empty(help.Groups);
        Assert.Equal("0 of 1", help.HitText);

        // Removing the last enabled binding refreshes the same open help model without losing its filter.
        var changed = ExecutableCommandCatalog.Build(EmptyShortcuts(), [], _ => null, owners);
        help.Refresh(changed, new AppPrefs());
        Assert.Equal("TakeSnapshot", help.FilterText);
        Assert.Equal(0, help.TotalCount);
        Assert.Empty(help.Groups);
        Assert.Equal("0 of 0", help.HitText);
    }

    [AvaloniaFact]
    public void View_model_fallback_aliases_reuse_the_existing_record_snapshot_and_hardware_owners()
    {
        ProjectionPlaybackController player = new() { IsOpened = true };
        using MainWindowViewModel model = new(player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        var rows = model.BuildExecutableCommands();
        foreach (string name in new[] { "ToggleRecording", "TakeSnapshot", "ToggleVideoAcceleration" })
        {
            ExecutableCommand row = Assert.Single(rows, current => current.Id == "app:" + name);
            Assert.Empty(row.Keys);
            Assert.True(row.TryExecute());
        }
        Assert.Equal([KeyBindingAction.ToggleRecording, KeyBindingAction.TakeSnapshot, KeyBindingAction.ToggleVideoAcceleration], player.Actions);
        Assert.Empty(player.KeysDown);
        Assert.Empty(player.KeysUp);
    }

    static AppShortcutMap EmptyShortcuts() => new(new Dictionary<string, Action>(), explicitRows: []);

    sealed class ProjectionPlaybackController : FakePlaybackController, IPlaybackController
    {
        public List<KeyBindingAction> Actions { get; } = [];
        Action? IPlaybackController.ResolveEngineAction(KeyBindingAction action) => () => Actions.Add(action);
    }
}
