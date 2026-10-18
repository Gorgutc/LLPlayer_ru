using Avalonia.Controls;
using Avalonia.Input;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() { InitializeComponent(); }
    public SettingsWindow(SettingsViewModel model) : this()
    {
        DataContext = model;
        model.CloseRequested += Close;
        Closed += (_, _) => model.CloseRequested -= Close;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
