using System;
using System.Buffers.Binary;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Managed audio pre-filter for the opt-in ASR denoise (F-02). The producer resamples each decoded frame to
/// little-endian signed 16-bit mono 16 kHz PCM for Whisper; when <c>Subtitles.ASRDenoise</c> is on, this high-pass
/// runs in place over that PCM (the "testable core") before the optional native FFmpeg <c>afftdn</c> stage. A
/// high-pass removes sub-bass rumble, hum and DC offset that smear the spectrogram, which helps Whisper on noisy
/// material. It is NOT speech/music separation (that is the full F-02 Demucs sidecar); this targets stationary noise.
/// <para>
/// Static / dependency-free over the same PCM <see cref="AsrSilence"/> reads, so it is unit-testable without FFmpeg.
/// The filter is stateful (a second-order section carries its history across frames so there is no per-frame
/// discontinuity), so one instance is used per ASR pass and reset between passes.
/// </para>
/// </summary>
public sealed class AsrHighPassFilter
{
    /// <summary>Default high-pass cutoff (Hz). ~80 Hz sits just below the speech fundamental, so it strips rumble
    /// without eating voiced energy.</summary>
    public const double DefaultCutoffHz = 80.0;

    // Normalized RBJ biquad high-pass coefficients (a0 folded in).
    private readonly double _b0, _b1, _b2, _a1, _a2;

    // Direct-Form-I state.
    private double _x1, _x2, _y1, _y2;

    /// <summary>
    /// Builds a second-order Butterworth (Q = 1/sqrt(2)) high-pass for <paramref name="sampleRate"/> Hz at
    /// <paramref name="cutoffHz"/>. An out-of-range cutoff (&lt;= 0 or &gt;= Nyquist) is clamped into a valid band so
    /// construction never throws.
    /// </summary>
    public AsrHighPassFilter(int sampleRate, double cutoffHz = DefaultCutoffHz)
    {
        if (sampleRate <= 0)
            sampleRate = 16000;

        double nyquist = sampleRate / 2.0;
        if (!(cutoffHz > 0.0))
            cutoffHz = DefaultCutoffHz;
        if (cutoffHz >= nyquist)
            cutoffHz = nyquist * 0.99;

        // RBJ Audio-EQ-Cookbook high-pass.
        double w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double q = 1.0 / Math.Sqrt(2.0); // Butterworth, maximally flat
        double alpha = sinW0 / (2.0 * q);

        double b0 = (1.0 + cosW0) / 2.0;
        double b1 = -(1.0 + cosW0);
        double b2 = (1.0 + cosW0) / 2.0;
        double a0 = 1.0 + alpha;
        double a1 = -2.0 * cosW0;
        double a2 = 1.0 - alpha;

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    /// <summary>Clears the filter history (call between ASR passes so one pass cannot bleed into the next).</summary>
    public void Reset()
    {
        _x1 = _x2 = _y1 = _y2 = 0.0;
    }

    /// <summary>
    /// High-passes <paramref name="pcmS16Le"/> (little-endian signed 16-bit mono) IN PLACE over the first
    /// <paramref name="byteCount"/> bytes (clamped to the span; a trailing odd byte is left untouched). The byte count
    /// is unchanged (1:1), so the caller's resampled size and the T-09 silence contract are preserved. Output is
    /// rounded and clamped to the 16-bit range. A buffer with no whole sample is a no-op. Never throws.
    /// </summary>
    public void ProcessInPlace(Span<byte> pcmS16Le, int byteCount)
    {
        int n = Math.Min(byteCount, pcmS16Le.Length);
        if (n < 2)
            return;

        n -= n % 2; // whole samples only

        for (int i = 0; i < n; i += 2)
        {
            double x = BinaryPrimitives.ReadInt16LittleEndian(pcmS16Le.Slice(i, 2));

            double y = (_b0 * x) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);

            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;

            int iy = (int)Math.Round(y);
            if (iy > short.MaxValue)
                iy = short.MaxValue;
            else if (iy < short.MinValue)
                iy = short.MinValue;

            BinaryPrimitives.WriteInt16LittleEndian(pcmS16Le.Slice(i, 2), (short)iy);
        }
    }
}

/// <summary>Helpers for the optional native FFmpeg <c>afftdn</c> denoise stage (built in the ASR audio reader).</summary>
public static class AsrDenoise
{
    /// <summary>Default afftdn noise-reduction amount in dB. Conservative so speech is not muffled.</summary>
    public const double DefaultAfftdnNoiseReductionDb = 12.0;

    /// <summary>
    /// Builds the FFmpeg <c>afftdn</c> filter argument string (the part after the filter name, e.g. <c>nr=12</c>) for a
    /// given noise-reduction amount (dB), suitable for <c>avfilter_graph_create_filter</c>. Clamped to the filter's
    /// documented 0.01..97 dB range (an out-of-range request is pulled back in). Uses the invariant culture so the
    /// decimal separator is always '.'.
    /// </summary>
    public static string BuildAfftdnArgs(double noiseReductionDb = DefaultAfftdnNoiseReductionDb)
    {
        if (!(noiseReductionDb >= 0.01))
            noiseReductionDb = 0.01;
        else if (noiseReductionDb > 97.0)
            noiseReductionDb = 97.0;

        return "nr=" + noiseReductionDb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
