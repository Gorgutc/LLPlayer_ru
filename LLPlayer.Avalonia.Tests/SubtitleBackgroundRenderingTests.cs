using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;
using static LLPlayer.Avalonia.Tests.SubtitleBackgroundTestSupport;

namespace LLPlayer.Avalonia.Tests;

public sealed class SubtitleBackgroundRenderingTests
{
    [AvaloniaFact]
    public void Pixel_oracle_distinguishes_transparency_half_alpha_and_opaque_on_a_known_backdrop()
    {
        // This independent drawing probe must pass before the feature assertions can be trusted.
        Window window = CreateWindow(new RectangleProbe());
        window.Show();
        try
        {
            Frame frame = Capture(window);
            Assert.Equal(Backdrop, frame.Pixel(4, 4));
            AssertPixel(frame.Pixel(24, 24), Composite(Colors.Black, .5));
            Assert.Equal(Colors.Black, frame.Pixel(74, 24));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, 0, 28, false)]
    [InlineData(1, 8, 29.25, true)]
    public void Default_and_zero_alpha_keep_the_exact_pre_background_glyph_outline_shadow_pixels(
        int slot, double outline, double size, bool italic)
    {
        SubtitleText text = CreateText(slot, outline, size, italic);
        Window window = CreateWindow(new LegacySubtitleDrawing(text));
        window.Show();
        try
        {
            Frame baseline = Capture(window);
            Assert.True(CountInk(baseline) > 100, "The legacy oracle must draw visible subtitle glyphs.");
            window.Content = text;
            Frame current = Capture(window);
            Assert.Equal(baseline.Pixels, current.Pixels);
            Assert.Null(GetBackground(text));
            SetBackground(text, new SolidColorBrush(Colors.Black, 0));
            Assert.Equal(baseline.Pixels, Capture(window).Pixels);
            SetBackground(text, new SolidColorBrush(Color.FromArgb(0, 190, 20, 80)));
            Assert.Equal(baseline.Pixels, Capture(window).Pixels);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 50)]
    [InlineData(0, 0, 100)]
    [InlineData(1, 8, 0)]
    [InlineData(1, 8, 50)]
    [InlineData(1, 8, 100)]
    public void Background_alpha_blends_one_compact_whole_wrapped_block_without_changing_layout(
        int slot, double outline, int percent)
    {
        SubtitleText text = CreateText(slot, outline);
        Window window = CreateWindow(text);
        window.Show();
        try
        {
            Frame before = Capture(window);
            Assert.True(CountInk(before) > 100);
            using TextLayout expected = ExpectedLayout(text);
            Assert.True(expected.TextLines.Count >= 3, "The fixture must wrap before its final short line.");
            Rect block = ExpectedBlock(text, expected);
            Assert.True(block.Width < text.Bounds.Width - 10, "The expected box must be compact, not full control width.");
            Rect bounds = text.Bounds;
            SetBackground(text, new SolidColorBrush(Colors.Black, percent / 100d));
            Frame after = Capture(window);
            Assert.Equal(bounds, text.Bounds);
            Assert.Equal(before.Width, after.Width);
            Assert.Equal(before.Height, after.Height);
            AssertRectanglePixels(text, before, after, block, Colors.Black, percent / 100d);
            AssertSolidGlyphPixelsUnchanged(before, after);

            // The final I is much narrower than preceding wrapped lines. Its empty side must still be filled,
            // distinguishing one rectangle for the whole text block from separate rectangles behind each line.
            Point origin = text.TranslatePoint(default, window)!.Value;
            TextLine last = expected.TextLines[^1];
            double lastTop = expected.TextLines.Take(expected.TextLines.Count - 1).Sum(line => line.Height);
            Rect emptySide = new(block.Left + 2, outline + lastTop + last.Height / 3,
                last.Start - block.Left - 4, Math.Max(2, last.Height / 3));
            Point sample = FindBackdrop(before, emptySide.Translate(new Vector(origin.X, origin.Y)));
            AssertPixel(after.Pixel((int)sample.X, (int)sample.Y), Composite(Colors.Black, percent / 100d));
            if (percent == 0) Assert.Equal(before.Pixels, after.Pixels);
            else Assert.False(before.Pixels.AsSpan().SequenceEqual(after.Pixels));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Warm_background_color_and_opacity_changes_match_fresh_controls_and_restore_zero_pixels(int slot)
    {
        SubtitleText text = CreateText(slot);
        Window window = CreateWindow(text);
        window.Show();
        try
        {
            Frame zero = Capture(window);
            Rect bounds = text.Bounds;
            foreach ((Color color, double alpha) in new[] { (Colors.Black, .5), (Colors.DarkRed, .5), (Colors.DarkRed, 1d), (Colors.DarkRed, 0d) })
            {
                SetBackground(text, new SolidColorBrush(color, alpha));
                Frame warm = Capture(window);
                Assert.Equal(bounds, text.Bounds);
                using TextLayout expected = ExpectedLayout(text);
                AssertRectanglePixels(text, zero, warm, ExpectedBlock(text, expected), color, alpha);
                AssertSolidGlyphPixelsUnchanged(zero, warm);
                SubtitleText fresh = CreateText(slot);
                SetBackground(fresh, new SolidColorBrush(color, alpha));
                window.Content = fresh;
                Frame cold = Capture(window);
                Assert.Equal(bounds, fresh.Bounds);
                Assert.Equal(cold.Pixels, warm.Pixels);
                window.Content = text;
                Capture(window);
            }
            Assert.Equal(zero.Pixels, Capture(window).Pixels);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(8)]
    public void Fractional_italic_font_and_resize_move_the_background_with_the_same_independent_line_layout(double outline)
    {
        SubtitleText text = CreateText(0, outline, 29.25, italic: true);
        Window window = CreateWindow(text);
        window.Show();
        try
        {
            foreach (double width in new[] { 246d, 381.5, 246d })
            {
                text.Width = width;
                SetBackground(text, null);
                Frame before = Capture(window);
                using TextLayout expected = ExpectedLayout(text);
                Rect block = ExpectedBlock(text, expected);
                SetBackground(text, new SolidColorBrush(Colors.Black, .5));
                Frame after = Capture(window);
                AssertRectanglePixels(text, before, after, block, Colors.Black, .5);
                AssertSolidGlyphPixelsUnchanged(before, after);
                SubtitleText fresh = CreateText(0, outline, 29.25, italic: true);
                fresh.Width = width;
                SetBackground(fresh, new SolidColorBrush(Colors.Black, .5));
                window.Content = fresh;
                Assert.Equal(after.Pixels, Capture(window).Pixels);
                window.Content = text;
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public void Missing_empty_and_whitespace_subtitles_draw_no_background(string? value)
    {
        SubtitleText text = CreateText(0);
        text.Text = value;
        Window window = CreateWindow(text);
        window.Show();
        try
        {
            Frame before = Capture(window);
            Assert.True(before.AllPixels(Backdrop));
            SetBackground(text, Brushes.Black);
            Assert.Equal(before.Pixels, Capture(window).Pixels);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hidden_subtitle_draws_no_background()
    {
        SubtitleText text = CreateText(1);
        text.IsVisible = false;
        Window window = CreateWindow(text);
        window.Show();
        try
        {
            Frame before = Capture(window);
            Assert.True(before.AllPixels(Backdrop));
            SetBackground(text, Brushes.Black);
            Assert.Equal(before.Pixels, Capture(window).Pixels);
        }
        finally { window.Close(); }
    }

    sealed class RectangleProbe : Control
    {
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(new SolidColorBrush(Colors.Black, .5), null, new Rect(20, 20, 30, 30));
            context.DrawRectangle(Brushes.Black, null, new Rect(70, 20, 30, 30));
        }
    }

    // Frozen pre-feature renderer oracle. This intentionally calls no SubtitleText rendering or private helpers.
    sealed class LegacySubtitleDrawing(SubtitleText source) : Control
    {
        public override void Render(DrawingContext context)
        {
            double pad = source.OutlineThickness;
            FormattedText formatted = new(source.Text!, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(source.FontFamily, source.FontStyle, source.FontWeight), source.FontSize, source.Fill)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = source.Width - 2 * pad,
            };
            Geometry? geometry = formatted.BuildGeometry(default);
            IBrush shadow = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
            Point origin = new((Bounds.Width - source.Width) / 2, (Bounds.Height - source.Height) / 2);
            using (context.PushTransform(Matrix.CreateTranslation(origin.X + pad + 2, origin.Y + pad + 2.5)))
                context.DrawGeometry(shadow, new Pen(shadow, pad * 2 + 2, lineJoin: PenLineJoin.Round), geometry!);
            using (context.PushTransform(Matrix.CreateTranslation(origin.X + pad, origin.Y + pad)))
            {
                if (pad > 0) context.DrawGeometry(null, new Pen(source.Outline, pad * 2, lineJoin: PenLineJoin.Round), geometry!);
                context.DrawGeometry(source.Fill, null, geometry!);
            }
        }
    }
}

internal static class SubtitleBackgroundTestSupport
{
    internal const string Subtitle = "alpha beta gamma delta epsilon\nI";
    internal static readonly Color Backdrop = Color.FromRgb(180, 200, 220);
    internal static readonly FontFamily Inter = new("fonts:Inter#Inter");
    internal static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(20, 240, 40));

    internal static SubtitleText CreateText(int slot, double outline = 3, double size = 28, bool italic = false)
        => new()
        {
            Text = Subtitle, Slot = slot, FontFamily = Inter, FontSize = size,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal, FontWeight = FontWeight.SemiBold,
            Width = 246, Height = 210, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Fill = Ink, Outline = Brushes.Magenta,
            OutlineThickness = outline,
        };

    internal static Window CreateWindow(Control content)
        => new() { Width = 480, Height = 300, Background = new SolidColorBrush(Backdrop), Content = content };

    internal static TextLayout ExpectedLayout(SubtitleText text)
        => new(text.Text ?? "", new Typeface(text.FontFamily, text.FontStyle, text.FontWeight), text.FontSize,
            Ink, TextAlignment.Center, TextWrapping.Wrap, maxWidth: text.Bounds.Width - 2 * text.OutlineThickness);

    internal static Rect ExpectedBlock(SubtitleText text, TextLayout expected)
    {
        double top = text.OutlineThickness;
        Rect union = default;
        bool first = true;
        foreach (TextLine line in expected.TextLines)
        {
            Rect box = new(line.Start + text.OutlineThickness, top, line.WidthIncludingTrailingWhitespace, line.Height);
            union = first ? box : union.Union(box);
            first = false;
            top += line.Height;
        }
        return union.Inflate(text.OutlineThickness);
    }

    internal static IBrush? GetBackground(SubtitleText text)
    {
        PropertyInfo? property = typeof(SubtitleText).GetProperty("Background");
        Assert.NotNull(property);
        Assert.Equal(typeof(IBrush), property.PropertyType);
        return (IBrush?)property.GetValue(text);
    }

    internal static void SetBackground(SubtitleText text, IBrush? value)
    {
        PropertyInfo? property = typeof(SubtitleText).GetProperty("Background");
        Assert.NotNull(property);
        Assert.Equal(typeof(IBrush), property.PropertyType);
        property.SetValue(text, value);
    }

    internal static void AssertRectanglePixels(SubtitleText text, Frame before, Frame after, Rect block, Color color, double alpha, Window? window = null)
    {
        window ??= (Window)TopLevel.GetTopLevel(text)!;
        Point origin = text.TranslatePoint(default, window)!.Value;
        Rect expected = block.Translate(new Vector(origin.X, origin.Y));
        int inside = 0, outside = 0;
        for (int y = 0; y < before.Height; y++)
            for (int x = 0; x < before.Width; x++)
            {
                if (before.Pixel(x, y) != Backdrop) continue; // Ignore glyph, outline and shadow coverage.
                Point pixel = new(x + .5, y + .5);
                if (expected.Deflate(1).Contains(pixel))
                {
                    AssertPixel(after.Pixel(x, y), Composite(color, alpha));
                    inside++;
                }
                else if (!expected.Inflate(1).Contains(pixel))
                {
                    Assert.Equal(Backdrop, after.Pixel(x, y));
                    outside++;
                }
            }
        Assert.True(inside > 200, "The oracle needs many clear pixels inside the independent text block.");
        Assert.True(outside > 1000, "The oracle needs clear outside pixels to reject full-width or extra-padding boxes.");
    }

    internal static Color Composite(Color color, double alpha)
        => Color.FromRgb((byte)Math.Round(color.R * alpha + Backdrop.R * (1 - alpha)),
            (byte)Math.Round(color.G * alpha + Backdrop.G * (1 - alpha)),
            (byte)Math.Round(color.B * alpha + Backdrop.B * (1 - alpha)));

    internal static void AssertSolidGlyphPixelsUnchanged(Frame before, Frame after)
    {
        int fill = 0;
        for (int y = 0; y < before.Height; y++)
            for (int x = 0; x < before.Width; x++)
            {
                Color c = before.Pixel(x, y);
                if (c != Color.FromRgb(20, 240, 40) && c != Colors.Magenta) continue;
                Assert.Equal(c, after.Pixel(x, y));
                if (c == Color.FromRgb(20, 240, 40)) fill++;
            }
        Assert.True(fill > 30, "The fixture must contain solid green glyph fill which the background cannot recolor.");
    }

    internal static void AssertPixel(Color actual, Color expected)
    {
        Assert.InRange(Math.Abs(actual.R - expected.R), 0, 1);
        Assert.InRange(Math.Abs(actual.G - expected.G), 0, 1);
        Assert.InRange(Math.Abs(actual.B - expected.B), 0, 1);
        Assert.Equal((byte)255, actual.A);
    }

    internal static Point FindBackdrop(Frame frame, Rect region)
    {
        for (int y = Math.Max(0, (int)Math.Ceiling(region.Top)); y < Math.Min(frame.Height, region.Bottom); y++)
            for (int x = Math.Max(0, (int)Math.Ceiling(region.Left)); x < Math.Min(frame.Width, region.Right); x++)
                if (frame.Pixel(x, y) == Backdrop) return new Point(x + .5, y + .5);
        Assert.Fail($"Expected a clear backdrop pixel within {region}.");
        return default;
    }

    internal static Point FindInk(Frame frame, Rect region)
    {
        for (int y = Math.Max(0, (int)Math.Ceiling(region.Top)); y < Math.Min(frame.Height, region.Bottom); y++)
            for (int x = Math.Max(0, (int)Math.Ceiling(region.Left)); x < Math.Min(frame.Width, region.Right); x++)
            {
                Color c = frame.Pixel(x, y);
                if (c.G > 180 && c.G > c.R * 2 && c.G > c.B * 2) return new Point(x + .5, y + .5);
            }
        Assert.Fail($"Expected visible independently positioned green glyph ink within {region}.");
        return default;
    }

    internal static int CountInk(Frame frame)
        => Enumerable.Range(0, frame.Width * frame.Height).Count(index =>
        {
            Color c = frame.Pixel(index % frame.Width, index / frame.Width);
            return c.G > 180 && c.G > c.R * 2 && c.G > c.B * 2;
        });

    internal static Frame Capture(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap bitmap = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Headless Skia did not provide a rendered frame.");
        using ILockedFramebuffer framebuffer = bitmap.Lock();
        Assert.Equal(PixelFormat.Bgra8888, framebuffer.Format);
        int rowLength = bitmap.PixelSize.Width * 4;
        byte[] pixels = new byte[rowLength * bitmap.PixelSize.Height];
        for (int row = 0; row < bitmap.PixelSize.Height; row++)
            Marshal.Copy(framebuffer.Address + row * framebuffer.RowBytes, pixels, row * rowLength, rowLength);
        return new Frame(pixels, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
    }

    internal sealed record Frame(byte[] Pixels, int Width, int Height)
    {
        internal Color Pixel(int x, int y)
        {
            int index = (y * Width + x) * 4;
            return Color.FromArgb(Pixels[index + 3], Pixels[index + 2], Pixels[index + 1], Pixels[index]);
        }
        internal bool AllPixels(Color expected)
            => Enumerable.Range(0, Width * Height).All(index => Pixel(index % Width, index / Width) == expected);
    }
}
