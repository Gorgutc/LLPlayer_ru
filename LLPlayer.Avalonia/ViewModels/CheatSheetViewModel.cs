using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed class ShortcutRowViewModel(ShortcutInfo info)
{
    public string Description => info.Description;
    public string ActionName => info.ActionName;

    /// <summary>Each chord split into key caps (rendered as Kbd badges), e.g. [["Ctrl","B"]].</summary>
    public IReadOnlyList<ChordViewModel> Chords { get; } =
        info.Keys.Select(k => new ChordViewModel(SplitChord(k))).ToList();

    public bool Matches(string filter)
        => string.IsNullOrEmpty(filter)
           || info.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || info.ActionName.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || info.Keys.Any(k => k.Contains(filter, StringComparison.OrdinalIgnoreCase));

    /// <summary>"Ctrl+Shift++" -> ["Ctrl","Shift","+"] (a trailing "+" is the plus key itself).</summary>
    public static string[] SplitChord(string chord)
    {
        List<string> parts = [];
        int start = 0;
        for (int i = 0; i < chord.Length; i++)
        {
            if (chord[i] == '+' && i > start)
            {
                parts.Add(chord[start..i]);
                start = i + 1;
            }
        }
        if (start < chord.Length)
            parts.Add(chord[start..]);
        return [.. parts];
    }
}

public sealed class ChordViewModel(IReadOnlyList<string> keys)
{
    public IReadOnlyList<string> Keys { get; } = keys;
}

public sealed class ShortcutGroupViewModel(string name, IReadOnlyList<ShortcutRowViewModel> rows)
{
    public string Name { get; } = name;
    public IReadOnlyList<ShortcutRowViewModel> Rows { get; } = rows;
}

/// <summary>Keyboard shortcut cheat sheet (F1 / "?"): grouped rows with key caps and a search filter.</summary>
public sealed partial class CheatSheetViewModel : ObservableObject
{
    readonly List<ShortcutRowViewModel> all;
    readonly List<(string Group, ShortcutRowViewModel Row)> grouped;

    public CheatSheetViewModel(IReadOnlyList<ShortcutInfo> rows)
    {
        grouped = rows.Select(r => (r.Group, new ShortcutRowViewModel(r))).ToList();
        all = grouped.Select(g => g.Row).ToList();
        ApplyFilter();
    }

    public ObservableCollection<ShortcutGroupViewModel> Groups { get; } = [];

    public int TotalCount => all.Count;

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    [ObservableProperty]
    public partial string HitText { get; private set; } = "";

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    void ApplyFilter()
    {
        Groups.Clear();
        int hits = 0;
        foreach (var g in grouped.Where(g => g.Row.Matches(FilterText)).GroupBy(g => g.Group))
        {
            List<ShortcutRowViewModel> rows = g.Select(x => x.Row).ToList();
            hits += rows.Count;
            Groups.Add(new ShortcutGroupViewModel(g.Key, rows));
        }
        HitText = string.IsNullOrEmpty(FilterText) ? $"{all.Count} shortcuts" : $"{hits} of {all.Count}";
    }
}
