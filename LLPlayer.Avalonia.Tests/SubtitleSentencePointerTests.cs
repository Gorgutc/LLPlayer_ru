using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;

namespace LLPlayer.Avalonia.Tests;

public class SubtitleSentencePointerTests
{
    [AvaloniaTheory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Middle_release_selects_the_whole_displayed_cue_once_with_slot_and_metadata(int slot, bool translated)
    {
        const string cue = "Hello brave\nnew world";
        SubtitleText text = new()
        {
            Text = cue, Slot = slot, FontSize = 24, OutlineThickness = 0,
            SourceLanguage = "en", TargetLanguage = "de", IsTranslated = translated,
        };
        Window window = new() { Width = 500, Height = 160, Content = text };
        List<WordClickedEventArgs> events = [];
        int pressed = 0;
        text.WordClicked += (_, args) => events.Add(args);
        text.WordPressed += (_, _) => pressed++;
        window.Show();
        try
        {
            window.CaptureRenderedFrame()?.Dispose();
            Point hit = FindWord(window, text, "brave");
            window.MouseMove(hit);
            window.MouseDown(hit, MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
            window.MouseUp(hit, MouseButton.Middle, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            WordClickedEventArgs selected = Assert.Single(events);
            Assert.Equal("Hello brave new world", selected.Word);
            Assert.Equal(cue, selected.Text);
            Assert.Equal(slot, selected.Slot);
            Assert.Equal(translated, selected.IsTranslated);
            Assert.Equal("en", selected.SourceLanguage);
            Assert.Equal("de", selected.TargetLanguage);
            Assert.False(selected.IsWord);
            Assert.Equal(MouseButton.Middle, selected.Button);
            Assert.Equal(KeyModifiers.Control, selected.Modifiers);
            Assert.Equal(1, pressed);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Changing_the_cue_between_middle_press_and_release_discards_the_sentence()
    {
        SubtitleText text = new() { Text = "First sentence", FontSize = 24, OutlineThickness = 0 };
        Window window = new() { Width = 500, Height = 160, Content = text };
        List<WordClickedEventArgs> events = [];
        text.WordClicked += (_, args) => events.Add(args);
        window.Show();
        try
        {
            window.CaptureRenderedFrame()?.Dispose();
            Point hit = FindWord(window, text, "First");
            window.MouseDown(hit, MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
            text.Text = "Replacement sentence";
            window.MouseUp(hit, MouseButton.Middle);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(events);
        }
        finally { window.Close(); }
    }

    internal static Point FindWord(Window window, SubtitleText text, string word)
    {
        for (double y = 1; y < text.Bounds.Height - 1; y++)
        for (double x = 1; x < text.Bounds.Width - 1; x++)
        {
            Point local = new(x, y);
            if (text.TryGetWordAt(local, out string found) && found == word
                && text.TranslatePoint(local, window) is { } hit
                && ReferenceEquals(window.InputHitTest(hit), text)) return hit;
        }
        throw new Xunit.Sdk.XunitException("A hit-tested glyph pixel must exist for " + word);
    }
}
