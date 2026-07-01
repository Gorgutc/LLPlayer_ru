using System.Drawing;
using System.Drawing.Imaging;
using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer;

// ImageProcessor (SubtitlesOCR.cs) prepares bitmap subtitles for OCR: BlackText composites the original over a
// white background (SourceOver alpha blending), AddPadding adds a white border around the image. Both are pure
// GDI+ transforms. Expectations are derived from the code; asserts stick to dimensions, pixel format and pixels
// well inside / well outside the drawn region (no byte-identical bitmap comparisons, and no asserts on the
// blended boundary pixels — GDI+ interpolation there is not contractual).
public class ImageProcessorTests
{
    private static int Argb(Color c) => c.ToArgb(); // named vs constructed Color are never Equals — compare ARGB

    // === BlackText ===============================================================================

    [Fact]
    public void BlackText_PreservesDimensions()
    {
        using Bitmap original = new(7, 5);
        using Bitmap converted = ImageProcessor.BlackText(original);

        converted.Width.Should().Be(7);
        converted.Height.Should().Be(5);
    }

    [Theory]
    [InlineData(PixelFormat.Format32bppArgb)]
    [InlineData(PixelFormat.Format24bppRgb)]
    public void BlackText_PreservesPixelFormat(PixelFormat format)
    {
        using Bitmap original = new(4, 4, format);
        using Bitmap converted = ImageProcessor.BlackText(original);

        converted.PixelFormat.Should().Be(format);
    }

    [Fact]
    public void BlackText_FullyTransparentSource_BecomesWhite()
    {
        // A transparent subtitle bitmap must land on the white background (Clear(White) + SourceOver).
        using Bitmap original = new(6, 4, PixelFormat.Format32bppArgb); // new 32bppArgb bitmap = all transparent
        using Bitmap converted = ImageProcessor.BlackText(original);

        Argb(converted.GetPixel(0, 0)).Should().Be(Argb(Color.White));
        Argb(converted.GetPixel(3, 2)).Should().Be(Argb(Color.White));
        Argb(converted.GetPixel(5, 3)).Should().Be(Argb(Color.White));
    }

    [Fact]
    public void BlackText_OpaqueSource_KeepsSourceColor()
    {
        using Bitmap original = new(6, 4, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(original))
            g.Clear(Color.Red);

        using Bitmap converted = ImageProcessor.BlackText(original);

        // Opaque source wins over the white background under SourceOver; probe an interior pixel.
        Argb(converted.GetPixel(3, 2)).Should().Be(Argb(Color.Red));
    }

    // === AddPadding ==============================================================================

    [Theory]
    [InlineData(10, 6, 3, 16, 12)]
    [InlineData(10, 6, 0, 10, 6)]
    [InlineData(1, 1, 20, 41, 41)]
    public void AddPadding_AddsPaddingOnAllSides(int width, int height, int padding, int expectedWidth, int expectedHeight)
    {
        using Bitmap original = new(width, height);
        using Bitmap padded = ImageProcessor.AddPadding(original, padding);

        padded.Width.Should().Be(expectedWidth);
        padded.Height.Should().Be(expectedHeight);
    }

    [Theory]
    [InlineData(PixelFormat.Format32bppArgb)]
    [InlineData(PixelFormat.Format24bppRgb)]
    public void AddPadding_PreservesPixelFormat(PixelFormat format)
    {
        using Bitmap original = new(4, 4, format);
        using Bitmap padded = ImageProcessor.AddPadding(original, 2);

        padded.PixelFormat.Should().Be(format);
    }

    [Fact]
    public void AddPadding_PaddingAreaIsWhite_AndOriginalIsCentered()
    {
        using Bitmap original = new(10, 10, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(original))
            g.Clear(Color.Red);

        const int padding = 3;
        using Bitmap padded = ImageProcessor.AddPadding(original, padding);

        // Corners sit deep inside the white border.
        Argb(padded.GetPixel(0, 0)).Should().Be(Argb(Color.White));
        Argb(padded.GetPixel(padded.Width - 1, padded.Height - 1)).Should().Be(Argb(Color.White));
        // Centre of the drawn region keeps the source colour (probe far from the blended boundary).
        Argb(padded.GetPixel(padding + 5, padding + 5)).Should().Be(Argb(Color.Red));
    }
}
