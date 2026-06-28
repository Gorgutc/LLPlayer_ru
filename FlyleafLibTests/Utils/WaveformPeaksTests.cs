using System.Buffers.Binary;
using AwesomeAssertions;

namespace FlyleafLib;

// Spec-derived tests for the F-12 waveform peak reducer (WaveformPeaks.Reduce) and the streaming time-bucket
// accumulator (WaveformPeakBuilder). Expectations come from the contract — max-abs per bucket normalized by
// 32768, index-mapping for Reduce, PTS-mapping for the builder — not from a run, so a regression that flips the
// metric, the normalization, or the bucket math turns one of these red. The native FFmpeg decode that feeds the
// builder (WaveformReader) is not unit-tested (needs a live graph) and is covered by .exe smoke instead.
public class WaveformPeaksTests
{
    private const int SampleRate = 16000;
    private const long TicksPerSecond = 10_000_000L;

    // count samples all equal to value, as S16LE bytes.
    private static byte[] Pcm(short value, int count)
    {
        var buf = new byte[count * 2];
        for (int i = 0; i < count; i++)
            BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(i * 2, 2), value);
        return buf;
    }

    private static short[] Samples(short value, int count)
    {
        var a = new short[count];
        Array.Fill(a, value);
        return a;
    }

    // ---- WaveformPeaks.Reduce (static, index-bucketed) --------------------

    [Fact]
    public void Reduce_FullScale_AllBucketsOne()
    {
        float[] peaks = WaveformPeaks.Reduce(Samples(short.MaxValue, 1000), 10);
        peaks.Should().HaveCount(10);
        peaks.Should().OnlyContain(p => p > 0.999f);
    }

    [Fact]
    public void Reduce_Silence_AllBucketsZero()
        => WaveformPeaks.Reduce(Samples(0, 1000), 10).Should().OnlyContain(p => p == 0f);

    [Fact]
    public void Reduce_HalfScale_IsAboutHalf()
    {
        float[] peaks = WaveformPeaks.Reduce(Samples(16384, 200), 4);
        peaks.Should().OnlyContain(p => p > 0.49f && p < 0.51f);
    }

    [Fact]
    public void Reduce_ShortMinValue_NormalizesToOne_NoOverflow()
        => WaveformPeaks.Reduce(Samples(short.MinValue, 50), 1)[0].Should().Be(1f);

    [Fact]
    public void Reduce_BucketCountZeroOrNegative_IsEmpty()
    {
        WaveformPeaks.Reduce(Samples(100, 10), 0).Should().BeEmpty();
        WaveformPeaks.Reduce(Samples(100, 10), -3).Should().BeEmpty();
    }

    [Fact]
    public void Reduce_EmptyInput_AllZeroOfBucketLength()
        => WaveformPeaks.Reduce(ReadOnlySpan<short>.Empty, 5).Should().HaveCount(5).And.OnlyContain(p => p == 0f);

    [Fact]
    public void Reduce_SingleLoudSampleInOneBucket_OnlyThatBucketHot()
    {
        // 100 samples → 10 buckets → 10 samples/bucket; sample 25 sits in bucket 2.
        var s = new short[100];
        s[25] = short.MaxValue;
        float[] peaks = WaveformPeaks.Reduce(s, 10);

        peaks[2].Should().BeGreaterThan(0.999f);
        for (int b = 0; b < 10; b++)
            if (b != 2)
                peaks[b].Should().Be(0f);
    }

    [Fact]
    public void Reduce_FewerSamplesThanBuckets_ScattersSamplesLeavesGapsZero()
    {
        // 2 samples over 8 buckets: sample i → bucket i*8/2, i.e. sample 0 → bucket 0, sample 1 → bucket 4.
        // The remaining buckets get no sample and stay 0 (index-mapping spreads sparse input, never crashes).
        var s = new short[] { short.MaxValue, short.MaxValue };
        float[] peaks = WaveformPeaks.Reduce(s, 8);
        peaks[0].Should().BeGreaterThan(0.999f);
        peaks[4].Should().BeGreaterThan(0.999f);
        foreach (int b in new[] { 1, 2, 3, 5, 6, 7 })
            peaks[b].Should().Be(0f);
    }

    [Fact]
    public void Reduce_TakesMaxAbsWithinBucket_NotLast()
    {
        // bucket 0 = samples {30000, -100, 5}; max-abs must win over the (later) quiet samples.
        var s = new short[] { 30000, -100, 5, 0, 0, 0 };
        float[] peaks = WaveformPeaks.Reduce(s, 2);
        peaks[0].Should().BeApproximately(30000f / 32768f, 0.001f);
        peaks[1].Should().Be(0f);
    }

    // ---- WaveformPeakBuilder (streaming, PTS-bucketed) --------------------

    [Fact]
    public void Builder_ClampsDegenerateArgs_NoThrow()
    {
        var b = new WaveformPeakBuilder(bucketCount: 0, totalDurationTicks: 0, sampleRate: 0);
        b.BucketCount.Should().Be(1);
        b.Invoking(x => x.Add(Pcm(1000, 10), 20, 0)).Should().NotThrow();
        b.Build().Should().HaveCount(1);
    }

    [Fact]
    public void Builder_FullScaleOverWholeDuration_AllBucketsOne()
    {
        long dur = 1 * TicksPerSecond;               // 1 second
        var b = new WaveformPeakBuilder(10, dur, SampleRate);
        b.Add(Pcm(short.MaxValue, SampleRate), SampleRate * 2, 0); // exactly 1s of full-scale audio
        b.Build().Should().OnlyContain(p => p > 0.999f);
    }

    [Fact]
    public void Builder_Silence_AllZero()
    {
        var b = new WaveformPeakBuilder(8, TicksPerSecond, SampleRate);
        b.Add(Pcm(0, SampleRate), SampleRate * 2, 0);
        b.Build().Should().OnlyContain(p => p == 0f);
    }

    [Fact]
    public void Builder_MapsSampleToBucketByPts()
    {
        // 10s duration, 10 buckets → 1 bucket/sec. A loud blip at t=2.5s must land in bucket 2.
        long dur = 10 * TicksPerSecond;
        var b = new WaveformPeakBuilder(10, dur, SampleRate);
        b.Add(Pcm(short.MaxValue, 100), 200, frameStartTicks: 25 * TicksPerSecond / 10); // 2.5s

        float[] peaks = b.Build();
        peaks[2].Should().BeGreaterThan(0.999f);
        for (int i = 0; i < 10; i++)
            if (i != 2)
                peaks[i].Should().Be(0f);
    }

    [Fact]
    public void Builder_OddTrailingByte_Ignored()
    {
        var b = new WaveformPeakBuilder(1, TicksPerSecond, SampleRate);
        byte[] pcm = Pcm(short.MaxValue, 3); // 6 bytes
        b.Add(pcm, 5, 0);                    // odd byteCount → 2 whole samples processed, 5th byte ignored
        b.Build()[0].Should().BeGreaterThan(0.999f);
    }

    [Fact]
    public void Builder_ByteCountBelowOneSample_NoOp()
    {
        var b = new WaveformPeakBuilder(4, TicksPerSecond, SampleRate);
        b.Add(Pcm(short.MaxValue, 4), 1, 0); // < 2 bytes usable
        b.Build().Should().OnlyContain(p => p == 0f);
    }

    [Fact]
    public void Builder_PositionPastDuration_ClampsToLastBucket_NoThrow()
    {
        var b = new WaveformPeakBuilder(4, TicksPerSecond, SampleRate);
        b.Invoking(x => x.Add(Pcm(short.MaxValue, 10), 20, frameStartTicks: 5 * TicksPerSecond))
            .Should().NotThrow();
        b.Build()[3].Should().BeGreaterThan(0.999f); // last bucket
    }

    [Fact]
    public void Builder_SplitFramesWithCorrectOffsets_AreAssociative()
    {
        // Same audio fed as one Add vs two half-Adds with the correct per-half start ticks → identical peaks.
        long dur = 10 * TicksPerSecond;
        const int half = SampleRate;            // 1s of samples per half
        byte[] whole = Pcm(10000, half * 2);    // 2s total
        long secondHalfTicks = (long)half * TicksPerSecond / SampleRate; // = 1s

        var single = new WaveformPeakBuilder(10, dur, SampleRate);
        single.Add(whole, whole.Length, 0);

        var split = new WaveformPeakBuilder(10, dur, SampleRate);
        split.Add(whole.AsSpan(0, half * 2), half * 2, 0);
        split.Add(whole.AsSpan(half * 2, half * 2), half * 2, secondHalfTicks);

        split.Build().Should().Equal(single.Build());
    }
}
