using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FlyleafLib;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class AiInsightsWindow : Window
{
    bool drained, draining;
    public AiInsightsWindow() { InitializeComponent(); }
    public AiInsightsWindow(AiInsightsViewModel model) : this()
    {
        DataContext = model;
        model.CopyTextAsync = async text =>
        {
            var clipboard = Clipboard ?? throw new IOException("Clipboard is unavailable.");
            await clipboard.SetTextAsync(text);
        };
        model.SaveTextAsync = async text =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save AI insights", SuggestedFileName = "transcript-insights.txt", DefaultExtension = "txt",
                FileTypeChoices = [new("Text file") { Patterns = ["*.txt"] }, new("TSV") { Patterns = ["*.tsv"] }, FilePickerFileTypes.All]
            });
            if (file == null || model.IsClosing) return null;
            string path = file.TryGetLocalPath() ?? throw new IOException("Choose a local file.");
            // Same UTF-8 without BOM as WPF; staging preserves an existing file if writing fails.
            AtomicFile.WriteAllText(path, text);
            return path;
        };
        Closing += async (_, e) =>
        {
            if (drained) return;
            if (draining) { e.Cancel = true; return; }
            draining = true;
            ValueTask disposal = model.DisposeAsync();
            if (disposal.IsCompletedSuccessfully)
            {
                disposal.GetAwaiter().GetResult();
                drained = true;
                return;
            }
            e.Cancel = true;
            await disposal; drained = true; Close();
        };
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }
    void OnClose(object? sender, RoutedEventArgs e) => Close();
}
