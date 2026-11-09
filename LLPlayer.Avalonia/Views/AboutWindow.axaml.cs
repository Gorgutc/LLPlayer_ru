using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() : this(new AboutViewModel(AboutInformation.FromAssembly(typeof(App).Assembly), _ => { })) { }
    public AboutWindow(AboutViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string link } && DataContext is AboutViewModel model)
            await model.OpenLinkAsync(link);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }
    void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
