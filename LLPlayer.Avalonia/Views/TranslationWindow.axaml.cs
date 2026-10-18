using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class TranslationWindow : Window
{
    public TranslationWindow() { InitializeComponent(); }
    public TranslationWindow(TranslationViewModel model) : this()
    {
        DataContext = model;
        Closing += (_, e) => e.Cancel = model.Busy;
        model.Saved += Close;
        Closed += (_, _) => model.Saved -= Close;
    }
    void OnClose(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
