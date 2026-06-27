using System;
using System.Buffers.Binary;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Pure audio-silence helpers for ASR chunk splitting (T-09). The producer normally cuts an audio chunk purely by
/// byte size or elapsed time, which can split a phrase mid-word. These let it instead prefer a SILENT boundary (so a
/// chunk ends in a pause between phrases) while the size/time caps stay as a hard ceiling. They operate on the same
/// little-endian signed 16-bit mono PCM the producer already resamples for Whisper, so there is no extra decode.
/// Static / dependency-free (mirrors <see cref="SubtitleCaseFixer"/>) → safe to unit-test without FFmpeg.
/// </summary>
public static class AsrSilence
{
    /// <summary>
    /// Root-mean-square amplitude of <paramref name="pcmS16Le"/> (interpreted as little-endian signed 16-bit mono
    /// samples), normalized to [0,1] by the full-scale value 32768. Reads at most <paramref name="byteCount"/> bytes
    /// and is clamped to the span length; a trailing odd byte is ignored. An empty or sub-sample buffer yields 0.0.
    /// Never throws.
    /// </summary>
    public static double Rms(ReadOnlySpan<byte> pcmS16Le, int byteCount)
    {
        int n = Math.Min(byteCount, pcmS16Le.Length);
        if (n < 2)
            return 0.0;

        n -= n % 2; // whole samples only

        double sumSquares = 0.0;
        int samples = n / 2;
        for (int i = 0; i < n; i += 2)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(pcmS16Le.Slice(i, 2));
            double v = sample;
            sumSquares += v * v;
        }

        return Math.Sqrt(sumSquares / samples) / 32768.0;
    }

    /// <summary>
    /// True when the frame in <paramref name="pcmS16Le"/> is quieter than <paramref name="rmsThreshold"/>
    /// (normalized RMS). A non-positive threshold disables silence detection (the frame is never considered silent).
    /// A frame with no whole sample (e.g. a zero-output resample) is NOT treated as silence — "no audio" is not the
    /// same as "a quiet boundary", so it must not trigger an early chunk cut.
    /// </summary>
    public static bool IsSilent(ReadOnlySpan<byte> pcmS16Le, int byteCount, double rmsThreshold)
    {
        if (rmsThreshold <= 0.0)
            return false;

        if (Math.Min(byteCount, pcmS16Le.Length) < 2)
            return false;

        return Rms(pcmS16Le, byteCount) <= rmsThreshold;
    }

    /// <summary>
    /// True once a chunk is "big enough" that cutting early at a silent boundary is allowed — i.e. either the
    /// accumulated PCM bytes or the elapsed wall-clock has reached <paramref name="softFraction"/> of its hard cap.
    /// This keeps silence-cut chunks from becoming tiny (each chunk is one Whisper invocation, and tiny chunks lose
    /// intra-chunk context). A <paramref name="softFraction"/> of 0 is always ready; 1.0 is ready only at the hard
    /// cap (i.e. silence-splitting is effectively off because the hard cut fires first).
    /// </summary>
    public static bool IsSoftReady(long dataBytes, long hardByteCap, TimeSpan elapsed, TimeSpan hardElapsedCap, double softFraction)
    {
        if (softFraction <= 0.0)
            return true;

        bool bySize = hardByteCap > 0 && dataBytes >= (long)(hardByteCap * softFraction);
        bool byTime = hardElapsedCap > TimeSpan.Zero && elapsed >= TimeSpan.FromTicks((long)(hardElapsedCap.Ticks * softFraction));
        return bySize || byTime;
    }
}
