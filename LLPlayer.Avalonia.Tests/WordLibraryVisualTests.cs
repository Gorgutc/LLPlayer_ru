using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;
using FlyleafLib;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation;

namespace LLPlayer.Avalonia.Tests;

public class WordLibraryVisualTests
{
    [AvaloniaFact]
    public async Task Manager_and_dictionary_popup_render_in_dark_light_and_small_windows()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        string? artifacts = Environment.GetEnvironmentVariable("LLPLAYER_WORD_ARTIFACTS");
        try
        {
            var library = new WordLibrary(Path.Combine(directory, "words.json"));
            library.TryAdd(new("Serendipity", "/ˌserənˈdɪpəti/", "Счастливая случайность", "An unexpected pleasant discovery.", "The discovery was pure serendipity.", "en", "ru", WordSource.WordClick, "2026-10-03"));
            await using var manager = new WordManagerViewModel(library); manager.Selected = manager.Words[0];
            var managerWindow = new WordManagerWindow(manager); managerWindow.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(managerWindow.IsVisible); Assert.Equal("Serendipity", manager.Term);
            TextBox editor = managerWindow.FindControl<TextBox>("ReadingEditor")!;
            editor.Focus(); editor.CaretIndex = 4; Dispatcher.UIThread.RunJobs();
            managerWindow.KeyTextInput("x"); Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsFocused); Assert.Equal(5, editor.CaretIndex);
            Assert.NotNull(manager.Selected); Assert.Equal("Serendipity", manager.Selected.Term);
            Assert.Equal(editor.Text, Assert.Single(library.GetAll()).Reading);
            // Another immediate edit must preserve the same editor and continue at its existing caret.
            managerWindow.KeyTextInput("y"); Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsFocused); Assert.Equal(6, editor.CaretIndex);
            Assert.Equal(editor.Text, manager.Reading);
            foreach (string theme in new[] { "Dark", "Light" })
            {
                ThemeService.Apply(theme); Dispatcher.UIThread.RunJobs(); Capture(managerWindow, "words-manager-" + theme.ToLowerInvariant());
            }
            managerWindow.Width = 460; managerWindow.Height = 420; Dispatcher.UIThread.RunJobs(); Capture(managerWindow, "words-manager-small");
            Assert.True(managerWindow.Bounds.Width <= 460);
            managerWindow.Close(); await manager.DisposeAsync(); Dispatcher.UIThread.RunJobs();

            await using var translator = new FlyleafWordTranslator(_ => Language.English,
                new() { TargetLanguage = TargetLanguage.Russian, WordProvider = "Off", DictionaryMode = WordDefinitionServiceType.Auto },
                (_, _) => throw new Exception("No translation provider"),
                _ => new WordDefinitionService((_, _) => Task.FromResult(new DictionaryEntry("serendipity", "/ˌserənˈdɪpəti/", [new("noun", string.Join(" ", Enumerable.Repeat("An unexpected pleasant discovery.", 18)), "pure serendipity")])), null));
            var window = new MainWindow { Width = 1000, Height = 650 };
            var vm = new MainWindowViewModel(new FakePlaybackController(), new AppPrefs().Normalize(), _ => { }, translator, window);
            vm.WordPopup.Library = library; window.Attach(vm, null); window.Show();
            await vm.WordPopup.OpenAsync("serendipity", "The discovery was pure serendipity.", 0, 400, 350);
            Assert.Contains("discovery", vm.WordPopup.Definition); Assert.True(vm.WordPopup.CanSave);
            foreach (string theme in new[] { "Dark", "Light" })
            {
                ThemeService.Apply(theme); Dispatcher.UIThread.RunJobs(); Capture(window, "words-popup-" + theme.ToLowerInvariant());
            }
            window.Width = 640; window.Height = 420; Dispatcher.UIThread.RunJobs();
            Capture(window, "words-popup-small");
            var card = window.FindControl<Border>("WordPopupCard")!;
            var video = window.FindControl<Panel>("VideoHost")!;
            Assert.True(card.Bounds.Height <= video.Bounds.Height - 16 + 1);
            Assert.True(Canvas.GetTop(card) >= 8);
            Assert.True(Canvas.GetTop(card) + card.Bounds.Height <= video.Bounds.Height - 8 + 1);
            var popupScroll = window.FindControl<ScrollViewer>("WordPopupScroll")!;
            popupScroll.Offset = new Vector(0, 1000); Dispatcher.UIThread.RunJobs();
            var save = card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Save word"));
            Assert.True(save.IsEffectivelyVisible); Assert.True(save.IsEnabled);
            Point saveTop = save.TranslatePoint(default, card)!.Value;
            Assert.True(saveTop.Y >= 0 && saveTop.Y + save.Bounds.Height <= card.Bounds.Height);
            Capture(window, "words-popup-small-save");
            window.Close(); vm.Dispose(); await vm.WordPopup.DisposeAsync();
        }
        finally { Directory.Delete(directory, true); }
        void Capture(Window window, string name)
        {
            if (artifacts == null) return;
            Directory.CreateDirectory(artifacts);
            window.CaptureRenderedFrame()!.Save(Path.Combine(artifacts, name + ".png"), PngBitmapEncoderOptions.Default);
        }
    }
}
