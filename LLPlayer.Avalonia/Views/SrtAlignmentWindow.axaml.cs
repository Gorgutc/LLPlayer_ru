using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class SrtAlignmentWindow : Window
{
    bool closing, drained;

    public SrtAlignmentWindow() => InitializeComponent();

    public SrtAlignmentWindow(SrtAlignmentViewModel model) : this()
    {
        DataContext = model;
        model.PickOutputPath = PickPathAsync;
        Closing += async (_, e) =>
        {
            if (drained || (e.CloseReason == WindowCloseReason.OwnerWindowClosing && Owner is MainWindow { ShutdownRequested: not null }))
                return;
            e.Cancel = true;
            if (closing) return;
            closing = true;
            try
            {
                if (await DrainAsync())
                {
                    // The initial Closing handler has to return before another Close can be processed.
                    await Task.Yield();
                    if (IsVisible) Close();
                }
            }
            finally { closing = false; }
        };
    }

    internal async Task DrainForShutdownAsync()
    {
        if (!await DrainAsync())
            throw new InvalidOperationException("Subtitle alignment is still stopping.");
    }

    async Task<bool> DrainAsync()
    {
        if (drained) return true;
        drained = DataContext is not SrtAlignmentViewModel model || await model.TryCloseAsync();
        return drained;
    }

    async Task<string?> PickPathAsync(string name, string? directory)
    {
        if (!StorageProvider.CanSave)
            throw new IOException("A save dialog is not available on this desktop.");

        using IStorageFolder? start = directory == null ? null : await StorageProvider.TryGetFolderFromPathAsync(directory);
        using IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save aligned subtitles",
            SuggestedFileName = name,
            SuggestedStartLocation = start,
            DefaultExtension = "srt",
            ShowOverwritePrompt = false,
            FileTypeChoices = [new FilePickerFileType("SubRip subtitles") { Patterns = ["*.srt"] }]
        });
        return file == null ? null : file.TryGetLocalPath() ?? throw new IOException("Choose a local file.");
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
