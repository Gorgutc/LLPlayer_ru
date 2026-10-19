using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;

namespace LLPlayer.Avalonia.Tests;

public sealed class SubtitleFontRenderingTests
{
    const string ShortText = "Inter italic alphabet wrapping words";
    const string LongText = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda omega";
    const double FontSize = 28;
    const double Padding = 3;
    static readonly FontFamily Inter = new("fonts:Inter#Inter");
    static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(32, 220, 80));

    [AvaloniaFact]
    public void Existing_bundled_Inter_synthetic_italic_changes_FormattedText_glyph_pixels()
    {
        // Independent feasibility proof: this draws no SubtitleText and needs no installed system font or new asset.
        Typeface normal = new(Inter, FontStyle.Normal, FontWeight.SemiBold);
        Typeface italic = new(Inter, FontStyle.Italic, FontWeight.SemiBold);
        Assert.StartsWith("Inter", normal.GlyphTypeface.FamilyName, StringComparison.Ordinal);
        Assert.Equal(normal.GlyphTypeface.FamilyName, italic.GlyphTypeface.FamilyName);
        Assert.True((italic.GlyphTypeface.FontSimulations & FontSimulations.Oblique) != 0,
            $"The existing upright-only Inter package must synthesize oblique; got {italic.GlyphTypeface.FontSimulations}.");

        Window window = CreateWindow(new TypefaceGeometry(normal), 540, 140);
        window.Show();
        try
        {
            Frame upright = Capture(window);
            Assert.True(CountInk(upright) > 100, "The probe must draw visible upright Inter glyphs.");
            window.Content = new TypefaceGeometry(italic);
            Frame slanted = Capture(window);
            Assert.True(CountInk(slanted) > 100, "The probe must draw visible italic Inter glyphs.");
            Assert.False(upright.Pixels.AsSpan().SequenceEqual(slanted.Pixels),
                "FormattedText.BuildGeometry must apply the existing Inter synthetic oblique to actual glyph pixels.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, 400)]
    [InlineData(0, 700)]
    [InlineData(1, 400)]
    [InlineData(1, 700)]
    public void Changing_only_weight_repaints_a_warm_subtitle_like_a_fresh_control(int slot, int weight)
    {
        SubtitleText text = CreateText(slot);
        Assert.Equal(FontWeight.SemiBold, text.FontWeight);
        Window window = CreateWindow(text);
        window.Show();
        try
        {
            Frame before = Capture(window);
            Rect bounds = text.Bounds;
            FontWeight target = (FontWeight)weight;
            text.FontWeight = target;
            Frame changed = Capture(window);
            Assert.Equal(bounds, text.Bounds);
            AssertDecorationUnchanged(text, slot);
            SubtitleText fresh = CreateText(slot);
            fresh.FontWeight = target;
            window.Content = fresh;
            Frame expected = Capture(window);
            Assert.True(CountInk(expected) > 100);
            Assert.False(before.Pixels.AsSpan().SequenceEqual(expected.Pixels),
                "The chosen bundled Inter weight must visibly differ from the existing SemiBold default.");
            Assert.False(before.Pixels.AsSpan().SequenceEqual(changed.Pixels),
                "Changing only FontWeight must replace the warm glyph geometry.");
            Assert.Equal(expected.Pixels, changed.Pixels);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Changing_only_style_repaints_warm_Inter_italic_and_restores_upright_pixels(int slot)
    {
        SubtitleText text = CreateText(slot);
        Window window = CreateWindow(text);
        window.Show();
        try
        {
            Frame before = Capture(window);
            Rect bounds = text.Bounds;
            SetStyle(text, FontStyle.Italic);
            Frame changed = Capture(window);
            Assert.Equal(bounds, text.Bounds);
            AssertDecorationUnchanged(text, slot);

            SubtitleText fresh = CreateText(slot);
            SetStyle(fresh, FontStyle.Italic);
            window.Content = fresh;
            Frame expected = Capture(window);
            Assert.True(CountInk(expected) > 100);
            Assert.False(before.Pixels.AsSpan().SequenceEqual(expected.Pixels),
                "Selecting italic must change visible bundled Inter glyphs, beyond storing a style flag.");
            Assert.False(before.Pixels.AsSpan().SequenceEqual(changed.Pixels),
                "Changing only FontStyle must repaint the already rendered subtitle.");
            Assert.Equal(expected.Pixels, changed.Pixels);

            window.Content = text;
            SetStyle(text, FontStyle.Normal);
            Assert.Equal(before.Pixels, Capture(window).Pixels);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Bundled_Inter_family_binding_matches_fresh_explicit_Inter_without_system_font_assumptions(int slot)
    {
        SubtitleText text = CreateText(slot);
        FamilySource source = new();
        text.Bind(SubtitleText.FontFamilyProperty, new Binding(nameof(FamilySource.Family)) { Source = source });
        Window window = CreateWindow(text);
        window.Show();
        try
        {
            Capture(window); // Warm the fallback layout before changing only the family binding source.
            FontFamily selected = new("fonts:Inter#Inter");
            Assert.Equal("Inter", new Typeface(selected).GlyphTypeface.FamilyName);
            source.Family = selected;
            Frame changed = Capture(window);
            Assert.Same(selected, text.FontFamily);
            AssertDecorationUnchanged(text, slot);
            SubtitleText fresh = CreateText(slot);
            window.Content = fresh;
            Frame expected = Capture(window);
            Assert.True(CountInk(expected) > 100);
            Assert.Equal(expected.Pixels, changed.Pixels);
            // The application fallback may already be Inter. This deliberately makes no family-difference claim.
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Changing_only_available_family_reflows_and_repaints_warm_text_like_fresh_with_independent_word_hits(int slot)
    {
        const double width = 300;
        FontFamily selected = FindDistinctAvailableFamily(width - 2 * Padding);
        Typeface selectedFace = new(selected, FontStyle.Normal, FontWeight.Normal);
        Assert.False(selectedFace.GlyphTypeface.FamilyName.StartsWith("Inter", StringComparison.OrdinalIgnoreCase));
        SubtitleText text = CreateText(slot, fixedSize: false);
        text.Text = LongText;
        text.FontWeight = FontWeight.Normal;
        FamilySource source = new() { Family = Inter };
        text.Bind(SubtitleText.FontFamilyProperty, new Binding(nameof(FamilySource.Family)) { Source = source });
        Window window = CreateWindow(text, width, 680);
        window.Show();
        try
        {
            Frame before = Capture(window);
            LayoutEvidence previous = AssertGlyphsAndWords(window, text, before, FontStyle.Normal, FontWeight.Normal, slot);
            source.Family = selected; // No text, size, weight, style, decoration, or viewport change.
            Frame changed = Capture(window);
            Assert.Same(selected, text.FontFamily);
            LayoutEvidence current = AssertGlyphsAndWords(window, text, changed, FontStyle.Normal, FontWeight.Normal, slot, selected);
            Assert.True(previous.Height != current.Height || previous.FirstWord != current.FirstWord || previous.LastWord != current.LastWord
                || !previous.LineStarts.AsSpan().SequenceEqual(current.LineStarts),
                $"The confirmed '{selectedFace.GlyphTypeface.FamilyName}' family must change wrapped glyph positions or line geometry.");

            SubtitleText fresh = CreateText(slot, fixedSize: false);
            fresh.Text = LongText;
            fresh.FontWeight = FontWeight.Normal;
            fresh.FontFamily = selected;
            window.Content = fresh;
            Frame expected = Capture(window);
            AssertGlyphsAndWords(window, fresh, expected, FontStyle.Normal, FontWeight.Normal, slot, selected);
            Assert.True(CountGlyphInk(expected) > 100, "The selected face must draw visible colored fill or outline glyphs.");
            Assert.False(before.Pixels.AsSpan().SequenceEqual(expected.Pixels),
                $"Resolved available family '{selectedFace.GlyphTypeface.FamilyName}' must change actual glyph pixels from Inter.");
            Assert.False(before.Pixels.AsSpan().SequenceEqual(changed.Pixels),
                "Changing only FontFamily must invalidate the already rendered geometry.");
            Assert.Equal(expected.Pixels, changed.Pixels);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Weight_style_and_resize_keep_wrapped_glyphs_and_word_hits_on_the_same_independent_layout(int slot)
    {
        // Place the first three words between their regular and bold widths to force a real weight-driven reflow.
        using TextLayout regularPrefix = ExpectedLayout("alpha beta gamma", FontStyle.Normal, FontWeight.Normal, double.PositiveInfinity);
        using TextLayout boldPrefix = ExpectedLayout("alpha beta gamma", FontStyle.Normal, FontWeight.Bold, double.PositiveInfinity);
        Assert.True(boldPrefix.Width > regularPrefix.Width + 2);
        double narrowWidth = (regularPrefix.Width + boldPrefix.Width) / 2 + 2 * Padding;
        SubtitleText text = CreateText(slot, fixedSize: false);
        text.Text = LongText;
        text.FontWeight = FontWeight.Normal;
        Window window = CreateWindow(text, narrowWidth, 680);
        window.Show();
        try
        {
            Frame regular = Capture(window);
            LayoutEvidence initial = AssertGlyphsAndWords(window, text, regular, FontStyle.Normal, FontWeight.Normal, slot);
            Assert.True(initial.LineStarts.Length >= 3);

            text.FontWeight = FontWeight.Bold;
            Frame bold = Capture(window);
            LayoutEvidence weighted = AssertGlyphsAndWords(window, text, bold, FontStyle.Normal, FontWeight.Bold, slot);
            Assert.False(initial.LineStarts.AsSpan().SequenceEqual(weighted.LineStarts),
                "This fixture must move word boundaries when only the font weight changes.");

            SetStyle(text, FontStyle.Italic);
            Frame italic = Capture(window);
            LayoutEvidence narrow = AssertGlyphsAndWords(window, text, italic, FontStyle.Italic, FontWeight.Bold, slot);
            Assert.False(bold.Pixels.AsSpan().SequenceEqual(italic.Pixels));
            double narrowHeight = text.Bounds.Height;

            window.Width = narrowWidth * 1.9;
            Frame wide = Capture(window);
            LayoutEvidence resized = AssertGlyphsAndWords(window, text, wide, FontStyle.Italic, FontWeight.Bold, slot);
            Assert.True(resized.LineStarts.Length < narrow.LineStarts.Length);
            Assert.True(text.Bounds.Height < narrowHeight);
            Assert.True(resized.LastWord.Y < narrow.LastWord.Y);

            window.Width = narrowWidth;
            Frame restored = Capture(window);
            LayoutEvidence restoredLayout = AssertGlyphsAndWords(window, text, restored, FontStyle.Italic, FontWeight.Bold, slot);
            Assert.Equal(narrow.LineStarts, restoredLayout.LineStarts);
            Assert.Equal(narrowHeight, text.Bounds.Height);
            Assert.Equal(italic.Pixels, restored.Pixels);
        }
        finally { window.Close(); }
    }

    static LayoutEvidence AssertGlyphsAndWords(Window window, SubtitleText text, Frame frame, FontStyle style, FontWeight weight, int slot,
        FontFamily? family = null)
    {
        // Expectations come from a separate known font layout, never from the control's own hit-test result.
        using TextLayout expected = ExpectedLayout(LongText, style, weight, text.Bounds.Width - 2 * Padding, family);
        double expectedHeight = expected.Height + 2 * Padding;
        if (text.UseLayoutRounding)
            expectedHeight = LayoutHelper.RoundLayoutSizeUp(new Size(text.Bounds.Width, expectedHeight), window.RenderScaling).Height;
        Assert.Equal(expectedHeight, text.Bounds.Height, 5);
        Point origin = text.TranslatePoint(default, window)!.Value;
        List<Point> samples = [];
        foreach (Match match in Regex.Matches(LongText, "[a-z]+"))
        {
            Rect first = expected.HitTestTextPosition(match.Index);
            Rect last = expected.HitTestTextPosition(match.Index + match.Length - 1);
            Assert.Equal(first.Y, last.Y, 5); // Each fixture word fits within a line.
            Rect wordBounds = new(first.X + Padding + origin.X, first.Y + Padding + origin.Y,
                last.Right - first.X, Math.Max(first.Height, last.Height));
            Point glyph = FindInk(frame, wordBounds);
            Point local = new(glyph.X - origin.X, glyph.Y - origin.Y);
            Assert.True(text.TryGetWordAt(local, out string actual), $"A visible '{match.Value}' glyph at {local} must be clickable.");
            Assert.Equal(match.Value, actual);
            samples.Add(local);
        }

        Assert.True(samples[^1].Y > samples[0].Y, "The last word must remain on a later wrapped line.");
        Assert.False(text.TryGetWordAt(new Point(text.Bounds.Width / 2, Padding / 2), out _));
        Assert.False(text.TryGetWordAt(new Point(text.Bounds.Width / 2, text.Bounds.Height - 1), out _));
        Assert.False(text.TryGetWordAt(new Point(0, samples[0].Y), out _));
        // Wrapped lines may include a trailing space beyond maxWidth, which intentionally hits the preceding word.
        // The final omega line has no trailing whitespace, so its right control padding is an unambiguous miss.
        Assert.False(text.TryGetWordAt(new Point(text.Bounds.Width - 1, samples[^1].Y), out _));

        WordClickedEventArgs? clicked = null;
        void OnWordClicked(object? sender, WordClickedEventArgs args) => clicked = args;
        text.WordClicked += OnWordClicked;
        try
        {
            foreach ((Point point, string word) in new[] { (samples[0], "alpha"), (samples[^1], "omega") })
            {
                clicked = null;
                window.MouseDown(point + origin, MouseButton.Left);
                window.MouseUp(point + origin, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(clicked);
                Assert.Equal(word, clicked.Word);
                Assert.Equal(LongText, clicked.Text);
                Assert.Equal(slot, clicked.Slot);
            }
        }
        finally { text.WordClicked -= OnWordClicked; }
        return new LayoutEvidence(expected.TextLines.Select(line => line.FirstTextSourceIndex).ToArray(), samples[0], samples[^1], text.Bounds.Height);
    }

    static TextLayout ExpectedLayout(string value, FontStyle style, FontWeight weight, double width, FontFamily? family = null)
        => new(value, new Typeface(family ?? Inter, style, weight), FontSize, Ink, TextAlignment.Center, TextWrapping.Wrap, maxWidth: width);

    static FontFamily FindDistinctAvailableFamily(double width)
    {
        using TextLayout baseline = ExpectedLayout(LongText, FontStyle.Normal, FontWeight.Normal, width);
        FontFamily[] families = FontManager.Current.SystemFonts.OrderBy(family => family.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (FontFamily family in families)
        {
            Typeface typeface = new(family, FontStyle.Normal, FontWeight.Normal);
            if (!FontManager.Current.TryGetGlyphTypeface(typeface, out GlyphTypeface? glyphTypeface) || glyphTypeface == null
                || glyphTypeface.FamilyName.StartsWith("Inter", StringComparison.OrdinalIgnoreCase)
                || LongText.Any(character => !char.IsWhiteSpace(character) && !glyphTypeface.CharacterToGlyphMap.ContainsGlyph(character)))
                continue;

            using TextLayout candidate = ExpectedLayout(LongText, FontStyle.Normal, FontWeight.Normal, width, family);
            if (candidate.Height > 600 || candidate.TextLines.Count < 2) continue;
            bool changed = Math.Abs(candidate.Height - baseline.Height) > 1;
            for (int index = 0; !changed && index < LongText.Length; index++)
            {
                Rect actual = candidate.HitTestTextPosition(index), original = baseline.HitTestTextPosition(index);
                changed = Math.Abs(actual.X - original.X) > 1 || Math.Abs(actual.Y - original.Y) > 1;
            }
            if (changed) return family;
        }

        Assert.Fail($"Font-family rendering needs an installed non-Inter face covering the ASCII fixture with distinct layout metrics. "
            + $"No suitable face among {families.Length} discovered system families; this environment requires a usable system-font baseline.");
        return null!;
    }

    static Point FindInk(Frame frame, Rect region)
    {
        double best = double.PositiveInfinity;
        Point? found = null;
        for (int y = Math.Max(0, (int)Math.Ceiling(region.Top)); y < Math.Min(frame.Height, region.Bottom); y++)
            for (int x = Math.Max(0, (int)Math.Ceiling(region.Left)); x < Math.Min(frame.Width, region.Right); x++)
            {
                if (!IsGlyphInk(frame.Pixels, (y * frame.Width + x) * 4)) continue;
                Point candidate = new(x + 0.5, y + 0.5);
                double dx = candidate.X - region.Center.X, dy = candidate.Y - region.Center.Y;
                double distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; found = candidate; }
            }
        Assert.True(found.HasValue, $"Expected independently positioned glyph ink within {region}.");
        return found!.Value;
    }

    static int CountInk(Frame frame)
    {
        int count = 0;
        for (int index = 0; index < frame.Pixels.Length; index += 4)
            if (IsInk(frame.Pixels, index)) count++;
        return count;
    }

    static bool IsInk(byte[] pixels, int index)
        => pixels[index] < 120 && pixels[index + 1] > 180 && pixels[index + 2] < 100 && pixels[index + 3] > 200;

    static int CountGlyphInk(Frame frame)
    {
        int count = 0;
        for (int index = 0; index < frame.Pixels.Length; index += 4)
            if (IsGlyphInk(frame.Pixels, index)) count++;
        return count;
    }

    static bool IsGlyphInk(byte[] pixels, int index)
    {
        int blue = pixels[index], green = pixels[index + 1], red = pixels[index + 2];
        // Thin vector glyphs may have only antialiased fill. Their colored outline is also real glyph ink;
        // the black backdrop and neutral black shadow cannot satisfy either chromatic-color predicate.
        return pixels[index + 3] > 200
            && ((green > 32 && green > 2 * red && green > 1.5 * blue)
                || (red > 128 && blue > 128 && green < 100));
    }

    static void AssertDecorationUnchanged(SubtitleText text, int slot)
    {
        Assert.Equal(slot, text.Slot);
        Assert.Equal(ShortText, text.Text);
        Assert.Equal(FontSize, text.FontSize);
        Assert.Equal(Padding, text.OutlineThickness);
        Assert.Same(Ink, text.Fill);
        Assert.Same(Brushes.Magenta, text.Outline);
    }

    // Reflection preserves a compilable pre-feature RED baseline; the tests above assert actual rendering and input.
    static void SetStyle(SubtitleText text, FontStyle value)
    {
        PropertyInfo? property = typeof(SubtitleText).GetProperty("FontStyle");
        Assert.NotNull(property);
        Assert.Equal(typeof(FontStyle), property.PropertyType);
        property.SetValue(text, value);
    }

    static SubtitleText CreateText(int slot, bool fixedSize = true)
        => new()
        {
            Text = ShortText, Slot = slot, FontFamily = Inter, FontSize = FontSize,
            Width = fixedSize ? 480 : double.NaN, Height = fixedSize ? 170 : double.NaN,
            HorizontalAlignment = fixedSize ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            VerticalAlignment = fixedSize ? VerticalAlignment.Center : VerticalAlignment.Top,
            Fill = Ink, Outline = Brushes.Magenta, OutlineThickness = Padding,
        };

    static Window CreateWindow(Control content, double width = 520, double height = 220)
        => new() { Width = width, Height = height, Background = Brushes.Black, Content = content };

    static Frame Capture(Window window)
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

    sealed class TypefaceGeometry(Typeface face) : Control
    {
        public override void Render(DrawingContext context)
        {
            FormattedText formatted = new(ShortText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 36, Ink)
            {
                MaxTextWidth = 500,
                TextAlignment = TextAlignment.Center,
            };
            Geometry? geometry = formatted.BuildGeometry(new Point(20, 30));
            Assert.NotNull(geometry);
            context.DrawGeometry(Ink, null, geometry);
        }
    }

    sealed class FamilySource : INotifyPropertyChanged
    {
        FontFamily family = FontFamily.Default;
        public FontFamily Family
        {
            get => family;
            set { family = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Family))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    sealed record Frame(byte[] Pixels, int Width, int Height);
    sealed record LayoutEvidence(int[] LineStarts, Point FirstWord, Point LastWord, double Height);
}
