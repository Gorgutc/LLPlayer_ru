using System.Threading;

namespace FlyleafLib.MediaPlayer;

partial class Player
{
    // ===== F-12 A-B repeat =====
    // Runtime-only loop between two user-set points (never persisted; reset on open/stop in ResetMe).
    // Stored as two longs (ticks; AbLoop.Unset = not set), accessed via Volatile.Read/Write for cross-thread
    // visibility: the UI/hotkey thread writes, the playback (screamer) thread reads in CheckABLoopBack, which
    // snapshots both into locals so a half-updated pair is rejected by AbLoop's A<B guard. 64-bit access is
    // atomic on the x64-only TFM; Volatile only adds the acquire/release barrier. ('volatile long' is illegal
    // in C# — CS0677 — so Volatile.Read/Write is used instead.)
    internal long _abLoopA = AbLoop.Unset;
    internal long _abLoopB = AbLoop.Unset;

    /// <summary>A-B repeat point A in 100ns ticks, or -1 when unset. Bindable.</summary>
    public long ABLoopA       { get { long a = Volatile.Read(ref _abLoopA); return a == AbLoop.Unset ? -1 : a; } }
    /// <summary>A-B repeat point B in 100ns ticks, or -1 when unset. Bindable.</summary>
    public long ABLoopB       { get { long b = Volatile.Read(ref _abLoopB); return b == AbLoop.Unset ? -1 : b; } }
    /// <summary>True when point A is set (drives the A marker). Bindable.</summary>
    public bool HasABLoopA    => Volatile.Read(ref _abLoopA) != AbLoop.Unset;
    /// <summary>True when point B is set (drives the B marker). Bindable.</summary>
    public bool HasABLoopB    => Volatile.Read(ref _abLoopB) != AbLoop.Unset;
    /// <summary>True when a valid A-B window is armed (both set, A &lt; B) and playback loops. Bindable.</summary>
    public bool ABLoopEnabled => AbLoop.IsEnabled(Volatile.Read(ref _abLoopA), Volatile.Read(ref _abLoopB));
    /// <summary>True when at least one point is set (drives the seekbar overlay visibility). Bindable.</summary>
    public bool ABLoopActive  => HasABLoopA || HasABLoopB;

    /// <summary>Sets A-B repeat point A to the current playback position. Drops B if it would no longer follow A.</summary>
    public void SetABLoopStart()
    {
        if (!canPlay)
            return;

        long t = curTime;
        Volatile.Write(ref _abLoopA, t);
        if (Volatile.Read(ref _abLoopB) != AbLoop.Unset && Volatile.Read(ref _abLoopB) <= t)
            Volatile.Write(ref _abLoopB, AbLoop.Unset);

        RaiseABLoopChanged();
        OSDMessage = "A-B Repeat: A set";
    }

    /// <summary>Sets A-B repeat point B to the current playback position. Ignored unless it falls after A.</summary>
    public void SetABLoopEnd()
    {
        if (!canPlay)
            return;

        long t = curTime;
        long a = Volatile.Read(ref _abLoopA);
        if (a != AbLoop.Unset && t <= a)
        {
            OSDMessage = "A-B Repeat: B must be after A";
            return;
        }

        Volatile.Write(ref _abLoopB, t);
        RaiseABLoopChanged();
        OSDMessage = "A-B Repeat: B set";
    }

    /// <summary>Clears both A-B repeat points.</summary>
    public void ClearABLoop()
    {
        if (Volatile.Read(ref _abLoopA) == AbLoop.Unset && Volatile.Read(ref _abLoopB) == AbLoop.Unset)
            return;

        Volatile.Write(ref _abLoopA, AbLoop.Unset);
        Volatile.Write(ref _abLoopB, AbLoop.Unset);
        RaiseABLoopChanged();
        OSDMessage = "A-B Repeat: Cleared";
    }

    /// <summary>Single-key cycle: no A → set A; A set / no B → set B; both set → clear.</summary>
    public void ToggleABLoop()
    {
        if (Volatile.Read(ref _abLoopA) == AbLoop.Unset)
            SetABLoopStart();
        else if (Volatile.Read(ref _abLoopB) == AbLoop.Unset)
            SetABLoopEnd();
        else
            ClearABLoop();
    }

    // Reset (no UI raise) — called from the non-UI part of ResetMe; the matching raise runs in its UIAdd block.
    internal void ResetABLoop()
    {
        Volatile.Write(ref _abLoopA, AbLoop.Unset);
        Volatile.Write(ref _abLoopB, AbLoop.Unset);
    }

    void RaiseABLoopChanged()
    {
        Raise(nameof(ABLoopA));
        Raise(nameof(ABLoopB));
        Raise(nameof(HasABLoopA));
        Raise(nameof(HasABLoopB));
        Raise(nameof(ABLoopEnabled));
        Raise(nameof(ABLoopActive));
    }

    /// <summary>
    /// Playback-thread hook (called from <see cref="UpdateCurTime"/> after curTime is committed): if a valid
    /// A-B window is armed and the playhead reached B, seek back to A. Returns true if it issued the loop-back
    /// seek. Reuses the proven seek path (queued under lock(seeks), drained by the screamer) — no new locking.
    /// Skipped during reverse playback (the reverse screamer never advances past B going forward).
    /// </summary>
    bool CheckABLoopBack(long curTimeTicks)
    {
        long a = Volatile.Read(ref _abLoopA);   // snapshot both; IsEnabled rejects any half-updated pair
        long b = Volatile.Read(ref _abLoopB);
        if (ReversePlayback || !AbLoop.ShouldLoopBack(a, b, curTimeTicks))
            return false;

        SeekAccurate((int)(a / 10000));          // accurate so the loop lands exactly on A
        return true;
    }
}
