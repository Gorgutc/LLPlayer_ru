namespace LLPlayer.Avalonia.Services;

/// <summary>
/// F-12 waveform display geometry shared by the control and its tests. Mirrors the WPF seek-bar envelope:
/// peaks are reduced to at most one column per device-pixel (max within each column) and display-gained to the
/// loudest bucket with 5 % headroom, so quiet dialogue stays visible while the engine peaks keep their fixed scale.
/// </summary>
public static class WaveformEnvelopeLayout
{
    /// <summary>
    /// Column amplitudes in device-independent pixels for a <paramref name="width"/> x <paramref name="height"/>
    /// envelope. Returns an empty array for null/empty peaks or a degenerate size (nothing to paint), and all zeros
    /// for a silent file (the envelope collapses to the centre line).
    /// </summary>
    public static double[] Compute(float[]? peaks, double width, double height)
    {
        if (peaks is not { Length: > 0 } || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return [];

        int cols = Math.Min(peaks.Length, Math.Max(1, (int)width));
        float max = 0f;
        for (int i = 0; i < peaks.Length; i++)
        {
            float value = Sanitize(peaks[i]);
            if (value > max)
                max = value;
        }

        double gain = max > 0f ? 0.95 / max : 0.0;
        double halfH = height / 2.0;
        double[] amp = new double[cols];
        for (int c = 0; c < cols; c++)
        {
            int start = (int)((long)c * peaks.Length / cols);
            int end = (int)((long)(c + 1) * peaks.Length / cols);
            if (end <= start)
                end = start + 1;
            if (end > peaks.Length)
                end = peaks.Length;

            float m = 0f;
            for (int i = start; i < end; i++)
            {
                float value = Sanitize(peaks[i]);
                if (value > m)
                    m = value;
            }

            amp[c] = Math.Min(halfH, m * gain * halfH);
        }

        return amp;
    }

    /// <summary>The engine promises finite 0..1 buckets; clamp defensively so bad data can never produce NaN geometry.</summary>
    static float Sanitize(float peak) => float.IsFinite(peak) ? Math.Clamp(peak, 0f, 1f) : 0f;
}
