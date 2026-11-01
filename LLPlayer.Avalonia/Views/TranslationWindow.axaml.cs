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
        model.Saved += Close;
        Closed += (_, _) => model.Saved -= Close;
    }
    bool drainStarted;
    bool drained;
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || drained || DataContext is not TranslationViewModel model) return;
        if (model.IsSaving) { e.Cancel = true; return; }
        if (!model.DiagnosticRunning) { _ = model.DrainDiagnosticsAsync(); return; }
        e.Cancel = true;
        if (!drainStarted) { drainStarted = true; IsEnabled = false; _ = DrainAndCloseAsync(model); }
    }
    async Task DrainAndCloseAsync(TranslationViewModel model)
    {
        await model.DrainDiagnosticsAsync();
        drained = true;
        Close();
    }
    void OnClose(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
