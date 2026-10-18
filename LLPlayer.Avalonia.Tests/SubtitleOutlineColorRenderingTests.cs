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

public sealed class SubtitleOutlineColorRenderingTests
{
    const string Subtitle = "Hello wrapping subtitle words";

    [AvaloniaTheory]
    [InlineData(0, 3, "#FFFFFF", "#00FF00")]
    [InlineData(0, 1.25, "#1234AB", "#D02080")]
    [InlineData(1, 2.5, "#E8E8E8", "#00FF00")]
    [InlineData(1, 7.25, "#56ABCD", "#FFFFFF")]
    public void Changing_only_Outline_repaints_a_warm_subtitle_like_a_fresh_control_for_each_slot(
        int slot, double thickness, string fill, string color)
    {
        SubtitleText text = CreateText(slot, thickness, fill, Brushes.Black);
        Window window = new() { Width = 360, Height = 200, Background = Brushes.LightGray, Content = text };
        window.Show();
        try
        {
            byte[] before = Capture(window);
            Rect bounds = text.Bounds;
            Assert.True(bounds.Width > 0 && bounds.Height > 0);
            IBrush? originalFill = text.Fill;

            var target = new SolidColorBrush(Color.Parse(color));
            text.Outline = target;
            byte[] changed = Capture(window);
            Assert.Equal(bounds, text.Bounds); Assert.Equal(Subtitle, text.Text);
            Assert.Equal(32, text.FontSize); Assert.Equal(thickness, text.OutlineThickness); Assert.Equal(slot, text.Slot);
            Assert.Same(originalFill, text.Fill);

            SubtitleText fresh = CreateText(slot, thickness, fill, target);
            window.Content = fresh;
            byte[] expected = Capture(window);
            Assert.Equal(bounds, fresh.Bounds);
            Assert.False(before.AsSpan().SequenceEqual(expected), "The target outline must visibly differ from the initial black outline.");
            Assert.False(before.AsSpan().SequenceEqual(changed), "Changing only Outline must repaint the cached subtitle geometry.");
            Assert.Equal(expected, changed);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void At_zero_thickness_outline_color_does_not_change_pixels_or_recolor_the_existing_black_shadow(int slot)
    {
        SubtitleText text = CreateText(slot, 0, "#FFFFFF", Brushes.Black);
        Window window = new() { Width = 360, Height = 200, Background = Brushes.White, Content = text };
        window.Show();
        try
        {
            byte[] before = Capture(window);
            Rect bounds = text.Bounds;
            // White fill on a white backdrop leaves only the existing black shadow visible.
            Assert.True(HasNeutralDarkPixel(before), "Zero outline thickness must retain the visible black subtitle shadow.");
            text.Outline = Brushes.Magenta;
            byte[] changed = Capture(window);
            Assert.Equal(bounds, text.Bounds); Assert.Equal(0, text.OutlineThickness); Assert.Equal(Subtitle, text.Text);
            Assert.Equal(before, changed);
            Assert.True(HasNeutralDarkPixel(changed));
            window.Content = CreateText(slot, 0, "#FFFFFF", Brushes.Magenta);
            Assert.Equal(before, Capture(window));
        }
        finally { window.Close(); }
    }

    static bool HasNeutralDarkPixel(byte[] pixels)
    {
        for (int index = 0; index < pixels.Length; index += 4)
            if (pixels[index] < 230 && pixels[index] == pixels[index + 1] && pixels[index + 1] == pixels[index + 2] && pixels[index + 3] == 255)
                return true;
        return false;
    }

    static SubtitleText CreateText(int slot, double thickness, string fill, IBrush outline)
        => new()
        {
            Text = Subtitle,
            Slot = slot,
            FontSize = 32,
            Width = 320,
            Height = 150,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new SolidColorBrush(Color.Parse(fill)),
            Outline = outline,
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
