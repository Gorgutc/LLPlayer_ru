using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>One explicit portable app binding. Null lists retain legacy overrides; empty lists remove all app keys.</summary>
public sealed class AppKeyBindingPref
{
    public string ActionName { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsKeyUp { get; set; }
}

public static class AppKeyBindings
{
    /// <summary>Deep copies valid rows, preserving order/conflicts/flags. Invalid-only input falls back to legacy.</summary>
    public static List<AppKeyBindingPref>? NormalizePrefs(IEnumerable<AppKeyBindingPref>? raw)
    {
        if (raw == null) return null;
        List<AppKeyBindingPref> result = [];
        bool hadRows = false;
        foreach (AppKeyBindingPref row in raw)
        {
            hadRows = true;
            if (row == null || !ShortcutActionCatalog.TryGetApp(row.ActionName, out _)
                || !Enum.TryParse(row.Key, false, out WpfKey key) || !Enum.IsDefined(key)
                || row.Key != key.ToString() || !AppShortcutMap.TryParseChord(row.Key, out _, out _, out _, out _))
                continue;
            result.Add(new()
            {
                ActionName = row.ActionName, Key = key.ToString(), Ctrl = row.Ctrl, Alt = row.Alt, Shift = row.Shift,
                IsEnabled = row.IsEnabled, IsKeyUp = row.IsKeyUp,
            });
        }
        return hadRows && result.Count == 0 ? null : result;
    }

    public static IReadOnlyList<AppShortcut> BuildEffective(IReadOnlyDictionary<string, string>? legacy,
        IEnumerable<AppKeyBindingPref>? rows)
    {
        List<AppKeyBindingPref>? normalized = NormalizePrefs(rows);
        if (normalized == null) return AppShortcutMap.BuildEffective(legacy);
        return normalized.Select(row => new AppShortcut(row.ActionName, Enum.Parse<WpfKey>(row.Key),
            row.Ctrl, row.Alt, row.Shift, ShortcutActionCatalog.AppActions.First(action => action.Name == row.ActionName).Description,
            row.IsKeyUp, row.IsEnabled)).ToArray();
    }

    public static List<AppKeyBindingPref> ToPrefs(IEnumerable<AppShortcut> rows) => rows.Select(row => new AppKeyBindingPref
    {
        ActionName = row.ActionName, Key = row.Key.ToString(), Ctrl = row.Ctrl, Alt = row.Alt, Shift = row.Shift,
        IsEnabled = row.IsEnabled, IsKeyUp = row.IsKeyUp,
    }).ToList();
}
