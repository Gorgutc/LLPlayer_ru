using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class PlaylistWindow : Window
{
    public PlaylistWindow() => InitializeComponent();
    public PlaylistWindow(PlaylistViewModel model) : this()
    {
        DataContext = model;
    }
    void OnClose(object? sender, RoutedEventArgs e) => Close();
    async void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PlaylistViewModel model) await model.OpenSelectedCommand.ExecuteAsync(null);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
