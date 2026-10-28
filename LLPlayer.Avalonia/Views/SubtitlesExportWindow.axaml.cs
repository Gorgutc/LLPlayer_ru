using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class SubtitlesExportWindow : Window
{
    bool draining, drained;
    public SubtitlesExportWindow() { InitializeComponent(); }
    public SubtitlesExportWindow(SubtitlesExportViewModel model) : this()
    {
        DataContext = model;
        model.PickExportPath = PickPathAsync;
        model.RevealExport = RevealAsync;
        model.ShowKnownError = message => new ErrorWindow("Subtitle export", message, "").ShowDialog(this);
        Closing += async (_, e) =>
        {
            if (drained) return;
            e.Cancel = true;
            if (draining) return;
            draining = true;
            await model.DisposeAsync();
            drained = true;
            Close();
        };
    }
    async Task<string?> PickPathAsync(SubtitleExportRequest request)
    {
        if (!StorageProvider.CanSave) throw new IOException("A save dialog is not available on this desktop.");
        IStorageFolder? start = null;
        if (request.InitialDirectory != null && Directory.Exists(request.InitialDirectory))
            start = await StorageProvider.TryGetFolderFromPathAsync(request.InitialDirectory);
        using (start)
        {
            string extension = FlyleafLib.MediaPlayer.SubtitleExporter.Extension(request.Format);
            string label = request.Format switch
            {
                FlyleafLib.MediaPlayer.SubtitleExportFormat.Srt => "SubRip subtitle",
                FlyleafLib.MediaPlayer.SubtitleExportFormat.Vtt => "WebVTT subtitle",
                _ => "Plain text"
            };
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export subtitles", SuggestedFileName = request.SuggestedFileName,
                DefaultExtension = extension.TrimStart('.'), SuggestedStartLocation = start,
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(label) { Patterns = ["*" + extension] }, FilePickerFileTypes.All]
            });
            return file == null ? null : file.TryGetLocalPath() ?? throw new IOException("Choose a local file for subtitle export.");
        }
    }
    async Task RevealAsync(string path)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(path));
        using var folder = parent == null ? null : await StorageProvider.TryGetFolderFromPathAsync(parent);
        if (folder == null || !await Launcher.LaunchFileAsync(folder))
            throw new IOException("Open the saved file's folder manually: " + parent);
    }
    void OnClose(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}