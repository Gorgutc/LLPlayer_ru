using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.ViewModels;
using SkiaSharp;

namespace LLPlayer.Avalonia.Tests;

public class ScreenshotTests
{
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Developer_screenshot_keeps_shadow_toast_aligned_and_uses_canonical_dips(double desktopScaling)
    {
        using TempDir directory = new();
        var toast = new Toast { Title = "Subtitles loaded", Message = "test-720p.srt", Variant = ToastVariant.Success, Width = 320 };
        var canvas = new Canvas { Background = Brushes.White, Children = { toast } };
        Canvas.SetLeft(toast, 100);
        Canvas.SetTop(toast, 30);
        var window = new Window { Width = 640, Height = 240, Content = canvas, RequestedThemeVariant = ThemeVariant.Light };
        try
        {
            window.Show();
            window.SetRenderScaling(desktopScaling);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(desktopScaling, window.RenderScaling);

            string path = Path.Combine(directory.Path, "nested", "window.png");
            AppHost.SaveScreenshot(window, path);
            using var image = SKBitmap.Decode(path);
            Assert.NotNull(image);

            // Follow the actual layout into the saved image. This catches the HiDPI shadow/clip transform bug
            // independently of image dimensions: the accent must stay at its arranged position inside the card.
            var accent = toast.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Accent");
            Point center = accent.TranslatePoint(new Point(accent.Bounds.Width / 2, accent.Bounds.Height / 2), window)!.Value;
            double imageScale = image.Width / window.Bounds.Width;
            SKColor pixel = image.GetPixel((int)Math.Floor(center.X * imageScale), (int)Math.Floor(center.Y * imageScale));
            Assert.True(pixel.Green > 160 && pixel.Red < 100 && pixel.Blue < 160,
                $"Toast accent is missing at its arranged position for desktop scaling {desktopScaling}: {pixel}.");

            var title = toast.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Subtitles loaded");
            Point titleOrigin = title.TranslatePoint(default, window)!.Value;
            int glyphPixels = 0;
            for (int y = (int)Math.Ceiling(titleOrigin.Y * imageScale); y < (int)Math.Floor((titleOrigin.Y + title.Bounds.Height) * imageScale); y++)
                for (int x = (int)Math.Ceiling(titleOrigin.X * imageScale); x < (int)Math.Floor((titleOrigin.X + title.Bounds.Width) * imageScale); x++)
                {
                    SKColor glyph = image.GetPixel(x, y);
                    if (glyph.Red < 100 && glyph.Green < 100 && glyph.Blue < 100)
                        glyphPixels++;
                }
            Assert.True(glyphPixels > 30, $"Toast title glyphs are missing inside their arranged bounds ({glyphPixels} pixels).");

            Assert.Equal((int)Math.Ceiling(window.Bounds.Width), image.Width);
            Assert.Equal((int)Math.Ceiling(window.Bounds.Height), image.Height);
        }
        finally
        {
            window.Close();
        }
    }
}
