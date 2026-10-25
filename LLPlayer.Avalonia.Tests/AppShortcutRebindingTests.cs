using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using AvKey = Avalonia.Input.Key;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

/// <summary>
/// Bounded hotkey rebinding: the app-level shortcuts (sidebar, search, cheat sheet, subtitle size/position, copy,
/// open subtitles, theme) become editable and persisted; engine key bindings stay at their defaults for now.
/// </summary>
public class AppShortcutRebindingTests
{
    [Fact]
    public void Chord_format_and_parse_roundtrip()
    {
        AppShortcutMap.FormatChord(WpfKey.B, true, false, false).Should().Be("Ctrl+B");
        AppShortcutMap.FormatChord(WpfKey.OemQuestion, false, false, true).Should().Be("Shift+OemQuestion");

        AppShortcutMap.TryParseChord("Ctrl+Shift+B", out WpfKey key, out bool ctrl, out bool alt, out bool shift).Should().BeTrue();
        key.Should().Be(WpfKey.B);
        ctrl.Should().BeTrue();
        shift.Should().BeTrue();
        alt.Should().BeFalse();

        AppShortcutMap.TryParseChord("F1", out key, out ctrl, out alt, out shift).Should().BeTrue();
        key.Should().Be(WpfKey.F1);
        ctrl.Should().BeFalse();

        AppShortcutMap.TryParseChord("Ctrl+", out _, out _, out _, out _).Should().BeFalse("a modifier alone is not a chord");
        AppShortcutMap.TryParseChord("Ctrl+NotAKey", out _, out _, out _, out _).Should().BeFalse();
        AppShortcutMap.TryParseChord(" ", out _, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Chord_parser_accepts_aliases_and_rejects_duplicate_modifiers()
    {
        AppShortcutMap.TryParseChord("Shift+↑", out WpfKey key, out _, out _, out bool shift).Should().BeTrue();
        key.Should().Be(WpfKey.Up);
        shift.Should().BeTrue();
        AppShortcutMap.TryParseChord("Esc", out key, out _, out _, out _).Should().BeTrue();
        key.Should().Be(WpfKey.Escape);
        AppShortcutMap.TryParseChord("Enter", out key, out _, out _, out _).Should().BeTrue();
        key.Should().Be(WpfKey.Return);
        AppShortcutMap.TryParseChord("Ctrl+Ctrl+B", out _, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Normalize_drops_default_equal_and_duplicate_overrides()
    {
        Dictionary<string, string> raw = new()
        {
            [AppShortcutMap.OpenWindowCheatSheet] = "F1",           // equal to the first default -> no override
            [AppShortcutMap.ToggleSidebar] = "Ctrl+F",              // duplicates ActivateSubsSearch default -> dropped
            [AppShortcutMap.SubsSizeIncrease] = "Ctrl+Shift+Right",
        };
        AppShortcutMap.TryNormalizeOverrides(raw, out Dictionary<string, string> normalized).Should().BeTrue();
        normalized.Should().NotContainKey(AppShortcutMap.OpenWindowCheatSheet);
        normalized.Should().NotContainKey(AppShortcutMap.ToggleSidebar);
        normalized[AppShortcutMap.SubsSizeIncrease].Should().Be("Ctrl+Shift+Right");

        AppShortcutMap map = new(AppActions([]), normalized);
        map.Shortcuts.Count(s => s.ActionName == AppShortcutMap.OpenWindowCheatSheet).Should().Be(2,
            "an untouched cheat-sheet action keeps both default chords");
    }

    [Fact]
    public void Map_applies_overrides_and_keeps_the_rest_of_the_defaults()
    {
        FakePlaybackController player = new();
        HashSet<string> executed = [];
        AppShortcutMap map = new(AppActions(executed), new Dictionary<string, string>
        {
            [AppShortcutMap.ToggleSidebar] = "Ctrl+Shift+B",
        });

        map.Find(WpfKey.B, true, false, false).Should().BeNull("the overridden default chord must stop matching");
        AppShortcut? custom = map.Find(WpfKey.B, true, false, true);
        custom.Should().NotBeNull();
        custom!.ActionName.Should().Be(AppShortcutMap.ToggleSidebar);
        map.TryExecute(WpfKey.B, true, false, true).Should().BeTrue();
        executed.Should().Contain(AppShortcutMap.ToggleSidebar);

        // untouched actions keep their default chord
        map.TryExecute(WpfKey.F1, false, false, false).Should().BeTrue();
        executed.Should().Contain(AppShortcutMap.OpenWindowCheatSheet);
    }

    [Fact]
    public void Invalid_or_unknown_overrides_fall_back_to_defaults()
    {
        AppShortcutMap map = new(AppActions([]), new Dictionary<string, string>
        {
            [AppShortcutMap.ToggleSidebar] = "Ctrl+NotAKey",
            ["UnknownAction"] = "Ctrl+Q",
        });
        map.Find(WpfKey.B, true, false, false).Should().NotBeNull();
        map.Shortcuts.Should().OnlyContain(s => s.ActionName != "UnknownAction");
    }

    [Fact]
    public void Duplicate_effective_chords_are_rejected()
    {
        Dictionary<string, string> overrides = new()
        {
            [AppShortcutMap.ToggleSidebar] = "Ctrl+F", // already used by ActivateSubsSearch
        };
        AppShortcutMap map = new(AppActions([]), overrides);
        map.TryValidateEffective(out string error).Should().BeFalse();
        error.Should().Contain("Ctrl+F");
    }

    [Fact]
    public void Overrides_roundtrip_through_preferences_and_drop_invalid_entries()
    {
        using TempDir temp = new();
        var store = new PrefsStore(Path.Combine(temp.Path, "preferences.json"));
        AppPrefs prefs = new AppPrefs().Normalize();
        prefs.AppShortcutOverrides[AppShortcutMap.ToggleSidebar] = "Ctrl+Shift+B";
        prefs.AppShortcutOverrides[AppShortcutMap.SubsSizeIncrease] = "Ctrl+NotAKey";
        prefs.AppShortcutOverrides["UnknownAction"] = "Ctrl+Q";
        store.Save(prefs);

        AppPrefs loaded = store.Load().Normalize();
        loaded.AppShortcutOverrides.Should().ContainKey(AppShortcutMap.ToggleSidebar);
        loaded.AppShortcutOverrides.Should().NotContainKey(AppShortcutMap.SubsSizeIncrease);
        loaded.AppShortcutOverrides.Should().NotContainKey("UnknownAction");
    }

    [Fact]
    public void Settings_draft_exposes_every_app_action_and_validates_before_commit()
    {
        AppPrefs prefs = new AppPrefs().Normalize();
        int commits = 0;
        SettingsViewModel draft = new(prefs, _ => commits++, new BitmapSubtitleRuntimeState(1, 1, 100));
        draft.ShortcutRows.Should().HaveCount(AppShortcutMap.Defaults.Select(d => d.ActionName).Distinct().Count());
        draft.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.ToggleSidebar).Chord.Should().Be("Ctrl+B");

        draft.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.ToggleSidebar).Chord = "Ctrl+F";
        draft.SaveCommand.Execute(null);
        commits.Should().Be(0);
        draft.Error.Should().Contain("Ctrl+F");

        draft.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.ToggleSidebar).Chord = "Ctrl+Shift+B";
        draft.SaveCommand.Execute(null);
        commits.Should().Be(1);
        draft.AppShortcuts[AppShortcutMap.ToggleSidebar].Should().Be("Ctrl+Shift+B");
    }

    [Fact]
    public void Swapping_two_default_chords_survives_normalization_and_application()
    {
        Dictionary<string, string> raw = new()
        {
            [AppShortcutMap.SubsPositionUp] = "Shift+Down",
            [AppShortcutMap.SubsPositionDown] = "Shift+Up",
        };
        AppShortcutMap.TryNormalizeOverrides(raw, out Dictionary<string, string> normalized).Should().BeTrue();
        normalized[AppShortcutMap.SubsPositionUp].Should().Be("Shift+Down");
        normalized[AppShortcutMap.SubsPositionDown].Should().Be("Shift+Up");

        AppShortcutMap map = new(AppActions([]), normalized);
        map.Find(WpfKey.Down, false, false, true)!.ActionName.Should().Be(AppShortcutMap.SubsPositionUp);
        map.Find(WpfKey.Up, false, false, true)!.ActionName.Should().Be(AppShortcutMap.SubsPositionDown);
    }

    [AvaloniaFact]
    public void Swapped_shortcuts_are_applied_live_and_persisted_consistently()
    {
        FakePlaybackController player = new() { IsOpened = true };
        AppPrefs prefs = new AppPrefs().Normalize();
        List<AppPrefs> saved = [];
        using MainWindowViewModel vm = new(player, prefs, saved.Add, new FakeTranslator(), new FakeShell());
        vm.ApplyAppShortcuts(new Dictionary<string, string>
        {
            [AppShortcutMap.SubsPositionUp] = "Shift+Down",
            [AppShortcutMap.SubsPositionDown] = "Shift+Up",
        });
        vm.Shortcuts.Find(WpfKey.Down, false, false, true)!.ActionName.Should().Be(AppShortcutMap.SubsPositionUp);
        vm.Shortcuts.Find(WpfKey.Up, false, false, true)!.ActionName.Should().Be(AppShortcutMap.SubsPositionDown);
        saved[^1].AppShortcutOverrides[AppShortcutMap.SubsPositionUp].Should().Be("Shift+Down");
        saved[^1].AppShortcutOverrides[AppShortcutMap.SubsPositionDown].Should().Be("Shift+Up");
    }

    [Fact]
    public void Closing_the_settings_draft_discards_shortcut_edits()
    {
        AppPrefs prefs = new AppPrefs().Normalize();
        int commits = 0;
        SettingsViewModel draft = new(prefs, _ => commits++, new BitmapSubtitleRuntimeState(1, 1, 100));
        draft.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.ToggleSidebar).Chord = "Ctrl+Shift+B";
        draft.CloseCommand.Execute(null);
        commits.Should().Be(0);
        draft.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.ToggleSidebar).Chord.Should().Be("Ctrl+Shift+B",
            "Close discards without applying, the draft object is simply abandoned");
    }

    [AvaloniaFact]
    public void Settings_save_without_edits_keeps_the_two_cheat_sheet_chords()
    {
        FakePlaybackController player = new() { IsOpened = true };
        AppPrefs prefs = new AppPrefs().Normalize();
        List<AppPrefs> saved = [];
        using MainWindowViewModel vm = new(player, prefs, saved.Add, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = vm.CreateSettings();
        draft.SaveCommand.Execute(null);
        draft.Error.Should().BeNull();
        saved.Should().ContainSingle();
        saved[0].AppShortcutOverrides.Should().BeEmpty("untouched rows must not materialise overrides");
        vm.Shortcuts.Shortcuts.Count(s => s.ActionName == AppShortcutMap.OpenWindowCheatSheet)
            .Should().Be(2, "F1 and Shift+OemQuestion must survive a plain Settings save");
    }

    [Fact]
    public void Reset_removes_the_override_and_restores_all_default_chords()
    {
        AppPrefs prefs = new AppPrefs().Normalize();
        SettingsViewModel draft = new(prefs, _ => { }, new BitmapSubtitleRuntimeState(1, 1, 100));
        ShortcutEditRow row = draft.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.OpenWindowCheatSheet);
        row.Chord = "Ctrl+Shift+F1";
        draft.SaveCommand.Execute(null);
        draft.AppShortcuts[AppShortcutMap.OpenWindowCheatSheet].Should().Be("Ctrl+Shift+F1");

        row.ResetCommand.Execute(null);
        row.Chord.Should().Be(row.DefaultChord);
        draft.SaveCommand.Execute(null);
        draft.AppShortcuts.Should().NotContainKey(AppShortcutMap.OpenWindowCheatSheet);
        AppShortcutMap map = new(AppActions([]), draft.AppShortcuts);
        map.Shortcuts.Count(s => s.ActionName == AppShortcutMap.OpenWindowCheatSheet).Should().Be(2);
    }

    [AvaloniaFact]
    public void Applying_overrides_rebuilds_the_live_map_and_persists()
    {
        FakePlaybackController player = new() { IsOpened = true };
        AppPrefs prefs = new AppPrefs().Normalize();
        List<AppPrefs> saved = [];
        using MainWindowViewModel vm = new(player, prefs, saved.Add, new FakeTranslator(), new FakeShell());
        vm.Shortcuts.Find(WpfKey.B, true, false, false).Should().NotBeNull();

        vm.ApplyAppShortcuts(new Dictionary<string, string> { [AppShortcutMap.ToggleSidebar] = "Ctrl+Shift+B" });

        vm.Shortcuts.Find(WpfKey.B, true, false, false).Should().BeNull();
        vm.Shortcuts.Find(WpfKey.B, true, false, true).Should().NotBeNull();
        saved.Should().ContainSingle("the public apply must persist exactly once");
        saved[0].AppShortcutOverrides[AppShortcutMap.ToggleSidebar].Should().Be("Ctrl+Shift+B");
    }

    [AvaloniaFact]
    public void Restart_restores_persisted_overrides()
    {
        FakePlaybackController player = new() { IsOpened = true };
        AppPrefs prefs = new AppPrefs().Normalize();
        prefs.AppShortcutOverrides[AppShortcutMap.ToggleSidebar] = "Ctrl+Shift+B";
        using MainWindowViewModel vm = new(player, prefs, _ => { }, new FakeTranslator(), new FakeShell());
        vm.Shortcuts.Find(WpfKey.B, true, false, true).Should().NotBeNull();
        vm.Shortcuts.Find(WpfKey.B, true, false, false).Should().BeNull();
    }

    [AvaloniaFact]
    public void Capture_control_turns_the_next_chord_into_the_stored_value()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new Window { Width = 320, Height = 120 };
        ChordCaptureButton button = new() { Chord = "Ctrl+B" };
        Button other = new() { Content = "other" };
        window.Content = new StackPanel { Children = { button, other } };
        window.Show();
        try
        {
            ChordCaptureButton.FromKey(AvKey.OemQuestion, KeyModifiers.Shift).Should().Be("Shift+OemQuestion");
            ChordCaptureButton.FromKey(AvKey.B, KeyModifiers.Control).Should().Be("Ctrl+B");
            ChordCaptureButton.FromKey(AvKey.LeftShift, KeyModifiers.Shift).Should().BeNull("modifier-only keys are ignored");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.IsCapturing.Should().BeTrue("a click starts capture");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.IsCapturing.Should().BeFalse("a second click cancels capture");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = button,
                Key = AvKey.B,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
            });
            button.Chord.Should().Be("Ctrl+Shift+B");
            button.IsCapturing.Should().BeFalse();

            button.BeginCapture();
            button.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = button,
                Key = AvKey.Escape,
            });
            button.IsCapturing.Should().BeFalse("Esc cancels capture");
            button.Chord.Should().Be("Ctrl+Shift+B");

            button.BeginCapture();
            other.Focus();
            Dispatcher.UIThread.RunJobs();
            button.IsCapturing.Should().BeFalse("losing focus cancels capture");
            button.Chord.Should().Be("Ctrl+Shift+B");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void Settings_window_shows_the_app_shortcut_rows()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        SettingsViewModel model = new(new AppPrefs().Normalize(), _ => { }, new BitmapSubtitleRuntimeState(1, 1, 100));
        var window = new SettingsWindow(model);
        window.Show();
        try
        {
            model.SelectedSection = 2;
            Dispatcher.UIThread.RunJobs();
            ItemsControl rows = window.FindControl<ItemsControl>("ShortcutRows")!;
            int actions = AppShortcutMap.Defaults.Select(d => d.ActionName).Distinct().Count();
            rows.ItemCount.Should().Be(actions);
            ChordCaptureButton[] captures = [.. rows.GetVisualDescendants().OfType<ChordCaptureButton>()];
            captures.Should().HaveCount(actions);
            captures[0].Chord.Should().Be("Shift+Up");
            rows.GetVisualDescendants().OfType<Button>().Count(b => (b.Content as string) == "Reset").Should().Be(actions);

            // The template's TwoWay binding must carry the captured chord into the draft row and its Save.
            ShortcutEditRow sidebarRow = model.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.SubsPositionUp);
            ChordCaptureButton first = captures[model.ShortcutRows.IndexOf(sidebarRow)];
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            first.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = first,
                Key = AvKey.B,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
            });
            Dispatcher.UIThread.RunJobs();
            sidebarRow.Chord.Should().Be("Ctrl+Shift+B");
            captures[model.ShortcutRows.IndexOf(sidebarRow)].Chord.Should().Be("Ctrl+Shift+B");

            int commits = 0;
            SettingsViewModel saveModel = new(new AppPrefs().Normalize(), _ => commits++,
                new BitmapSubtitleRuntimeState(1, 1, 100));
            saveModel.ShortcutRows.Single(r => r.ActionName == AppShortcutMap.SubsPositionUp).Chord = "Ctrl+Shift+B";
            saveModel.SaveCommand.Execute(null);
            saveModel.Error.Should().BeNull();
            commits.Should().Be(1);
            saveModel.AppShortcuts[AppShortcutMap.SubsPositionUp].Should().Be("Ctrl+Shift+B");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    static IReadOnlyDictionary<string, Action> AppActions(HashSet<string> executed) => new Dictionary<string, Action>
    {
        [AppShortcutMap.ToggleSidebar] = () => executed.Add(AppShortcutMap.ToggleSidebar),
        [AppShortcutMap.ActivateSubsSearch] = () => executed.Add(AppShortcutMap.ActivateSubsSearch),
        [AppShortcutMap.OpenWindowCheatSheet] = () => executed.Add(AppShortcutMap.OpenWindowCheatSheet),
        [AppShortcutMap.SubsSizeIncrease] = () => executed.Add(AppShortcutMap.SubsSizeIncrease),
        [AppShortcutMap.SubsSizeDecrease] = () => executed.Add(AppShortcutMap.SubsSizeDecrease),
        [AppShortcutMap.SubsPositionUp] = () => executed.Add(AppShortcutMap.SubsPositionUp),
        [AppShortcutMap.SubsPositionDown] = () => executed.Add(AppShortcutMap.SubsPositionDown),
        [AppShortcutMap.SubsPrimaryTextCopy] = () => executed.Add(AppShortcutMap.SubsPrimaryTextCopy),
        [AppShortcutMap.OpenSubtitles] = () => executed.Add(AppShortcutMap.OpenSubtitles),
        [AppShortcutMap.ToggleTheme] = () => executed.Add(AppShortcutMap.ToggleTheme),
    };
}
