using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class OpenSubtitlesDownloadWindow : Window
{
    bool draining, drained;
    public OpenSubtitlesDownloadWindow() { InitializeComponent(); }
    public OpenSubtitlesDownloadWindow(OpenSubtitlesDownloadViewModel model) : this()
    {
        DataContext = model;
        model.PickSavePath = PickPathAsync;
        model.ShowKnownError = message => new ErrorWindow("Subtitle download", message, "").ShowDialog(this);
        Closing += async (_, e) =>
        {
            if (drained) return;
            e.Cancel = true;
            if (draining) return;
            draining = true;
            await model.DisposeAsync();
            drained = true; Close();
        };
        Opened += (_, _) => SubtitleQuery.Focus();
    }
    async Task<string?> PickPathAsync(string name)
    {
        if (!StorageProvider.CanSave) throw new IOException("A save dialog is not available on this desktop.");
        string extension = Path.GetExtension(name);
        using IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save downloaded subtitle", SuggestedFileName = name, DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("Subtitle file") { Patterns = ["*" + extension] }],
        });
        return file == null ? null : file.TryGetLocalPath() ?? throw new IOException("Choose a local subtitle file.");
    }
    /// <summary>App shutdown drains provider/picker/load ownership before disposing the native player.</summary>
    public Task DrainAsync() => DataContext is OpenSubtitlesDownloadViewModel model ? model.DisposeAsync().AsTask() : Task.CompletedTask;
    void OnClose(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
