using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class CommandPaletteViewModel : ObservableObject
{
    IReadOnlyList<ExecutableCommand> all = [];
    bool completed;

    public CommandPaletteViewModel(IReadOnlyList<ExecutableCommand> commands) => Refresh(commands);

    public ObservableCollection<ExecutableCommand> Rows { get; } = [];
    [ObservableProperty] public partial string FilterText { get; set; } = "";
    [ObservableProperty] public partial ExecutableCommand? SelectedCommand { get; set; }
    public void Refresh(IReadOnlyList<ExecutableCommand> commands)
    {
        string? selectedId = SelectedCommand?.Id;
        all = commands.Where(command => command.Id != "app:" + AppShortcutMap.OpenCommandPalette).ToArray();
        ApplyFilter(selectedId);
    }

    public void RefreshAvailability()
    {
        foreach (ExecutableCommand command in all) command.RefreshAvailability();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    void ApplyFilter(string? selectedId = null)
    {
        Rows.Clear();
        string filter = FilterText.Trim();
        foreach (ExecutableCommand command in all.Where(command => filter.Length == 0
                     || command.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
                     || command.ActionName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                     || command.Keys.Any(chord => chord.Contains(filter, StringComparison.OrdinalIgnoreCase))))
            Rows.Add(command);
        SelectedCommand = Rows.FirstOrDefault(command => command.Id == selectedId) ?? Rows.FirstOrDefault();
    }

    public bool TryActivateSelected(Action close)
    {
        if (completed || SelectedCommand is not { } selected || !Rows.Contains(selected) || !selected.CanExecute)
            return false;
        completed = true;
        close();
        return selected.TryExecute();
    }

    public void Cancel(Action close)
    {
        if (completed) return;
        completed = true;
        close();
    }
}
