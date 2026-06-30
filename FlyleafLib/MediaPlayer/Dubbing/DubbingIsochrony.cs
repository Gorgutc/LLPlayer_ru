using System.Collections.Generic;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Pure isochrony math for the MVP: capped pitch-preserving time-stretch factor (<c>atempo</c>) plus
/// drift-reset placement. Russian runs ~10-30% longer than most source languages, so a single
/// capped atempo cannot always fit a line; the placement lets a line run long / start late and then
/// HARD-RESETS accumulated drift at the next pause so error never compounds. No I/O — unit-tested.
/// </summary>
public static class DubbingIsochrony
{
    /// <summary>Source gap, in ms, that counts as a pause and triggers a drift reset.</summary>
    public const double PauseResetMs = 300.0;

    /// <summary>
    /// atempo factor to fit a rendered clip into its slot. atempo &gt; 1 SHORTENS (speeds up). We only
    /// shorten overflowing clips; a clip already within its slot is left at 1.0 (silence fills the
    /// remainder — slowing speech to fill sounds worse). The factor is clamped to [<paramref name="min"/>,
    /// <paramref name="max"/>]; if the required factor exceeds max, the clip stays long and the overflow
    /// becomes drift (recovered at the next pause).
    /// </summary>
    public static double ComputeAtempo(double clipMs, double slotMs, double min, double max)
    {
        if (clipMs <= 0 || slotMs <= 0)
            return 1.0;

        if (clipMs <= slotMs)
            return 1.0;

        double factor = clipMs / slotMs;
        return Clamp(factor, min, max);
    }

    public static double Clamp(double value, double min, double max)
    {
        // Tolerate a caller passing min/max in the wrong order.
        double lo = System.Math.Min(min, max);
        double hi = System.Math.Max(min, max);
        return System.Math.Clamp(value, lo, hi);
    }

    /// <summary>
    /// Resulting placement (start offset in ms) of a single line during sequential layout.
    /// </summary>
    public readonly record struct Placement(int Index, double StartMs, double EndMs)
    {
        public double DurationMs => EndMs - StartMs;
    }

    /// <summary>
    /// Place each line on the timeline. <paramref name="lines"/> must be ordered by start time;
    /// <paramref name="renderedDurationsMs"/> is the post-atempo clip duration per line (same order/length).
    /// A clip cannot start before the previous placed clip ends (no overlap); when a real source pause
    /// (gap ≥ <paramref name="pauseResetMs"/>) precedes a line, accumulated drift is dropped so the line
    /// snaps back to its source start.
    /// </summary>
    public static IReadOnlyList<Placement> Place(
        IReadOnlyList<DubbingLine> lines,
        IReadOnlyList<double> renderedDurationsMs,
        double pauseResetMs = PauseResetMs)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(renderedDurationsMs);
        if (lines.Count != renderedDurationsMs.Count)
            throw new ArgumentException("lines and renderedDurationsMs must have the same length.");

        List<Placement> result = new(lines.Count);
        double cursor = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            double srcStart = lines[i].Start.TotalMilliseconds;

            if (i > 0)
            {
                double srcGap = lines[i].Start.TotalMilliseconds - lines[i - 1].End.TotalMilliseconds;
                if (srcGap >= pauseResetMs)
                {
                    // Forgive accumulated drift at a genuine pause: this line starts at its own source time.
                    cursor = srcStart;
                }
            }

            double start = System.Math.Max(srcStart, cursor);
            double dur = System.Math.Max(0, renderedDurationsMs[i]);
            result.Add(new Placement(lines[i].Index, start, start + dur));
            cursor = start + dur;
        }

        return result;
    }
}
