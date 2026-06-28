using System;
using System.Buffers.Binary;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Pure, dependency-free peak reducer for the opt-in seek-bar audio waveform (F-12). Operates on the same
/// little-endian signed-16-bit mono PCM the ASR path produces, so it is unit-testable without FFmpeg/WPF
/// (mirrors <see cref="AsrHighPassFilter"/> and <c>AbLoop</c>). It produces a fixed number of amplitude
/// buckets — a max-abs envelope normalized to 0..1 against full scale (<c>32768</c>). Display gain is applied
/// by the UI layer, not here, so the peaks stay predictable and exactly testable, and the OFF path (no peaks)
/// draws nothing.
/// </summary>
public static class WaveformPeaks
{
    /// <summary>Full-scale divisor for S16 samples (|short.MinValue| == 32768).</summary>
    public const float FullScale = 32768f;

    /// <summary>
    /// Reduces mono S16 <paramref name="samples"/> to <paramref name="bucketCount"/> max-abs buckets in 0..1
    /// (<c>|sample| / 32768</c>) by index (sample i → bucket <c>i * bucketCount / length</c>). A
    /// <paramref name="bucketCount"/> &lt;= 0 yields an empty array; an empty input yields all-zero buckets;
    /// fewer samples than buckets leaves the trailing buckets at 0. Never throws.
    /// <para>
    /// This is a standalone, index-bucketed reference/utility reducer for a fully in-memory buffer; the LIVE
    /// waveform decode path does NOT use it (it streams via <see cref="WaveformPeakBuilder"/>, which buckets by
    /// absolute PTS so variable-frame-rate/gapped/odd-start streams land correctly).
    /// </para>
    /// </summary>
    public static float[] Reduce(ReadOnlySpan<short> samples, int bucketCount)
    {
        if (bucketCount <= 0)
            return Array.Empty<float>();

        float[] peaks = new float[bucketCount];
        int n = samples.Length;
        if (n == 0)
            return peaks;

        for (int i = 0; i < n; i++)
        {
            // long math so a long stream never overflows the index multiply.
            int b = (int)((long)i * bucketCount / n);
            if (b >= bucketCount)
                b = bucketCount - 1; // guard the i == n-1 rounding edge

            int s = samples[i];
            int abs = s < 0 ? -s : s; // promotes to int first, so short.MinValue → 32768, no overflow
            float v = abs / FullScale;
            if (v > peaks[b])
                peaks[b] = v;
        }

        return peaks;
    }
}

/// <summary>
/// Streaming companion to <see cref="WaveformPeaks"/> for the offline decode pass (<c>WaveformReader</c>):
/// folds S16 mono PCM frames into fixed time-buckets as they are decoded, so the whole file's samples never
/// need to be held in memory (a 3 h film is a few KB of peaks, not hundreds of MB of PCM). Each sample is
/// placed by its absolute timeline position (frame PTS + intra-frame offset), so variable-frame-rate,
/// gapped, or odd-start-time streams still map correctly. Pure (no FFmpeg/WPF) and unit-testable.
/// </summary>
public sealed class WaveformPeakBuilder
{
    private const long TicksPerSecond = 10_000_000L;

    private readonly float[] _peaks;
    private readonly int _bucketCount;
    private readonly long _totalDurationTicks;
    private readonly int _sampleRate;

    /// <summary>Number of buckets (clamped to &gt;= 1).</summary>
    public int BucketCount => _bucketCount;

    /// <param name="bucketCount">Target bucket count (clamped to &gt;= 1).</param>
    /// <param name="totalDurationTicks">Total media duration in 100ns ticks (clamped to &gt;= 1 to avoid div-by-zero).</param>
    /// <param name="sampleRate">PCM sample rate in Hz (&lt;= 0 falls back to 16000).</param>
    public WaveformPeakBuilder(int bucketCount, long totalDurationTicks, int sampleRate)
    {
        _bucketCount = Math.Max(1, bucketCount);
        _totalDurationTicks = Math.Max(1, totalDurationTicks);
        _sampleRate = sampleRate <= 0 ? 16000 : sampleRate;
        _peaks = new float[_bucketCount];
    }

    /// <summary>
    /// Folds the first <paramref name="byteCount"/> bytes of <paramref name="pcmS16LeMono"/> (little-endian
    /// signed-16-bit mono) into the buckets, taking the running max-abs per bucket. <paramref name="frameStartTicks"/>
    /// is the timeline position (100ns ticks) of the first sample; sample i then sits at
    /// <c>frameStartTicks + i * 10_000_000 / sampleRate</c>. A trailing odd byte is ignored. Never throws.
    /// </summary>
    public void Add(ReadOnlySpan<byte> pcmS16LeMono, int byteCount, long frameStartTicks)
    {
        int n = Math.Min(byteCount, pcmS16LeMono.Length);
        if (n < 2)
            return;

        n -= n % 2; // whole samples only
        int sampleCount = n / 2;

        for (int i = 0; i < sampleCount; i++)
        {
            short s = BinaryPrimitives.ReadInt16LittleEndian(pcmS16LeMono.Slice(i * 2, 2));

            long sampleTicks = frameStartTicks + ((long)i * TicksPerSecond / _sampleRate);
            long b = sampleTicks * _bucketCount / _totalDurationTicks;
            int bucket = b < 0 ? 0 : (b >= _bucketCount ? _bucketCount - 1 : (int)b);

            int abs = s < 0 ? -s : s;
            float v = abs / WaveformPeaks.FullScale;
            if (v > _peaks[bucket])
                _peaks[bucket] = v;
        }
    }

    /// <summary>Returns the accumulated 0..1 max-abs peaks (the builder's own array; build once at end of pass).</summary>
    public float[] Build() => _peaks;
}
