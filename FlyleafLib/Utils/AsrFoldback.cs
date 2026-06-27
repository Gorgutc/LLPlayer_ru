using System;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Pure decision helpers for ASR "fold-back" (T-08). When interactive ASR starts mid-video it seeks to the current
/// playback position and transcribes forward, leaving the earlier audio untranscribed. Fold-back additionally
/// transcribes that skipped span — and, crucially, runs it FIRST so cues are still emitted in increasing-time order.
/// That ordering matters because the subtitle store's Add is append-only and its binary search (current-cue lookup,
/// delete-after) requires the cues to stay sorted by start time; backfilling earlier cues after later ones would
/// corrupt that invariant. Static / dependency-free → unit-testable without FFmpeg.
/// </summary>
public static class AsrFoldback
{
    /// <summary>
    /// Decide whether to fold back. Returns (foldBack, floor): <c>foldBack</c> is true only when
    /// <paramref name="enabled"/> and the start position <paramref name="curTime"/> exceeds
    /// <paramref name="threshold"/> (the same gate that makes the producer seek to curTime instead of starting from
    /// the beginning). <c>floor</c> is the timestamp to backfill from — the stream start, <see cref="TimeSpan.Zero"/>.
    /// When <c>foldBack</c> is false the caller's behavior is unchanged (single forward pass from curTime).
    /// </summary>
    public static (bool foldBack, TimeSpan floor) Plan(TimeSpan curTime, bool enabled, TimeSpan threshold)
    {
        bool foldBack = enabled && curTime > threshold;
        return (foldBack, TimeSpan.Zero);
    }

    /// <summary>
    /// True when a backfill pass has reached its stop point: the current frame time <paramref name="chunkRelTime"/>
    /// is at or past <paramref name="stopAt"/>. A null <paramref name="stopAt"/> (the open-ended forward pass) is
    /// never "reached", so that pass runs to EOF.
    /// </summary>
    public static bool ReachedStop(TimeSpan chunkRelTime, TimeSpan? stopAt)
    {
        return stopAt.HasValue && chunkRelTime >= stopAt.Value;
    }
}
