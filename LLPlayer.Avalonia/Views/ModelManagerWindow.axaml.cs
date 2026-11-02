using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class ModelManagerWindow : Window
{
    bool draining;
    public ModelManagerWindow() { InitializeComponent(); }
    public ModelManagerWindow(ModelManagerViewModel model) : this()
    {
        DataContext = model;
        Closing += async (_, e) =>
        {
            if (!model.Busy && !draining) return;
            e.Cancel = true;
            if (draining) return;
            draining = true;
            await model.CancelAndDrainAsync();
            draining = false;
            Close();
        };
    }
    void OnClose(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
