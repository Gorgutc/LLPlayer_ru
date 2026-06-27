using AwesomeAssertions;

namespace FlyleafLib;

public class AsrSilenceTests
{
    private static byte[] Pcm(params short[] samples)
    {
        byte[] buf = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            buf[i * 2] = (byte)(samples[i] & 0xFF);
            buf[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }
        return buf;
    }

    [Fact]
    public void Rms_AllZero_IsZero()
    {
        byte[] buf = Pcm(0, 0, 0, 0);
        AsrSilence.Rms(buf, buf.Length).Should().Be(0.0);
    }

    [Fact]
    public void Rms_FullScaleSquareWave_IsAboutOne()
    {
        byte[] buf = Pcm(short.MaxValue, short.MinValue, short.MaxValue, short.MinValue);
        AsrSilence.Rms(buf, buf.Length).Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Rms_HalfScale_IsAboutHalf()
    {
        const short half = 16384;
        byte[] buf = Pcm(half, -half, half, -half);
        AsrSilence.Rms(buf, buf.Length).Should().BeApproximately(0.5, 0.001);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)] // fewer than one whole sample
    public void Rms_TooShort_IsZeroNoThrow(int byteCount)
    {
        byte[] buf = new byte[2];
        AsrSilence.Rms(buf, byteCount).Should().Be(0.0);
    }

    [Fact]
    public void Rms_OddByteCount_IgnoresTrailingByteNoThrow()
    {
        byte[] buf = Pcm(short.MaxValue, short.MinValue); // 4 bytes
        // an odd count drops the trailing byte, leaving one whole full-scale sample
        AsrSilence.Rms(buf, 3).Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Rms_ByteCountLargerThanSpan_ClampedNoOverread()
    {
        byte[] buf = Pcm(short.MaxValue, short.MinValue);
        AsrSilence.Rms(buf, 9999).Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void IsSilent_BelowThreshold_True()
    {
        byte[] quiet = Pcm(10, -10, 10, -10); // rms ≈ 10/32768 ≈ 0.0003
        AsrSilence.IsSilent(quiet, quiet.Length, 0.01).Should().BeTrue();
    }

    [Fact]
    public void IsSilent_AboveThreshold_False()
    {
        byte[] loud = Pcm(16384, -16384); // rms ≈ 0.5
        AsrSilence.IsSilent(loud, loud.Length, 0.01).Should().BeFalse();
    }

    [Fact]
    public void IsSilent_NonPositiveThreshold_NeverSilent()
    {
        byte[] zero = Pcm(0, 0);
        AsrSilence.IsSilent(zero, zero.Length, 0.0).Should().BeFalse();
    }

    [Fact]
    public void IsSilent_NoSamples_NotSilent()
    {
        // a zero-output resample frame contributed no audio; "no samples" must not count as a silent boundary
        AsrSilence.IsSilent(new byte[2], 0, 0.01).Should().BeFalse();
    }

    [Fact]
    public void IsSilent_RealZeroValuedSamples_IsSilent()
    {
        // genuine digital silence (zero-valued samples) IS a silent boundary
        byte[] zero = Pcm(0, 0, 0, 0);
        AsrSilence.IsSilent(zero, zero.Length, 0.01).Should().BeTrue();
    }

    [Theory]
    [InlineData(0L, false)]    // nothing accumulated
    [InlineData(599L, false)]  // just under 60% of 1000
    [InlineData(600L, true)]   // at 60%
    [InlineData(1000L, true)]  // at the hard cap
    public void IsSoftReady_BySize(long dataBytes, bool expected)
    {
        AsrSilence.IsSoftReady(dataBytes, 1000, TimeSpan.Zero, TimeSpan.FromSeconds(20), 0.6)
            .Should().Be(expected);
    }

    [Fact]
    public void IsSoftReady_ByElapsed()
    {
        // size axis not ready, but elapsed crossed 60% of 20s = 12s
        AsrSilence.IsSoftReady(0, 1000, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(20), 0.6)
            .Should().BeTrue();
    }

    [Fact]
    public void IsSoftReady_ZeroFraction_AlwaysReady()
    {
        AsrSilence.IsSoftReady(0, 1000, TimeSpan.Zero, TimeSpan.FromSeconds(20), 0.0)
            .Should().BeTrue();
    }

    [Fact]
    public void IsSoftReady_FullFraction_OnlyAtHardCap()
    {
        AsrSilence.IsSoftReady(999, 1000, TimeSpan.Zero, TimeSpan.FromSeconds(20), 1.0)
            .Should().BeFalse();
        AsrSilence.IsSoftReady(1000, 1000, TimeSpan.Zero, TimeSpan.FromSeconds(20), 1.0)
            .Should().BeTrue();
    }
}
