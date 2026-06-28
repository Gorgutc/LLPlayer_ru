namespace FlyleafLib.MediaPlayer;

/// <summary>
/// Pure decision helpers for the A-B repeat feature (F-12). Points are expressed in 100ns ticks,
/// the same domain as <see cref="Player.CurTime"/>. <see cref="Unset"/> marks a point as "not set".
/// Kept free of any FFmpeg/WPF/native dependency so it can be unit-tested in isolation
/// (mirrors how <c>PauseTokenSource</c>, <c>SubtitleSearcher</c> etc. are extracted for testing).
/// </summary>
public static class AbLoop
{
    /// <summary>Sentinel meaning "this point is not set".</summary>
    public const long Unset = long.MinValue;

    /// <summary>
    /// True when both points are set and strictly ordered (A &lt; B). This is the sole gate:
    /// a single-point or inverted/equal window is inert, so the OFF path is one boolean.
    /// </summary>
    public static bool IsEnabled(long aTicks, long bTicks)
        => aTicks != Unset && bTicks != Unset && aTicks < bTicks;

    /// <summary>
    /// True when a valid window is armed and the playhead has reached or passed B
    /// (so playback must seek back to A). Uses <c>&gt;=</c> so a frame landing exactly on B loops.
    /// Returns false whenever the window is not enabled.
    /// </summary>
    public static bool ShouldLoopBack(long aTicks, long bTicks, long curTicks)
        => IsEnabled(aTicks, bTicks) && curTicks >= bTicks;
}
