using Avalonia.Controls;
using Avalonia.Input;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class PlaybackSettingsWindow : Window
{
    public PlaybackSettingsWindow() { InitializeComponent(); }
    public PlaybackSettingsWindow(PlaybackSettingsViewModel model) : this()
    {
        DataContext = model;
        model.CloseRequested += Close;
        Closed += (_, _) => model.CloseRequested -= Close;
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is PlaybackSettingsViewModel { Busy: true }) e.Cancel = true;
        base.OnClosing(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is PlaybackSettingsViewModel { Busy: false })
        { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
