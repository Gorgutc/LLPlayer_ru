using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed class ShortcutRowViewModel(ShortcutInfo info, ExecutableCommand? owner = null) : ObservableObject
{
    public string Description => info.Description;
    public string ActionName => info.ActionName;
    public bool CanExecute => owner?.CanExecute ?? false;
    internal ExecutableCommand? Owner => owner;
    internal void RefreshAvailability() => OnPropertyChanged(nameof(CanExecute));

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
    bool completed;

    public CheatSheetViewModel(IReadOnlyList<ShortcutInfo> rows)
    {
        grouped = rows.Where(r => r.Keys.Count > 0).Select(r => (r.Group, new ShortcutRowViewModel(r))).ToList();
        all = grouped.Select(g => g.Row).ToList();
        ApplyFilter();
    }

    public CheatSheetViewModel(IReadOnlyList<ExecutableCommand> rows, AppPrefs prefs) : this(Array.Empty<ShortcutInfo>())
        => Refresh(rows, prefs);

    public IReadOnlyList<MouseHelpRow> MouseRows { get; private set; } = [];
    public void Refresh(IReadOnlyList<ExecutableCommand> rows, AppPrefs prefs)
    {
        grouped.Clear();
        all.Clear();
        foreach (ExecutableCommand command in rows.Where(command => command.Keys.Count > 0))
        {
            ShortcutRowViewModel row = new(new(command.Group, command.ActionName, command.Description, command.Keys), command);
            grouped.Add((command.Group, row));
            all.Add(row);
        }
        MouseRows = MouseHelpCatalog.Build(prefs);
        OnPropertyChanged(nameof(MouseRows));
        OnPropertyChanged(nameof(TotalCount));
        ApplyFilter();
    }

    public bool TryActivate(ShortcutRowViewModel row, Action close)
    {
        if (completed || !all.Contains(row) || row.Owner is not { } owner || !owner.CanExecute) return false;
        completed = true;
        close();
        return owner.TryExecute();
    }

    public void RefreshAvailability()
    {
        foreach (ShortcutRowViewModel row in all)
        {
            row.Owner?.RefreshAvailability();
            row.RefreshAvailability();
        }
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
