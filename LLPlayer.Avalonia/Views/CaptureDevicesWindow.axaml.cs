using Avalonia.Controls;
using Avalonia.Input;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class CaptureDevicesWindow : Window
{
    public CaptureDevicesWindow() { InitializeComponent(); }
    public CaptureDevicesWindow(CaptureDevicesViewModel model) : this()
    {
        DataContext = model;
        model.CloseRequested += Close;
        Closed += (_, _) => model.CloseRequested -= Close;
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is CaptureDevicesViewModel { Busy: true } model)
        { e.Cancel = true; model.RequestClose(); }
        base.OnClosing(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is CaptureDevicesViewModel model)
        { model.RequestClose(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
