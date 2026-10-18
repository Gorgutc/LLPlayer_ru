using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;

namespace LLPlayer.Avalonia.Tests;

public sealed class SubtitleTextColorRenderingTests
{
    const string Subtitle = "Hello wrapping subtitle words";

    [AvaloniaTheory]
    [InlineData("#00FF00")]
    [InlineData("#D02080")]
    [InlineData("#000000")]
    public void Changing_only_Fill_repaints_an_existing_subtitle_like_a_fresh_control(string color)
    {
        SubtitleText text = CreateText(Brushes.White);
        Window window = new() { Width = 360, Height = 200, Background = Brushes.LightGray, Content = text };
        window.Show();
        try
        {
            byte[] before = Capture(window);
            Rect bounds = text.Bounds;
            Assert.True(bounds.Width > 0 && bounds.Height > 0);

            var target = new SolidColorBrush(Color.Parse(color));
            text.Fill = target;
            byte[] changed = Capture(window);
            Assert.Equal(bounds, text.Bounds);
            Assert.Equal(Subtitle, text.Text);
            Assert.Equal(3, text.OutlineThickness);

            SubtitleText fresh = CreateText(target);
            window.Content = fresh;
            byte[] expected = Capture(window);
            Assert.Equal(bounds, fresh.Bounds);
            Assert.False(before.AsSpan().SequenceEqual(expected), "The target Fill must visibly differ from the initial white text.");
            Assert.False(before.AsSpan().SequenceEqual(changed), "Changing only Fill must invalidate the rendered subtitle.");
            Assert.Equal(expected, changed);
        }
        finally { window.Close(); }
    }

    static SubtitleText CreateText(IBrush fill)
        => new()
        {
            Text = Subtitle,
            FontSize = 32,
            Width = 320,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = fill,
            Outline = Brushes.Black,
            OutlineThickness = 3,
        };

    static byte[] Capture(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Headless Skia did not provide a rendered frame.");
        using ILockedFramebuffer framebuffer = frame.Lock();
        Assert.Equal(PixelFormat.Bgra8888, framebuffer.Format);
        int rowLength = frame.PixelSize.Width * 4;
        byte[] pixels = new byte[rowLength * frame.PixelSize.Height];
        for (int row = 0; row < frame.PixelSize.Height; row++)
            Marshal.Copy(framebuffer.Address + row * framebuffer.RowBytes, pixels, row * rowLength, rowLength);
        return pixels;
    }
}
