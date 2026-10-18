using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class WordManagerWindow : Window
{
    bool drained;
    public WordManagerWindow() { InitializeComponent(); }
    public WordManagerWindow(WordManagerViewModel model) : this()
    {
        DataContext = model;
        model.PickExportPath = async format =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export words", SuggestedFileName = "LLPlayer." + format,
                DefaultExtension = format, FileTypeChoices = [new FilePickerFileType(format.ToUpperInvariant()) { Patterns = ["*." + format] }]
            });
            return file?.TryGetLocalPath();
        };
        Closing += async (_, e) =>
        {
            if (drained) return;
            e.Cancel = true;
            await model.DisposeAsync(); drained = true; Close();
        };
    }
    void OnClose(object? sender, RoutedEventArgs e) => Close();
}
