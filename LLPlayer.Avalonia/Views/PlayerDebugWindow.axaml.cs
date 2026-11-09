using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class PlayerDebugWindow : Window
{
    public PlayerDebugWindow() : this(new PlayerDebugViewModel(new("", "", "", "", "", false, "", "", "", "", ""))) { }
    public PlayerDebugWindow(PlayerDebugViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerDebugViewModel model && Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(model.CopyText);
    }
    void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
