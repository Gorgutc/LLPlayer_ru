using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class CheatSheetWindow : Window
{
    public CheatSheetWindow() : this(new CheatSheetViewModel([]))
    {
    }

    public CheatSheetWindow(CheatSheetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Opened += (_, _) => SearchBox.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
