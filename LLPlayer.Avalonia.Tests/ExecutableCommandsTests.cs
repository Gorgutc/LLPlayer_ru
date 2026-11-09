using AwesomeAssertions;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public class ExecutableCommandsTests
{
    [Fact]
    public void New_palette_and_debug_defaults_preserve_existing_legacy_engine_custom_chords()
    {
        Dictionary<string, Action> actions = new();
        AppShortcutMap fresh = new(actions);
        (fresh.Find(WpfKey.K, true, false, false)?.ActionName).Should().Be("OpenCommandPalette");
        (fresh.Find(WpfKey.D, true, false, true)?.ActionName).Should().Be("ToggleDebugOverlay");
        KeyBinding[] custom = [new() { Key = WpfKey.K, Ctrl = true, Action = KeyBindingAction.SeekForward },
            new() { Key = WpfKey.D, Ctrl = true, Shift = true, Action = KeyBindingAction.SeekBackward }];
        AppShortcutMap legacy = new(actions, legacyEngineBindings: custom);
        legacy.Find(WpfKey.K, true, false, false).Should().BeNull();
        legacy.Find(WpfKey.D, true, false, true).Should().BeNull();
        legacy.Find(WpfKey.B, true, false, false)!.ActionName.Should().Be(AppShortcutMap.ToggleSidebar);
        AppShortcutMap explicitPalette = new(actions,
            new Dictionary<string, string> { ["OpenCommandPalette"] = "Ctrl+Shift+K" }, legacyEngineBindings: custom);
        explicitPalette.Find(WpfKey.K, true, false, true)!.ActionName.Should().Be("OpenCommandPalette");
    }

    [Fact]
    public void Catalog_uses_current_chords_and_actual_resolved_owner_with_live_availability()
    {
        bool available = false;
        int calls = 0;
        KeyBinding[] bindings = [new() { Action = KeyBindingAction.TogglePlayPause, Key = WpfKey.Return },
            new() { Action = KeyBindingAction.TogglePlayPause, Key = WpfKey.Space, IsEnabled = false }];
        var rows = ExecutableCommandCatalog.Build(new AppShortcutMap(new Dictionary<string, Action>()), bindings,
            action => action == KeyBindingAction.TogglePlayPause ? () => calls++ : null,
            new Dictionary<string, Action>(), _ => available);
        ExecutableCommand row = rows.Single(r => r.Id == "engine:TogglePlayPause");
        row.Keys.Should().Equal("Enter");
        row.CanExecute.Should().BeFalse();
        row.TryExecute().Should().BeFalse();
        calls.Should().Be(0);
        available = true;
        row.CanExecute.Should().BeTrue();
        row.Command.Execute(null);
        calls.Should().Be(1);
    }

    [Fact]
    public void Palette_excludes_itself_searches_description_action_and_chord_and_selects_first_result()
    {
        ExecutableCommand[] rows = [Row("OpenCommandPalette", "Open Command Palette", "Ctrl+K"),
            Row("OpenWindowSettings", "Open Settings Window", "Ctrl+,"), Row("ToggleSidebar", "Toggle Subtitles Sidebar", "Ctrl+B")];
        CommandPaletteViewModel model = new(rows);
        model.Rows.Select(r => r.ActionName).Should().Equal("OpenWindowSettings", "ToggleSidebar");
        model.SelectedCommand.Should().BeSameAs(rows[1]);
        foreach (string query in new[] { "settings window", "OpenWindowSettings", "Ctrl+," })
        {
            model.FilterText = query;
            model.Rows.Should().ContainSingle().Which.Should().BeSameAs(rows[1]);
            model.SelectedCommand.Should().BeSameAs(rows[1]);
        }
        model.FilterText = "missing";
        model.Rows.Should().BeEmpty();
        model.SelectedCommand.Should().BeNull();
    }

    [Fact]
    public void Activation_closes_before_calling_owner_once_and_cancel_does_not_execute()
    {
        List<string> events = [];
        CommandPaletteViewModel model = new([Row("OpenWindowSettings", "Settings", "", () => events.Add("owner"))]);
        model.TryActivateSelected(() => events.Add("close")).Should().BeTrue();
        model.TryActivateSelected(() => events.Add("close-again")).Should().BeFalse();
        events.Should().Equal("close", "owner");

        CommandPaletteViewModel canceled = new([Row("AppExit", "Exit", "", () => events.Add("exit"))]);
        canceled.Cancel(() => events.Add("cancel"));
        canceled.TryActivateSelected(() => events.Add("late-close")).Should().BeFalse();
        events.Should().Equal("close", "owner", "cancel");
    }

    [Fact]
    public void Refresh_preserves_filter_and_selects_the_current_first_match_including_disabled_rows()
    {
        CommandPaletteViewModel model = new([Row("ToggleSidebar", "Sidebar", "Ctrl+B")]) { FilterText = "Ctrl+B" };
        ExecutableCommand current = Row("OpenWindowSettings", "Settings", "Ctrl+B", null);
        model.Refresh([Row("ToggleSidebar", "Sidebar", "Ctrl+Shift+B"), current]);
        model.FilterText.Should().Be("Ctrl+B");
        model.Rows.Should().ContainSingle().Which.Should().BeSameAs(current);
        model.SelectedCommand.Should().BeSameAs(current);
        current.CanExecute.Should().BeFalse();
        model.TryActivateSelected(() => throw new InvalidOperationException("disabled commands must not close")).Should().BeFalse();
    }

    static ExecutableCommand Row(string action, string description, string chord, Action? execute = null)
        => new("app:" + action, action, description, "Window", chord.Length == 0 ? [] : [chord], execute);
}
