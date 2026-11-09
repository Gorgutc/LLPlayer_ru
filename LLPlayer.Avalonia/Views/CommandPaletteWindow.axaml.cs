using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class CommandPaletteWindow : Window
{
    public CommandPaletteWindow() : this(new CommandPaletteViewModel([])) { }
    public CommandPaletteWindow(CommandPaletteViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Opened += (_, _) => SearchBox.Focus();
        Closed += (_, _) => model.Cancel(() => { });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel model && e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Key == Key.Escape)
            {
                model.Cancel(Close);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter)
            {
                model.TryActivateSelected(Close);
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Up or Key.Down && model.Rows.Count > 0)
            {
                int current = model.SelectedCommand == null ? -1 : model.Rows.IndexOf(model.SelectedCommand);
                model.SelectedCommand = model.Rows[Math.Clamp(current + (e.Key == Key.Down ? 1 : -1), 0, model.Rows.Count - 1)];
                Results.ScrollIntoView(model.SelectedCommand);
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }

    void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel model || e.Source is not Visual source) return;
        ListBoxItem? item = source.FindAncestorOfType<ListBoxItem>(true);
        if (item?.DataContext is not Services.ExecutableCommand selected) return;
        model.SelectedCommand = selected;
        model.TryActivateSelected(Close);
        e.Handled = true;
    }

    void OnRunClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel model) model.TryActivateSelected(Close);
    }

    void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel model) model.Cancel(Close);
        else Close();
    }
}
