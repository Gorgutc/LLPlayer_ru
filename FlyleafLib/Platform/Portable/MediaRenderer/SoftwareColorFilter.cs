#nullable enable

using System.Numerics;
using System.Runtime.CompilerServices;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// F-13 portable renderer: the Flyleaf colour filters (brightness / contrast / hue / saturation) applied in place on
/// BGRA32 frames, with the math of the Windows FlyleafVP pixel shader (ShaderCompiler.PS.cs, <c>dFilters</c>):
/// <code>
/// YUV sources: Y' = lerp(Y, pow(Y, 2 - contrast), smoothstep(0, 1, Y))   (before YUV -> RGB; only when contrast != 1)
/// RGB / gray:  c  = (c - 0.5) * (2 - contrast) + 0.5
/// then:        c += brightness;  c = Hue(c, hue);  c = lerp(dot(c, BT.709 luma), c, saturation);  saturate(c)
/// </code>
/// swscale's own brightness/contrast/saturation (sws_setColorspaceDetails) do not follow these formulas (and it has no
/// hue), so the filters are applied by a vectorized managed pass after the conversion. All steps after the luma curve
/// are affine in RGB and are folded into one 3x3 matrix + offset; the YUV luma curve is recovered from the converted
/// RGB (the Y of the source matrix is the luma of its RGB output) and applied through a lookup table.
/// Instances are immutable; <see cref="Create"/> returns null when every filter is at its default (zero cost).
/// </summary>
internal sealed class SoftwareColorFilter
{
    // Hue matrices of the pixel shader (rows)
    static readonly float[] HueBase = [0.299f, 0.587f, 0.114f,  0.299f, 0.587f, 0.114f,  0.299f, 0.587f, 0.114f];
    static readonly float[] HueCos  = [0.701f, -0.587f, -0.114f,  -0.299f, 0.413f, -0.114f,  -0.300f, -0.588f, 0.886f];
    static readonly float[] HueSin  = [0.168f, 0.330f, -0.497f,  -0.328f, 0.035f, 0.292f,  1.250f, -1.050f, -0.203f];

    const float Bt709Kr = 0.2126f, Bt709Kg = 0.7152f, Bt709Kb = 0.0722f;
    const int   LutScale = 4;       // luma (0..255) steps of 1/4
    const int   LutSize  = 256 * LutScale;

    /// <summary>Shader-unit values (brightness -0.5..0.5, contrast 0..2, hue -3.14..3.14 rad, saturation 0..2).</summary>
    public float Brightness     { get; }
    public float Contrast       { get; }
    public float Hue            { get; }
    public float Saturation     { get; }

    /// <summary>True when the YUV luma contrast curve is used (YUV source and contrast != 1).</summary>
    public bool  UsesLumaCurve  => lumaDelta != null;

    // out = M * rgb + (delta(luma) + offset) * u   (RGB order, 0..255 units)
    readonly float m00, m01, m02, m10, m11, m12, m20, m21, m22;
    readonly float u0, u1, u2;
    readonly float offset;

    // YUV luma curve: delta (0..255 RGB units) indexed by round(luma * LutScale); luma weights of the source matrix
    readonly float[]? lumaDelta;
    readonly float kr, kg, kb;

    SoftwareColorFilter(float brightness, float contrast, float hue, float saturation, ColorType colorType, ColorSpace colorSpace, ColorRange colorRange)
    {
        Brightness  = brightness;
        Contrast    = contrast;
        Hue         = hue;
        Saturation  = saturation;

        // SH = Saturation * Hue (3x3)
        float[] h = new float[9];
        if (hue == 0)
            h[0] = h[4] = h[8] = 1;
        else
        {
            float c = MathF.Cos(hue), s = -MathF.Sin(hue);
            for (int i = 0; i < 9; i++)
                h[i] = HueBase[i] + c * HueCos[i] + s * HueSin[i];
        }

        float[] sat = new float[9];
        float[] lum = [Bt709Kr, Bt709Kg, Bt709Kb];
        for (int r = 0; r < 3; r++)
            for (int col = 0; col < 3; col++)
                sat[r * 3 + col] = (1 - saturation) * lum[col] + (r == col ? saturation : 0);

        float[] sh = new float[9];
        for (int r = 0; r < 3; r++)
            for (int col = 0; col < 3; col++)
                sh[r * 3 + col] = sat[r * 3] * h[col] + sat[r * 3 + 1] * h[3 + col] + sat[r * 3 + 2] * h[6 + col];

        bool yuv = colorType == ColorType.YUV;
        float k  = yuv ? 1 : 2 - contrast;  // RGB/gray contrast (YUV contrast is the luma curve)

        m00 = k * sh[0]; m01 = k * sh[1]; m02 = k * sh[2];
        m10 = k * sh[3]; m11 = k * sh[4]; m12 = k * sh[5];
        m20 = k * sh[6]; m21 = k * sh[7]; m22 = k * sh[8];

        u0 = sh[0] + sh[1] + sh[2];
        u1 = sh[3] + sh[4] + sh[5];
        u2 = sh[6] + sh[7] + sh[8];

        offset = 255f * ((yuv ? 0 : 0.5f - 0.5f * k) + brightness);

        if (yuv && contrast != 1)
        {
            (kr, kb) = colorSpace switch
            {
                ColorSpace.Bt709    => (0.2126f, 0.0722f),
                ColorSpace.Bt2020   => (0.2627f, 0.0593f),
                _                   => (0.299f,  0.114f),
            };
            kg = 1 - kr - kb;

            bool    limited = colorRange != ColorRange.Full;
            float   yScale  = limited ? 1.16438356f : 1f;   // first column of the shader's YUV -> RGB matrices
            float   yOffset = limited ? 0.0625f : 0f;
            float   e       = 2 - contrast;

            lumaDelta = new float[LutSize];
            for (int i = 0; i < LutSize; i++)
            {
                float y     = (i / (float)LutScale / 255f) / yScale + yOffset; // shader Y (0..1) that produced this luma
                float t     = Math.Clamp(y, 0, 1);
                float ss    = t * t * (3 - 2 * t);                          // smoothstep(0, 1, y)
                float curve = y + (MathF.Pow(Math.Max(y, 0), e) - y) * ss;  // lerp(y, pow(y, e), ss)
                lumaDelta[i] = (curve - y) * yScale * 255f;
            }
        }
    }

    /// <summary>
    /// Builds the filter from shader-unit values, or returns null when all of them are at their defaults
    /// (brightness 0, contrast 1, hue 0, saturation 1).
    /// </summary>
    public static SoftwareColorFilter? Create(float brightness, float contrast, float hue, float saturation, ColorType colorType, ColorSpace colorSpace, ColorRange colorRange)
    {
        if (brightness == 0 && contrast == 1 && hue == 0 && saturation == 1)
            return null;

        return new(brightness, contrast, hue, saturation, colorType, colorSpace, colorRange);
    }

    /// <summary>Applies the filters in place on <paramref name="height"/> rows of <paramref name="width"/> BGRA pixels.</summary>
    public unsafe void Apply(byte* bgra, int stride, int width, int height)
    {
        for (int y = 0; y < height; y++)
            ApplyRow((uint*)(bgra + (long)y * stride), width);
    }

    /// <summary>Span overload of <see cref="Apply(byte*, int, int, int)"/> (bounds checked).</summary>
    public unsafe void Apply(Span<byte> bgra, int stride, int width, int height)
    {
        BgraPixelOps.CheckImage(bgra.Length, stride, width, height);

        fixed (byte* p = bgra)
            Apply(p, stride, width, height);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)] // per-frame hot loop: skip tier-0
    unsafe void ApplyRow(uint* px, int width)
    {
        int x = 0;

        if (Vector.IsHardwareAccelerated && width >= Vector<uint>.Count)
        {
            int n = Vector<uint>.Count;
            Vector<uint>    mask    = new(0xFF);
            Vector<uint>    alpha   = new(0xFF000000);
            Vector<float>   zero    = Vector<float>.Zero;
            Vector<float>   max     = new(255f);
            Vector<float>   half    = new(0.5f);
            Vector<float>   off     = new(offset);
            Span<float>     deltas  = stackalloc float[Vector<float>.Count];

            for (; x <= width - n; x += n)
            {
                var v = *(Vector<uint>*)(px + x);
                var b = Vector.ConvertToSingle(Vector.AsVectorInt32(v & mask));
                var g = Vector.ConvertToSingle(Vector.AsVectorInt32(Vector.ShiftRightLogical(v, 8)  & mask));
                var r = Vector.ConvertToSingle(Vector.AsVectorInt32(Vector.ShiftRightLogical(v, 16) & mask));

                Vector<float> s = off;
                if (lumaDelta != null)
                {
                    var luma = (r * kr + g * kg + b * kb) * LutScale + half;
                    var idx  = Vector.ConvertToInt32(luma);
                    for (int i = 0; i < n; i++)
                        deltas[i] = lumaDelta[Math.Clamp(idx[i], 0, LutSize - 1)];
                    s += new Vector<float>(deltas);
                }

                var ro = Vector.Min(Vector.Max(r * m00 + g * m01 + b * m02 + s * u0, zero), max) + half;
                var go = Vector.Min(Vector.Max(r * m10 + g * m11 + b * m12 + s * u1, zero), max) + half;
                var bo = Vector.Min(Vector.Max(r * m20 + g * m21 + b * m22 + s * u2, zero), max) + half;

                var outV =
                    Vector.AsVectorUInt32(Vector.ConvertToInt32(bo)) |
                    Vector.ShiftLeft(Vector.AsVectorUInt32(Vector.ConvertToInt32(go)), 8) |
                    Vector.ShiftLeft(Vector.AsVectorUInt32(Vector.ConvertToInt32(ro)), 16) |
                    (v & alpha);

                *(Vector<uint>*)(px + x) = outV;
            }
        }

        for (; x < width; x++)
            px[x] = ApplyPixel(px[x]);
    }

    /// <summary>Scalar path (row tails, no SIMD): same math as the vector path.</summary>
    internal uint ApplyPixel(uint p)
    {
        float b = p & 0xFF, g = (p >> 8) & 0xFF, r = (p >> 16) & 0xFF;

        float s = offset;
        if (lumaDelta != null)
            s += lumaDelta[Math.Clamp((int)((r * kr + g * kg + b * kb) * LutScale + 0.5f), 0, LutSize - 1)];

        uint ro = (uint)(Math.Clamp(r * m00 + g * m01 + b * m02 + s * u0, 0, 255) + 0.5f);
        uint go = (uint)(Math.Clamp(r * m10 + g * m11 + b * m12 + s * u1, 0, 255) + 0.5f);
        uint bo = (uint)(Math.Clamp(r * m20 + g * m21 + b * m22 + s * u2, 0, 255) + 0.5f);

        return (p & 0xFF000000) | (ro << 16) | (go << 8) | bo;
    }
}
