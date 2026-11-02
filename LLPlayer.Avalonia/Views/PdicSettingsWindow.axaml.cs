using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class PdicSettingsWindow : Window
{
    public PdicSettingsWindow() { InitializeComponent(); }
    public PdicSettingsWindow(PdicSettingsViewModel model) : this()
    {
        DataContext = model;
        model.PickExecutable = PickExecutableAsync;
        model.Saved += Close;
        Closed += (_, _) => { model.Saved -= Close; model.PickExecutable = null; };
    }
    async Task<string?> PickExecutableAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose pdic-pipe.exe", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PDIC bridge") { Patterns = ["*.exe"] }],
        });
        try { return files.Count == 0 ? null : files[0].TryGetLocalPath(); }
        finally { foreach (var file in files) file.Dispose(); }
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is PdicSettingsViewModel { Busy: true }) e.Cancel = true;
        base.OnClosing(e);
    }
    void OnClose(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
