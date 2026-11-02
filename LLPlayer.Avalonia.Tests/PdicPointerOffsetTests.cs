using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LLPlayer.Avalonia.Controls;

namespace LLPlayer.Avalonia.Tests;

public sealed class PdicPointerOffsetTests
{
    [AvaloniaFact]
    public void ClickingTheSecondRepeatedWordRetainsItsOriginalUtf16Offset()
    {
        const string cue = "😀 word word";
        SubtitleText text = new() { Text = cue, FontSize = 24, OutlineThickness = 0 };
        Window window = new() { Width = 500, Height = 140, Content = text };
        WordClickedEventArgs? selected = null;
        text.WordClicked += (_, args) => selected = args;
        window.Show();
        try
        {
            window.CaptureRenderedFrame()?.Dispose();
            Point hit = default; bool found = false;
            for (int y = 1; y < text.Bounds.Height && !found; y++)
            for (int x = 1; x < text.Bounds.Width && !found; x++)
            {
                Point point = new(x, y);
                if (text.TryGetWordRangeAt(point, out int start, out _) && start == 8
                    && text.TranslatePoint(point, window) is { } translated
                    && ReferenceEquals(window.InputHitTest(translated), text))
                { hit = translated; found = true; }
            }
            Assert.True(found, "Second repeated word must have a hit-tested glyph.");
            window.MouseDown(hit, MouseButton.Left);
            window.MouseUp(hit, MouseButton.Left);
            Assert.NotNull(selected);
            Assert.Equal("word", selected.Word);
            var offset = typeof(WordClickedEventArgs).GetProperty("WordOffset");
            Assert.NotNull(offset);
            Assert.Equal(8, offset.GetValue(selected));
        }
        finally { window.Close(); }
    }
}
