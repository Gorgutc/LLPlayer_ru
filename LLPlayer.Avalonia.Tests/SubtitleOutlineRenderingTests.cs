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

public sealed class SubtitleOutlineRenderingTests
{
    const string Subtitle = "Hello wrapping subtitle words";

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(3.1)]
    public void Changing_only_outline_thickness_repaints_like_a_fresh_control(double thickness)
    {
        SubtitleText text = CreateText(3);
        Window window = new() { Width = 360, Height = 200, Background = Brushes.LightGray, Content = text };
        window.Show();
        try
        {
            byte[] before = Capture(window);
            Rect bounds = text.Bounds;
            Assert.True(bounds.Width > 0 && bounds.Height > 0);

            text.OutlineThickness = thickness;
            byte[] changed = Capture(window);
            Assert.Equal(bounds, text.Bounds);
            Assert.Equal(Subtitle, text.Text);

            SubtitleText fresh = CreateText(thickness);
            window.Content = fresh;
            byte[] expected = Capture(window);
            Assert.Equal(bounds, fresh.Bounds);
            Assert.False(before.AsSpan().SequenceEqual(expected), "The chosen thickness must visibly differ from the initial outline.");
            Assert.Equal(expected, changed);
        }
        finally
        {
            window.Close();
        }
    }

    static SubtitleText CreateText(double thickness)
        => new()
        {
            Text = Subtitle,
            FontSize = 32,
            Width = 320,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = Brushes.White,
            Outline = Brushes.Black,
            OutlineThickness = thickness,
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
