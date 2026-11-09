using Avalonia.Headless.XUnit;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public class SettingsKeyCompatibilityTests
{
    [AvaloniaTheory]
    [InlineData(WpfKey.K, false, AppShortcutMap.OpenCommandPalette)]
    [InlineData(WpfKey.D, true, AppShortcutMap.ToggleDebugOverlay)]
    public void Legacy_engine_chord_survives_opening_settings_and_an_unrelated_save(
        WpfKey key, bool shift, string additiveAction)
    {
        AppPrefs prefs = LegacyPrefs(key, shift);
        FakePlaybackController player = new();
        List<AppPrefs> saved = [];
        using MainWindowViewModel main = new(player, prefs, saved.Add, new FakeTranslator(), new FakeShell());
        Assert.Null(main.Shortcuts.Find(key, true, false, shift));

        SettingsViewModel draft = main.CreateSettings();
        draft.AlwaysOnTop = true;
        draft.SaveCommand.Execute(null);

        Assert.Null(draft.Error);
        AppPrefs committed = Assert.Single(saved);
        Assert.True(committed.AlwaysOnTop);
        Assert.Null(committed.AppKeyBindings);
        Assert.Empty(committed.AppShortcutOverrides);
        Assert.Null(main.Shortcuts.Find(key, true, false, shift));
        Assert.DoesNotContain(draft.ShortcutRows, row => row.ActionName == additiveAction);
        EngineKeyBindingInfo engine = Assert.Single(player.CurrentEngineKeyBindings);
        Assert.Equal(KeyBindingAction.TogglePlayPause, engine.Action);
        Assert.Equal(key, engine.Key);
        Assert.True(engine.Ctrl);
        Assert.Equal(shift, engine.Shift);
        Assert.True(engine.IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData(WpfKey.K, false)]
    [InlineData(WpfKey.D, true)]
    public void Editing_another_app_chord_preserves_the_legacy_engine_owner(WpfKey key, bool shift)
    {
        AppPrefs prefs = LegacyPrefs(key, shift);
        FakePlaybackController player = new();
        List<AppPrefs> saved = [];
        using MainWindowViewModel main = new(player, prefs, saved.Add, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        draft.ShortcutRows.Single(row => row.ActionName == AppShortcutMap.ToggleSidebar).Chord = "Ctrl+Alt+B";

        draft.SaveCommand.Execute(null);

        Assert.Null(draft.Error);
        Assert.Single(saved);
        Assert.Null(main.Shortcuts.Find(key, true, false, shift));
        Assert.Equal(AppShortcutMap.ToggleSidebar, main.Shortcuts.Find(WpfKey.B, true, true, false)?.ActionName);
        Assert.Equal(key, Assert.Single(player.CurrentEngineKeyBindings).Key);
    }

    [AvaloniaTheory]
    [InlineData(WpfKey.K, false)]
    [InlineData(WpfKey.D, true)]
    public void Explicit_reset_still_blocks_a_collision_with_a_legacy_engine_chord(WpfKey key, bool shift)
    {
        AppPrefs prefs = LegacyPrefs(key, shift);
        List<AppPrefs> saved = [];
        using MainWindowViewModel main = new(new FakePlaybackController(), prefs, saved.Add, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        draft.ResetAppKeysCommand.Execute(null);

        draft.SaveCommand.Execute(null);

        Assert.NotNull(draft.Error);
        Assert.Contains(AppShortcutMap.FormatChord(key, true, false, shift), draft.Error);
        Assert.Empty(saved);
        Assert.Null(prefs.AppKeyBindings);
        Assert.Null(main.Shortcuts.Find(key, true, false, shift));
    }

    [AvaloniaTheory]
    [InlineData(WpfKey.K, false)]
    [InlineData(WpfKey.D, true)]
    public void Explicit_rebind_still_blocks_a_collision_with_a_legacy_engine_chord(WpfKey key, bool shift)
    {
        AppPrefs prefs = LegacyPrefs(key, shift);
        List<AppPrefs> saved = [];
        using MainWindowViewModel main = new(new FakePlaybackController(), prefs, saved.Add, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        draft.ShortcutRows.Single(row => row.ActionName == AppShortcutMap.ToggleSidebar).Chord =
            AppShortcutMap.FormatChord(key, true, false, shift);

        draft.SaveCommand.Execute(null);

        Assert.NotNull(draft.Error);
        Assert.Contains(AppShortcutMap.FormatChord(key, true, false, shift), draft.Error);
        Assert.Empty(saved);
        Assert.Null(prefs.AppKeyBindings);
        Assert.Null(main.Shortcuts.Find(key, true, false, shift));
    }

    static AppPrefs LegacyPrefs(WpfKey key, bool shift) => new AppPrefs
    {
        EngineKeysCustomized = true,
        EngineKeyBindings = [new EngineKeyBindingPref
        {
            Action = nameof(KeyBindingAction.TogglePlayPause), Key = key.ToString(), Ctrl = true,
            Shift = shift, IsEnabled = true
        }],
        AppKeyBindings = null
    }.Normalize();
}
