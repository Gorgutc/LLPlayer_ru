using Avalonia.Headless.XUnit;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public class HotkeyAppRowResetTests
{
    [AvaloniaFact]
    public void Resetting_the_last_cloned_action_is_draft_only_and_can_be_discarded_or_saved()
    {
        using TempDir temp = new();
        PrefsStore store = new(Path.Combine(temp.Path, "prefs.json"));
        AppPrefs prefs = new AppPrefs().Normalize();
        store.Save(prefs);
        int saves = 0;
        using MainWindowViewModel main = new(new FakePlaybackController(), prefs, candidate =>
        {
            store.Save(candidate);
            saves++;
        }, new FakeTranslator(), new FakeShell());
        AppShortcutMap originalMap = main.Shortcuts;
        string originalDisk = File.ReadAllText(store.Path);

        SettingsViewModel discarded = main.CreateSettings();
        ShortcutEditRow clone = CloneLastTheme(discarded);
        Exception? failure = Record.Exception(() => clone.ResetCommand.Execute(null));
        Assert.Null(failure);
        Assert.Equal(11, discarded.ShortcutRows.Count);
        Assert.Equal("ToggleTheme", discarded.ShortcutRows[^1].ActionName);
        Assert.Equal("Ctrl+Shift+T", discarded.ShortcutRows[^1].Chord);
        Assert.Equal(0, saves);
        Assert.Equal(originalDisk, File.ReadAllText(store.Path));
        Assert.Same(originalMap, main.Shortcuts);
        discarded.CloseCommand.Execute(null);
        Assert.Equal(0, saves);
        Assert.Null(prefs.AppKeyBindings);

        SettingsViewModel committed = main.CreateSettings();
        CloneLastTheme(committed).ResetCommand.Execute(null);
        committed.SaveCommand.Execute(null);
        Assert.Null(committed.Error);
        Assert.Equal(1, saves);
        AppPrefs reloaded = store.Load();
        Assert.Null(reloaded.AppKeyBindings);
        SettingsViewModel afterReload = new(reloaded, _ => { }, new(1, 1, 100));
        Assert.Equal(new[]
        {
            "SubsPositionUp", "SubsPositionDown", "SubsSizeIncrease", "SubsSizeDecrease", "SubsPrimaryTextCopy",
            "ActivateSubsSearch", "ToggleSidebar", "OpenWindowCheatSheet", "OpenWindowCheatSheet", "OpenSubtitles", "ToggleTheme",
        }, afterReload.ShortcutRows.Select(row => row.ActionName));
        AssertDualCheatSheetDefaults(afterReload);
        Assert.NotNull(main.Shortcuts.Find(WpfKey.T, true, false, true));
        Assert.Null(main.Shortcuts.Find(WpfKey.F6, false, false, false));
    }

    [AvaloniaFact]
    public void Resetting_noncontiguous_multi_default_rows_uses_the_first_action_anchor_and_preserves_other_order()
    {
        using TempDir temp = new();
        PrefsStore store = new(Path.Combine(temp.Path, "prefs.json"));
        AppPrefs prefs = new AppPrefs().Normalize();
        store.Save(prefs);
        int saves = 0;
        using MainWindowViewModel main = new(new FakePlaybackController(), prefs, candidate =>
        {
            store.Save(candidate);
            saves++;
        }, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        ShortcutEditRow first = draft.ShortcutRows.First(row => row.ActionName == AppShortcutMap.OpenWindowCheatSheet);
        ShortcutEditRow alternate = draft.ShortcutRows.Last(row => row.ActionName == AppShortcutMap.OpenWindowCheatSheet);
        first.Chord = "F6";
        first.CloneCommand.Execute(null);
        ShortcutEditRow clone = draft.ShortcutRows[draft.ShortcutRows.IndexOf(first) + 1];
        clone.Chord = "F7";
        alternate.Chord = "F8";
        draft.ShortcutRows.Move(draft.ShortcutRows.IndexOf(clone), 0);
        draft.ShortcutRows.Move(draft.ShortcutRows.IndexOf(alternate), draft.ShortcutRows.Count - 1);
        ShortcutEditRow[] untouched = [.. draft.ShortcutRows.Where(row => row.ActionName != AppShortcutMap.OpenWindowCheatSheet)];
        string originalDisk = File.ReadAllText(store.Path);
        AppShortcutMap originalMap = main.Shortcuts;

        Exception? failure = Record.Exception(() => alternate.ResetCommand.Execute(null));
        Assert.Null(failure);
        AssertDualCheatSheetDefaults(draft);
        Assert.Equal(new[] { "OpenWindowCheatSheet", "OpenWindowCheatSheet" }, draft.ShortcutRows.Take(2).Select(row => row.ActionName));
        Assert.Equal(untouched, draft.ShortcutRows.Skip(2));
        Assert.Equal(0, saves);
        Assert.Equal(originalDisk, File.ReadAllText(store.Path));
        Assert.Same(originalMap, main.Shortcuts);

        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        Assert.Equal(1, saves);
        AppPrefs reloaded = store.Load();
        Assert.NotNull(reloaded.AppKeyBindings);
        Assert.Equal(new[] { "OpenWindowCheatSheet", "OpenWindowCheatSheet" }, reloaded.AppKeyBindings.Take(2).Select(row => row.ActionName));
        Assert.Equal(untouched.Select(row => row.ActionName), reloaded.AppKeyBindings.Skip(2).Select(row => row.ActionName));
        SettingsViewModel afterReload = new(reloaded, _ => { }, new(1, 1, 100));
        AssertDualCheatSheetDefaults(afterReload);
        Assert.Equal(draft.ShortcutRows.Select(row => row.ActionName), afterReload.ShortcutRows.Select(row => row.ActionName));
        Assert.NotNull(main.Shortcuts.Find(WpfKey.F1, false, false, false));
        Assert.NotNull(main.Shortcuts.Find(WpfKey.OemQuestion, false, false, true));
        foreach (WpfKey retired in new[] { WpfKey.F6, WpfKey.F7, WpfKey.F8 })
            Assert.Null(main.Shortcuts.Find(retired, false, false, false));
    }

    static ShortcutEditRow CloneLastTheme(SettingsViewModel draft)
    {
        ShortcutEditRow theme = draft.ShortcutRows[^1];
        Assert.Equal("ToggleTheme", theme.ActionName);
        theme.CloneCommand.Execute(null);
        ShortcutEditRow clone = draft.ShortcutRows[^1];
        Assert.NotSame(theme, clone);
        clone.Chord = "F6";
        return clone;
    }

    static void AssertDualCheatSheetDefaults(SettingsViewModel draft)
    {
        ShortcutEditRow[] rows = [.. draft.ShortcutRows.Where(row => row.ActionName == AppShortcutMap.OpenWindowCheatSheet)];
        Assert.Equal(new[] { "F1", "Shift+OemQuestion" }, rows.Select(row => row.Chord));
        Assert.All(rows, row => { Assert.True(row.IsEnabled); Assert.False(row.IsKeyUp); });
    }
}
