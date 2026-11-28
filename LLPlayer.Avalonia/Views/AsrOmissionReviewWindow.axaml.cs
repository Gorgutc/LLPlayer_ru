using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class AsrOmissionReviewWindow : Window
{
    public AsrOmissionReviewWindow() => InitializeComponent();

    public AsrOmissionReviewWindow(AsrOmissionReviewViewModel model) : this() => DataContext = model;

    void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else base.OnKeyDown(e);
    }
}
