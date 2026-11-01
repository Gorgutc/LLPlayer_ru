using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class WordActionsPointerTests
{
    const string Cue = "Hello brave new world";

    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Control, KeyModifiers.Control, 1)]
    [InlineData(RawInputModifiers.Control | RawInputModifiers.Shift, KeyModifiers.Control | KeyModifiers.Shift, 0)]
    [InlineData(RawInputModifiers.Control | RawInputModifiers.Alt, KeyModifiers.Control | KeyModifiers.Alt, 0)]
    [InlineData(RawInputModifiers.Meta, KeyModifiers.Meta, 0)]
    [InlineData(RawInputModifiers.None, KeyModifiers.None, 0)]
    public void Real_pointer_release_preserves_exact_modifiers_and_executes_the_selected_word_once(
        RawInputModifiers raw, KeyModifiers expected, int searchCount)
    {
        FakeShell shell = new(); FakeTranslator translator = new();
        var (window, main, _, text) = Open(shell, translator, new AppPrefs { WordCopyOnSelected = true, WordLastSearchOnSelected = true });
        try
        {
            List<WordClickedEventArgs> events = [];
            text.WordClicked += (_, args) => events.Add(args);
            Point hit = FindWord(window, text, "brave");
            window.MouseMove(hit);
            // The release is authoritative: adding a modifier only after the press must still be observed.
            window.MouseDown(hit, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(hit, MouseButton.Left, raw);
            Dispatcher.UIThread.RunJobs();
            WordClickedEventArgs selection = Assert.Single(events);
            Assert.Equal(expected, selection.Modifiers); Assert.Equal(MouseButton.Left, selection.Button); Assert.True(selection.IsWord);
            Assert.Equal("brave", selection.Word); Assert.Equal(Cue, selection.Text);
            Assert.Equal(new[] { "brave" }, shell.Clipboard);
            Assert.Equal(searchCount, shell.BrowserRequests.Count);
            Assert.Single(translator.Calls); Assert.True(main.WordPopup.IsOpen);
        }
        finally { window.Close(); main.Dispose(); }
    }

    [AvaloniaFact]
    public void Right_word_click_opens_its_menu_without_lookup_copy_search_pause_or_drag()
    {
        FakeShell shell = new(); FakeTranslator translator = new();
        var (window, main, player, text) = Open(shell, translator,
            new AppPrefs { WordCopyOnSelected = true, WordLastSearchOnSelected = true });
        try
        {
            player.IsPlaying = true;
            List<WordClickedEventArgs> events = [];
            text.WordClicked += (_, args) => events.Add(args);
            Point hit = FindWord(window, text, "brave");
            window.MouseMove(hit);
            window.MouseDown(hit, MouseButton.Right, RawInputModifiers.RightMouseButton | RawInputModifiers.Control);
            window.MouseUp(hit, MouseButton.Right, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            WordClickedEventArgs selection = Assert.Single(events);
            Assert.Equal(MouseButton.Right, selection.Button); Assert.Equal(KeyModifiers.Control, selection.Modifiers);
            Assert.Equal("brave", selection.Word); Assert.True(selection.IsWord);
            Assert.Empty(translator.Calls); Assert.Empty(shell.Clipboard); Assert.Empty(shell.BrowserRequests);
            Assert.Equal(0, player.PauseCalls); Assert.False(main.WordPopup.IsOpen);
        }
        finally { window.Close(); main.Dispose(); }
    }

    static (MainWindow, MainWindowViewModel, FakePlaybackController, SubtitleText) Open(
        FakeShell shell, FakeTranslator translator, AppPrefs prefs)
    {
        FakePlaybackController player = new();
        MainWindowViewModel main = new(player, prefs.Normalize(), _ => { }, translator, shell);
        MainWindow window = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        window.Attach(main, null); window.Show();
        player.IsOpened = true; player.PrimaryText = Cue;
        Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
        return (window, main, player, window.FindControl<SubtitleText>("PrimarySubtitle")!);
    }

    static Point FindWord(Window window, SubtitleText text, string word)
    {
        for (double y = 1; y < text.Bounds.Height - 1; y++)
        for (double x = 1; x < text.Bounds.Width - 1; x++)
        {
            Point local = new(x, y);
            if (!text.TryGetWordAt(local, out string found) || found != word) continue;
            Point? point = text.TranslatePoint(local, window);
            if (point is { } hit && ReferenceEquals(window.InputHitTest(hit), text)) return hit;
        }
        throw new Xunit.Sdk.XunitException("A hit-tested glyph pixel must exist for " + word);
    }
}
