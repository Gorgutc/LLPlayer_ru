using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class SettingsViewModel
{
    List<AppShortcut> initialAppRows = [];
    Dictionary<string, string> initialAppOverrides = [];
    List<AppKeyBindingPref>? initialExplicitAppRows;
    bool resetAppRows;
    public IReadOnlyList<AppKeyBindingPref>? AppKeyBindingPrefs { get; private set; }

    void InitializeAppKeyRows(AppPrefs current)
    {
        CaptureToolsBaseline(current);
        initialAppOverrides = new(current.AppShortcutOverrides, StringComparer.Ordinal);
        initialExplicitAppRows = AppKeyBindings.NormalizePrefs(current.AppKeyBindings);
        initialAppRows = [.. AppKeyBindings.BuildEffective(initialAppOverrides, initialExplicitAppRows)];
        foreach (AppShortcut row in initialAppRows) ShortcutRows.Add(CreateAppRow(row));
    }

    ShortcutEditRow CreateAppRow(AppShortcut binding)
    {
        AppShortcut? firstDefault = AppShortcutMap.Defaults.FirstOrDefault(row => row.ActionName == binding.ActionName);
        string defaultChord = firstDefault == null ? "" : AppShortcutMap.FormatChord(firstDefault.Key, firstDefault.Ctrl, firstDefault.Alt, firstDefault.Shift);
        return new(binding.ActionName, binding.Description, defaultChord,
            AppShortcutMap.FormatChord(binding.Key, binding.Ctrl, binding.Alt, binding.Shift),
            DeleteAppKeyRow, CloneAppKeyRow, ResetAppKeyRow) { IsEnabled = binding.IsEnabled, IsKeyUp = binding.IsKeyUp };
    }

    [RelayCommand] void AddAppKey() => ShortcutRows.Add(CreateAppRow(
        new(AppShortcutMap.ToggleSidebar, WpfKey.None, false, false, false, "Toggle Subtitles Sidebar")));
    void DeleteAppKeyRow(ShortcutEditRow row) => ShortcutRows.Remove(row);
    void CloneAppKeyRow(ShortcutEditRow row)
    {
        int index = ShortcutRows.IndexOf(row);
        if (index < 0) return;
        ShortcutEditRow copy = new(row.ActionName, row.Description, row.DefaultChord, row.Chord,
            DeleteAppKeyRow, CloneAppKeyRow, ResetAppKeyRow) { IsEnabled = row.IsEnabled, IsKeyUp = row.IsKeyUp };
        ShortcutRows.Insert(index + 1, copy);
    }
    void ResetAppKeyRow(ShortcutEditRow row)
    {
        AppShortcut[] defaults = [.. AppShortcutMap.Defaults.Where(binding => binding.ActionName == row.ActionName)];
        if (defaults.Length == 0) { row.Chord = ""; return; }
        ShortcutEditRow[] matchingRows = [.. ShortcutRows.Where(item => item.ActionName == row.ActionName)];
        if (matchingRows.Length == 0) return;
        int index = ShortcutRows.IndexOf(matchingRows[0]);
        row.Chord = AppShortcutMap.FormatChord(defaults[0].Key, defaults[0].Ctrl, defaults[0].Alt, defaults[0].Shift);
        foreach (ShortcutEditRow existing in matchingRows)
            ShortcutRows.Remove(existing);
        foreach (AppShortcut binding in defaults) ShortcutRows.Insert(index++, CreateAppRow(binding));
    }
    [RelayCommand] void ResetAppKeys()
    {
        ShortcutRows.Clear();
        foreach (AppShortcut row in AppShortcutMap.Defaults) ShortcutRows.Add(CreateAppRow(row));
        resetAppRows = true;
    }

    [RelayCommand] void AddEngineKey()
    {
        EngineKeyRows.Add(new(new(KeyBindingAction.TogglePlayPause, WpfKey.None), DeleteEngineKeyRow, CloneEngineKeyRow));
        OnPropertyChanged(nameof(HasEngineKeyRows));
    }
    void CloneEngineKeyRow(EngineKeyEditRow row)
    {
        int index = EngineKeyRows.IndexOf(row);
        if (index < 0) return;
        // Chord text may be intentionally invalid/empty in a draft; cloning preserves it without coercion.
        EngineKeyEditRow copy = new(new(row.Action, WpfKey.None, IsEnabled: row.IsEnabled, IsKeyUp: row.IsKeyUp),
            DeleteEngineKeyRow, CloneEngineKeyRow) { Chord = row.Chord };
        EngineKeyRows.Insert(index + 1, copy);
        OnPropertyChanged(nameof(HasEngineKeyRows));
    }

    bool TryBuildAppRows(out Dictionary<string, string> overrides, out AppShortcutMap map, out string error)
    {
        overrides = new(initialAppOverrides, StringComparer.Ordinal);
        map = new(new Dictionary<string, Action>());
        error = "";
        List<AppShortcut> bindings = [];
        foreach (ShortcutEditRow row in ShortcutRows)
        {
            if (!ShortcutActionCatalog.TryGetApp(row.ActionName, out var descriptor)
                || !AppShortcutMap.TryParseChord(row.Chord, out WpfKey key, out bool ctrl, out bool alt, out bool shift))
            {
                error = $"Choose an available app action and a key chord such as Ctrl+Shift+B for \"{row.Description}\".";
                return false;
            }
            bindings.Add(new(row.ActionName, key, ctrl, alt, shift, descriptor.Description, row.IsKeyUp, row.IsEnabled));
        }
        bool defaults = bindings.SequenceEqual(AppShortcutMap.Defaults);
        bool unchanged = bindings.SequenceEqual(initialAppRows);
        if (resetAppRows && defaults || !unchanged && defaults)
        {
            overrides.Clear();
            AppKeyBindingPrefs = null;
        }
        else if (unchanged)
            AppKeyBindingPrefs = AppKeyBindings.NormalizePrefs(initialExplicitAppRows);
        else
        {
            AppKeyBindingPrefs = AppKeyBindings.ToPrefs(bindings);
            // Retain the existing compatibility output for older callers; explicit rows own actual routing.
            foreach (var group in bindings.GroupBy(row => row.ActionName))
            {
                AppShortcut? first = AppShortcutMap.Defaults.FirstOrDefault(row => row.ActionName == group.Key);
                if (first == null) continue;
                AppShortcut changed = group.First();
                string chord = AppShortcutMap.FormatChord(changed.Key, changed.Ctrl, changed.Alt, changed.Shift);
                if (chord == AppShortcutMap.FormatChord(first.Key, first.Ctrl, first.Alt, first.Shift)) overrides.Remove(group.Key);
                else overrides[group.Key] = chord;
            }
        }
        map = new(new Dictionary<string, Action>(), overrides, AppKeyBindingPrefs?.ToList());
        return map.TryValidateEffective(out error);
    }
}
