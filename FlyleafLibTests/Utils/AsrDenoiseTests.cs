using System.Buffers.Binary;
using AwesomeAssertions;

namespace FlyleafLib;

// Tests for the F-02 managed denoise core (AsrHighPassFilter) and the afftdn args builder. The native afftdn
// avfilter stage is not unit-tested (needs a live FFmpeg graph) and is covered by .exe smoke instead.
public class AsrDenoiseTests
{
    private const int SampleRate = 16000;

    private static byte[] Sine(double freq, double amp, int samples)
    {
        var buf = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            double v = amp * Math.Sin(2.0 * Math.PI * freq * i / SampleRate);
            int s = (int)Math.Round(v);
            BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(i * 2, 2), (short)Math.Clamp(s, short.MinValue, short.MaxValue));
        }
        return buf;
    }

    [Fact]
    public void HighPass_RemovesDcOffset()
    {
        // A constant (DC) signal must decay to ~0 — a high-pass blocks DC entirely in steady state.
        var buf = new byte[2000 * 2];
        for (int i = 0; i < 2000; i++)
            BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(i * 2, 2), (short)8000);

        new AsrHighPassFilter(SampleRate).ProcessInPlace(buf, buf.Length);

        short last = BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(buf.Length - 2, 2));
        Math.Abs((int)last).Should().BeLessThan(5);
    }

    [Fact]
    public void HighPass_AttenuatesLowFreq_PassesHighFreq()
    {
        // 4 kHz is well above the 80 Hz cutoff -> passes almost unchanged; 20 Hz is well below -> heavily attenuated.
        var high = Sine(4000, 10000, 3200);
        var low = Sine(20, 10000, 3200);

        double highIn = AsrSilence.Rms(high, high.Length);
        double lowIn = AsrSilence.Rms(low, low.Length);

        new AsrHighPassFilter(SampleRate).ProcessInPlace(high, high.Length);
        new AsrHighPassFilter(SampleRate).ProcessInPlace(low, low.Length);

        double highRatio = AsrSilence.Rms(high, high.Length) / highIn;
        double lowRatio = AsrSilence.Rms(low, low.Length) / lowIn;

        highRatio.Should().BeGreaterThan(0.85);
        lowRatio.Should().BeLessThan(0.3);
        lowRatio.Should().BeLessThan(highRatio); // and strictly weaker than the high-band
    }

    [Fact]
    public void HighPass_StateContinuity_ChunkedEqualsWhole()
    {
        // Filtering frame-by-frame (carrying state) must equal filtering the whole buffer in one call.
        var whole = Sine(1000, 8000, 400);
        var chunked = (byte[])whole.Clone();

        new AsrHighPassFilter(SampleRate).ProcessInPlace(whole, whole.Length);

        var f = new AsrHighPassFilter(SampleRate);
        f.ProcessInPlace(chunked.AsSpan(0, 400), 400);   // first 200 samples
        f.ProcessInPlace(chunked.AsSpan(400, 400), 400); // next 200 samples, state carried

        chunked.Should().Equal(whole);
    }

    [Fact]
    public void HighPass_Reset_ClearsHistory()
    {
        var original = Sine(1000, 8000, 400);
        var viaReset = (byte[])original.Clone();
        var viaFresh = (byte[])original.Clone();

        var f = new AsrHighPassFilter(SampleRate);
        f.ProcessInPlace((byte[])original.Clone(), original.Length); // dirty the filter state with a throwaway buffer
        f.Reset();
        f.ProcessInPlace(viaReset, viaReset.Length); // after reset, must match a brand-new filter

        new AsrHighPassFilter(SampleRate).ProcessInPlace(viaFresh, viaFresh.Length);

        viaReset.Should().Equal(viaFresh);
    }

    [Fact]
    public void HighPass_EmptyOrSubSample_NoOp()
    {
        var f = new AsrHighPassFilter(SampleRate);

        f.ProcessInPlace(Array.Empty<byte>(), 0); // must not throw
        var one = new byte[] { 0x12 };
        f.ProcessInPlace(one, 1);
        one[0].Should().Be(0x12); // a lone trailing byte is untouched
    }

    [Fact]
    public void HighPass_OutOfRangeCutoff_DoesNotThrow()
    {
        var buf = Sine(1000, 8000, 100);
        Action act = () =>
        {
            new AsrHighPassFilter(SampleRate, -5).ProcessInPlace(buf, buf.Length);
            new AsrHighPassFilter(SampleRate, 999999).ProcessInPlace(buf, buf.Length);
            new AsrHighPassFilter(0, 80).ProcessInPlace(buf, buf.Length);
        };
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(12.0, "nr=12")]
    [InlineData(6.5, "nr=6.5")]
    [InlineData(0.0, "nr=0.01")]   // clamped up to the filter minimum
    [InlineData(500.0, "nr=97")]   // clamped down to the filter maximum
    public void BuildAfftdnArgs_ClampsAndFormatsInvariant(double nr, string expected)
    {
        AsrDenoise.BuildAfftdnArgs(nr).Should().Be(expected);
    }
}
