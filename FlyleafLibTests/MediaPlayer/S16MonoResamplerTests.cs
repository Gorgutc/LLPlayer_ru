using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer;

// HC-44 slice 1: the swr → S16 mono resample block that was copied byte-for-byte into AudioReader.ResampleTo (ASR)
// and WaveformReader.ResampleFrame (F-12 waveform) now lives once in S16MonoResampler. The native swr_convert itself
// is exercised by owner manual-smoke (ASR transcription + waveform render), exactly as before — but the three pure
// seams the resampler is built from (output-capacity math, codec-change guard, buffer growth) are pinned here so a
// future edit to the single shared copy can't silently drift the sizing/reinit behavior both callers depend on.
public class S16MonoResamplerTests
{
    // --- ComputeOutputSampleCapacity: nOut = (int)(nb * target/in) + 32, plus swr's delay clamped to [.., max(4096,nOut)] ---

    [Fact]
    public void OutputCapacity_NoDelay_IsResampledCountPlusPad()
    {
        // 1024 in-samples 44100→16000: 1024 * (16000/44100) = 371.5 → truncated 371, + 32 pad.
        S16MonoResampler.ComputeOutputSampleCapacity(1024, 44100, 16000, delay: 0).Should().Be(403);
    }

    [Fact]
    public void OutputCapacity_IdentityRate_IsNbPlusPad()
    {
        // Same in/out rate → ratio 1.0, no resampling: exactly nb + 32.
        S16MonoResampler.ComputeOutputSampleCapacity(1600, 16000, 16000, delay: 0).Should().Be(1632);
    }

    [Theory]
    [InlineData(0, 403)]     // no delay → base only
    [InlineData(-1, 403)]    // non-positive delay is ignored (guard is delay > 0)
    [InlineData(100, 503)]   // small delay added verbatim (100 < max(4096,403))
    [InlineData(5000, 4499)] // large delay clamped to max(4096, base=403) = 4096 → 403 + 4096
    public void OutputCapacity_AppliesDelayClamp(long delay, int expected)
    {
        S16MonoResampler.ComputeOutputSampleCapacity(1024, 44100, 16000, delay).Should().Be(expected);
    }

    [Fact]
    public void OutputCapacity_DelayClampFloorIsNOutWhenNOutExceeds4096()
    {
        // Upsample so base nOut > 4096: 48000 * (48000/16000) = 144000, + 32 = 144032. The clamp ceiling is
        // max(4096, 144032) = 144032, so a delay of 200000 is capped at nOut, not at the 4096 floor: 144032 + 144032.
        S16MonoResampler.ComputeOutputSampleCapacity(48000, 16000, 48000, delay: 200000).Should().Be(288064);
    }

    // --- DetectCodecChange: rebuild the SwrContext whenever format/sample-rate/channel-layout differs from last frame ---

    [Fact]
    public void CodecChange_FirstFrame_IsAlwaysAChange()
    {
        using var r = new S16MonoResampler();
        // Seeds are -1/-1/0, so any real first frame reports a change (and Resample also allocates because the
        // context is null — the seed never affects output, only the very first change flag).
        r.DetectCodecChange(format: 8, sampleRate: 44100, channelLayout: 3).Should().BeTrue();
    }

    [Fact]
    public void CodecChange_FirstFrame_ChangesEvenWhenChannelLayoutIsZero()
    {
        using var r = new S16MonoResampler();
        // A frame carrying an unspecified (mask 0) layout must still count as a first-frame change via format/rate,
        // so the context is built rather than skipped on the layout seed (0 == 0).
        r.DetectCodecChange(format: 8, sampleRate: 44100, channelLayout: 0).Should().BeTrue();
    }

    [Fact]
    public void CodecChange_UnchangedFrame_IsNotAChange()
    {
        using var r = new S16MonoResampler();
        r.DetectCodecChange(8, 44100, 3);
        r.DetectCodecChange(8, 44100, 3).Should().BeFalse();
    }

    [Theory]
    [InlineData(9, 44100, 3)]  // format differs
    [InlineData(8, 48000, 3)]  // sample rate differs
    [InlineData(8, 44100, 4)]  // channel layout differs
    public void CodecChange_AnyFieldDiffering_IsAChange(int format, int sampleRate, ulong layout)
    {
        using var r = new S16MonoResampler();
        r.DetectCodecChange(8, 44100, 3);
        r.DetectCodecChange(format, sampleRate, layout).Should().BeTrue();
    }

    // --- EnsureCapacity: the output buffer grows to the largest frame and is never shrunk (so it can be reused) ---

    [Fact]
    public void Buffer_StartsEmpty()
    {
        using var r = new S16MonoResampler();
        r.Buffer.Length.Should().Be(0);
    }

    [Fact]
    public void EnsureCapacity_GrowsToRequestedSize()
    {
        using var r = new S16MonoResampler();
        r.EnsureCapacity(100);
        r.Buffer.Length.Should().Be(100);
    }

    [Fact]
    public void EnsureCapacity_NeverShrinks_AndReusesSameArray()
    {
        using var r = new S16MonoResampler();
        r.EnsureCapacity(100);
        byte[] first = r.Buffer;

        r.EnsureCapacity(50); // smaller request must not reallocate
        r.Buffer.Should().BeSameAs(first);
        r.Buffer.Length.Should().Be(100);
    }

    [Fact]
    public void EnsureCapacity_GrowsAndReallocatesWhenLarger()
    {
        using var r = new S16MonoResampler();
        r.EnsureCapacity(100);
        byte[] first = r.Buffer;

        r.EnsureCapacity(200);
        r.Buffer.Should().NotBeSameAs(first);
        r.Buffer.Length.Should().Be(200);
    }
}
