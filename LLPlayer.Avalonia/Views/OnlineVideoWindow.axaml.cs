using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class OnlineVideoWindow : Window
{
    public OnlineVideoWindow() { InitializeComponent(); }
    public OnlineVideoWindow(OnlineVideoViewModel model) : this()
    {
        DataContext = model;
        Closing += (_, e) => e.Cancel = model.Busy;
        model.Opened += Close;
        Closed += (_, _) => model.Opened -= Close;
        Opened += (_, _) => OnlineUrlBox.Focus();
    }
    void OnClose(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
