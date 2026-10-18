using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafLib.Platform;

// F-13 portable software renderer: pure (no FFmpeg) tests of the presentation pipeline math — orientation
// (rotation / mirroring), presentation / snapshot sizes, and the Flyleaf colour filters against a direct transcription
// of the Windows pixel shader (FlyleafLib/MediaFramework/MediaRenderer/ShaderCompiler.PS.cs).
public class PortableVideoPipelineUnitTests
{
    #region Orientation
    public static TheoryData<uint, bool, bool> AllOrientations()
    {
        TheoryData<uint, bool, bool> data = [];
        foreach (uint rotation in new uint[] { 0, 90, 180, 270 })
            foreach (bool h in new[] { false, true })
                foreach (bool v in new[] { false, true })
                    data.Add(rotation, h, v);
        return data;
    }

    // Naive reference of the Windows vertex shader: mirror in the frame's own orientation, then rotate clockwise.
    internal static int[,] Reference(int[,] img, uint rotation, bool hflip, bool vflip)
    {
        int h = img.GetLength(0), w = img.GetLength(1);
        var f = new int[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                f[y, x] = img[vflip ? h - 1 - y : y, hflip ? w - 1 - x : x];

        for (uint r = 0; r < rotation / 90; r++)
        {   // 90 degrees clockwise: out(x, y) = in(y, H-1-x), out is H wide and W high
            int ih = f.GetLength(0), iw = f.GetLength(1);
            var o = new int[iw, ih];
            for (int y = 0; y < iw; y++)
                for (int x = 0; x < ih; x++)
                    o[y, x] = f[ih - 1 - x, y];
            f = o;
        }

        return f;
    }

    static int[,] Numbered(int w, int h)
    {
        var img = new int[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[y, x] = y * 1000 + x + 1;
        return img;
    }

    [Theory]
    [MemberData(nameof(AllOrientations))]
    public void VideoTransform_MapToSource_MatchesFlipThenClockwiseRotation(uint rotation, bool hflip, bool vflip)
    {
        const int W = 5, H = 3;
        var src = Numbered(W, H);
        var expected = Reference(src, rotation, hflip, vflip);
        var t = VideoTransform.From(rotation, hflip, vflip);

        var (ow, oh) = t.OutputSize(W, H);
        ow.Should().Be(expected.GetLength(1));
        oh.Should().Be(expected.GetLength(0));

        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                var (sx, sy) = t.MapToSource(x, y, W, H);
                src[sy, sx].Should().Be(expected[y, x], $"output ({x},{y}) of rotation {rotation} h {hflip} v {vflip}");
            }
    }

    [Theory]
    [MemberData(nameof(AllOrientations))]
    public void BgraPixelOps_SourceVFlipPlusPass_ProducesTheReferenceOrientation(uint rotation, bool hflip, bool vflip)
    {
        // Odd sizes larger than the 16x16 transpose tile, strides with padding
        const int W = 37, H = 21;
        var src = Numbered(W, H);
        var expected = Reference(src, rotation, hflip, vflip);
        var t = VideoTransform.From(rotation, hflip, vflip);

        // What swscale writes: the source, rows reversed when the transform's vertical mirror is done on its input
        int srcStride = W * 4 + 12;
        byte[] converted = new byte[srcStride * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                BitConverter.TryWriteBytes(converted.AsSpan(y * srcStride + x * 4), src[t.SourceVFlip ? H - 1 - y : y, x]);

        var (ow, oh) = t.OutputSize(W, H);
        int dstStride = ow * 4 + 8;
        byte[] output;
        if (t.NeedsPass)
        {
            output = new byte[dstStride * oh];
            BgraPixelOps.ApplyPass(t, converted, srcStride, W, H, output, dstStride);
        }
        else
        {
            output = converted;
            dstStride = srcStride;
        }

        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
                BitConverter.ToInt32(output, y * dstStride + x * 4).Should().Be(expected[y, x], $"output ({x},{y})");
    }

    [Fact]
    public void VideoTransform_IdentityNeedsNoPassAndOnlyVerticalMirrorIsFree()
    {
        VideoTransform.From(0, false, false).IsIdentity.Should().BeTrue();
        VideoTransform.From(360, false, false).IsIdentity.Should().BeTrue();

        var v = VideoTransform.From(0, false, true);
        v.NeedsPass.Should().BeFalse("a vertical mirror is done by swscale reading the source bottom-up");
        v.SourceVFlip.Should().BeTrue();

        VideoTransform.From(0, true, false).NeedsPass.Should().BeTrue();
        VideoTransform.From(90, false, false).Transpose.Should().BeTrue();
        VideoTransform.From(180, false, false).Transpose.Should().BeFalse();
        VideoTransform.From(180, true, true).IsIdentity.Should().BeTrue("mirroring both axes is a 180 degree rotation");
    }
    #endregion

    #region Sizes
    [Theory]
    [InlineData(1280, 720, 640, 360, 640, 360)]     // downscale to the viewport
    [InlineData(1280, 720, 1920, 1080, 1280, 720)]  // never upscale
    [InlineData(1280, 720, 0, 0, 1280, 720)]        // no host control yet: native
    [InlineData(1280, 720, 5000, 360, 1280, 360)]   // zoomed / stretched: each axis capped independently
    [InlineData(720, 1280, 202.5, 360, 203, 360)]   // rotated portrait frame, fractional viewport rounds up
    [InlineData(1280, 720, 0.4, 100, 1280, 720)]    // degenerate viewport: native
    public void PresentationSize_IsMinOfNativeAndViewport(int w, int h, double vw, double vh, int ew, int eh)
        => SoftwareVideoGeometry.PresentationSize(w, h, vw, vh).Should().Be((ew, eh));

    [Fact]
    public void PresentationSize_NaNViewport_KeepsNative()
        => SoftwareVideoGeometry.PresentationSize(640, 360, double.NaN, 100).Should().Be((640, 360));

    [Theory]
    [InlineData(1280, 720, 0u, 0u, 1280, 720)]
    [InlineData(1280, 720, 320u, 0u, 320, 180)]
    [InlineData(1280, 720, 0u, 180u, 320, 180)]
    [InlineData(1280, 720, 333u, 101u, 332, 100)]   // even sizes (encoders)
    [InlineData(180, 320, 0u, 0u, 180, 320)]        // upright (rotated) native size
    [InlineData(1280, 720, 1u, 1u, 2, 2)]
    public void SnapshotSize_KeepsStage1Semantics(int w, int h, uint rw, uint rh, int ew, int eh)
        => SoftwareVideoGeometry.SnapshotSize(w, h, rw, rh).Should().Be((ew, eh));

    [Fact]
    public void ClampCrop_LeavesAtLeastOnePixel()
    {
        var c = SoftwareVideoGeometry.ClampCrop(new(top: 500, left: 900, bottom: 500, right: 900), 100, 50);
        (100 - (int)c.Width).Should().BeGreaterThanOrEqualTo(1);
        (50 - (int)c.Height).Should().BeGreaterThanOrEqualTo(1);

        SoftwareVideoGeometry.ClampCrop(new(2, 4, 6, 8), 100, 50).Should().Be(new CropRect(2, 4, 6, 8));
    }
    #endregion

    #region Colour filters
    // Direct transcription of the pixel shader (normalized values), independent of SoftwareColorFilter's folding.
    static readonly float[,] HueBase = { { 0.299f, 0.587f, 0.114f }, { 0.299f, 0.587f, 0.114f }, { 0.299f, 0.587f, 0.114f } };
    static readonly float[,] HueCos  = { { 0.701f, -0.587f, -0.114f }, { -0.299f, 0.413f, -0.114f }, { -0.300f, -0.588f, 0.886f } };
    static readonly float[,] HueSin  = { { 0.168f, 0.330f, -0.497f }, { -0.328f, 0.035f, 0.292f }, { 1.250f, -1.050f, -0.203f } };
    static readonly float[,] Bt709Limited = { { 1.16438356f, 0f, 1.79274107f }, { 1.16438356f, -0.21324861f, -0.53290933f }, { 1.16438356f, 2.11240179f, 0f } };

    internal static float[] ShaderTail(float[] c, float brightness, float hue, float saturation)
    {
        for (int i = 0; i < 3; i++)
            c[i] += brightness;

        if (hue != 0)
        {
            float cs = MathF.Cos(hue), sn = -MathF.Sin(hue);
            float[] o = new float[3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    o[i] += (HueBase[i, j] + cs * HueCos[i, j] + sn * HueSin[i, j]) * c[j];
            c = o;
        }

        if (saturation != 1)
        {
            float lum = c[0] * 0.2126f + c[1] * 0.7152f + c[2] * 0.0722f;
            for (int i = 0; i < 3; i++)
                c[i] = lum + (c[i] - lum) * saturation;
        }

        for (int i = 0; i < 3; i++)
            c[i] = Math.Clamp(c[i], 0, 1);

        return c;
    }

    static float[] ShaderRgb(float r, float g, float b, float brightness, float contrast, float hue, float saturation)
    {
        float[] c = [r, g, b];
        for (int i = 0; i < 3; i++)
            c[i] = (c[i] - 0.5f) * (2 - contrast) + 0.5f;
        return ShaderTail(c, brightness, hue, saturation);
    }

    static float[] YuvToRgb709Limited(float y, float u, float v)
    {
        float[] yuv = [y - 0.0625f, u - 0.5f, v - 0.5f];
        float[] c = new float[3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                c[i] += Bt709Limited[i, j] * yuv[j];
        return c;
    }

    static float[] ShaderYuv(float y, float u, float v, float brightness, float contrast, float hue, float saturation)
    {
        if (contrast != 1)
        {
            float t = Math.Clamp(y, 0, 1), ss = t * t * (3 - 2 * t);
            y = y + (MathF.Pow(y, 2 - contrast) - y) * ss;
        }
        return ShaderTail(YuvToRgb709Limited(y, u, v), brightness, hue, saturation);
    }

    static uint Bgra(int r, int g, int b) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;

    public static TheoryData<float, float, float, float> FilterSettings() => new()
    {
        { 0.25f, 1f, 0f, 1f },          // brightness +50
        { -0.4f, 1f, 0f, 1f },          // brightness -80
        { 0f, 1.5f, 0f, 1f },           // contrast +50
        { 0f, 0.3f, 0f, 1f },           // contrast -70
        { 0f, 1f, 1.57f, 1f },          // hue +90 deg
        { 0f, 1f, -3.14f, 1f },         // hue -180 deg
        { 0f, 1f, 0f, 0f },             // saturation -100 (grey)
        { 0f, 1f, 0f, 1.8f },           // saturation +80
        { 0.1f, 1.4f, 0.7f, 1.3f },     // all at once
    };

    [Fact]
    public void ColorFilter_AllDefaults_IsNull()
        => SoftwareColorFilter.Create(0, 1, 0, 1, ColorType.YUV, ColorSpace.Bt709, ColorRange.Limited).Should().BeNull("defaults cost nothing");

    [Theory]
    [MemberData(nameof(FilterSettings))]
    public void ColorFilter_RgbSource_MatchesPixelShader(float brightness, float contrast, float hue, float saturation)
    {
        var filter = SoftwareColorFilter.Create(brightness, contrast, hue, saturation, ColorType.RGB, ColorSpace.None, ColorRange.Full)!;
        filter.UsesLumaCurve.Should().BeFalse();

        Random rnd = new(1234);
        for (int i = 0; i < 500; i++)
        {
            int r = rnd.Next(256), g = rnd.Next(256), b = rnd.Next(256);
            uint outPx = filter.ApplyPixel(Bgra(r, g, b));
            var exp = ShaderRgb(r / 255f, g / 255f, b / 255f, brightness, contrast, hue, saturation);

            ((int)((outPx >> 16) & 0xFF)).Should().BeCloseTo((int)MathF.Round(exp[0] * 255), 1, $"R of ({r},{g},{b})");
            ((int)((outPx >> 8)  & 0xFF)).Should().BeCloseTo((int)MathF.Round(exp[1] * 255), 1, $"G of ({r},{g},{b})");
            ((int)( outPx        & 0xFF)).Should().BeCloseTo((int)MathF.Round(exp[2] * 255), 1, $"B of ({r},{g},{b})");
            (outPx >> 24).Should().Be(0xFFu, "alpha is kept");
        }
    }

    [Theory]
    [MemberData(nameof(FilterSettings))]
    public void ColorFilter_YuvSource_MatchesPixelShaderIncludingLumaContrastCurve(float brightness, float contrast, float hue, float saturation)
    {
        var filter = SoftwareColorFilter.Create(brightness, contrast, hue, saturation, ColorType.YUV, ColorSpace.Bt709, ColorRange.Limited)!;
        filter.UsesLumaCurve.Should().Be(contrast != 1);

        Random rnd = new(4321);
        int checkedPixels = 0;
        while (checkedPixels < 400)
        {
            // 8-bit limited-range YUV sample -> what swscale hands the filter (full-range 8-bit RGB, no filters)
            float y = rnd.Next(16, 236) / 255f, u = rnd.Next(16, 241) / 255f, v = rnd.Next(16, 241) / 255f;
            var rgb = YuvToRgb709Limited(y, u, v);
            if (rgb.Any(c => c < 0 || c > 1))
                continue; // out of gamut: swscale clamps before the filter (the shader clamps after)

            checkedPixels++;
            uint outPx = filter.ApplyPixel(Bgra((int)MathF.Round(rgb[0] * 255), (int)MathF.Round(rgb[1] * 255), (int)MathF.Round(rgb[2] * 255)));
            var exp = ShaderYuv(y, u, v, brightness, contrast, hue, saturation);

            ((int)((outPx >> 16) & 0xFF)).Should().BeCloseTo((int)MathF.Round(exp[0] * 255), 3, $"R of YUV ({y},{u},{v})");
            ((int)((outPx >> 8)  & 0xFF)).Should().BeCloseTo((int)MathF.Round(exp[1] * 255), 3, $"G of YUV ({y},{u},{v})");
            ((int)( outPx        & 0xFF)).Should().BeCloseTo((int)MathF.Round(exp[2] * 255), 3, $"B of YUV ({y},{u},{v})");
        }
    }

    [Fact]
    public void ColorFilter_VectorPath_EqualsScalarPath_AndDoesNotAllocate()
    {
        var filter = SoftwareColorFilter.Create(0.1f, 1.4f, 0.7f, 1.3f, ColorType.YUV, ColorSpace.Bt709, ColorRange.Limited)!;

        const int W = 37, H = 5, Stride = W * 4 + 20; // row length not a multiple of the vector width
        byte[] img = new byte[Stride * H];
        new Random(7).NextBytes(img);
        uint[] expected = new uint[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                expected[y * W + x] = filter.ApplyPixel(BitConverter.ToUInt32(img, y * Stride + x * 4));

        filter.Apply(img.AsSpan(), Stride, W, H); // warm-up (JIT) + result

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                BitConverter.ToUInt32(img, y * Stride + x * 4).Should().Be(expected[y * W + x], $"pixel ({x},{y})");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            filter.Apply(img.AsSpan(), Stride, W, H);
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0, "the per-frame filter pass must not allocate");
    }

    [Fact]
    public void ColorFilter_SaturationZero_GivesBt709Grey()
    {
        var filter = SoftwareColorFilter.Create(0, 1, 0, 0, ColorType.RGB, ColorSpace.None, ColorRange.Full)!;
        uint px = filter.ApplyPixel(Bgra(200, 40, 40));
        int lum = (int)MathF.Round(0.2126f * 200 + 0.7152f * 40 + 0.0722f * 40);

        ((int)((px >> 16) & 0xFF)).Should().BeCloseTo(lum, 1);
        ((int)((px >> 8)  & 0xFF)).Should().BeCloseTo(lum, 1);
        ((int)( px        & 0xFF)).Should().BeCloseTo(lum, 1);
    }
    #endregion
}
