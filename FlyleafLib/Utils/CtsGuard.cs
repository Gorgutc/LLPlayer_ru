using System.Threading;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Race-safe teardown of a shared <see cref="CancellationTokenSource"/> slot used by the "cancel the running work,
/// wait on its lock, then dispose" pattern (SubtitlesManager / SubtitlesOCR). The naive
/// <c>if (_cts != null) { _cts.Cancel(); lock (gate) { _cts.Dispose(); _cts = null; } }</c> re-reads the field
/// after the null-check, so a concurrent teardown can null or dispose it in between → NRE / ObjectDisposedException,
/// and it unconditionally clears the slot, so a freshly-installed CTS gets clobbered (HC-37).
///
/// The teardown is split so it works with EITHER a monitor object or a <see cref="System.Threading.Lock"/> as the
/// caller's gate (this class never locks): call <see cref="CancelCaptured"/> to snapshot+cancel the current CTS
/// OUTSIDE the lock, then call <see cref="TryDisposeAndClear"/> INSIDE the slot's own lock to dispose+null it only
/// if it still points at the captured instance.
/// </summary>
public static class CtsGuard
{
    /// <summary>
    /// Snapshots the current CTS from <paramref name="slot"/> and cancels it (tolerating a concurrent dispose), then
    /// returns the captured instance so the caller can hand it to <see cref="TryDisposeAndClear"/> under its lock.
    /// Returns null when the slot was already empty. Cancels the captured local — never the live field — so a
    /// concurrent null of the field cannot NRE this call.
    /// </summary>
    public static CancellationTokenSource? CancelCaptured(ref CancellationTokenSource? slot)
    {
        CancellationTokenSource? cts = Volatile.Read(ref slot);
        if (cts == null)
            return null;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent teardown disposed this CTS between the read and the cancel; nothing left to cancel.
        }

        return cts;
    }

    /// <summary>
    /// Disposes <paramref name="captured"/> and clears <paramref name="slot"/> ONLY if the slot still references the
    /// captured instance (compare-and-clear). Must be called under the same lock that guards writes to the slot.
    /// Returns true if this call cleared the slot; false when the slot was already cleared or has been replaced by a
    /// fresh CTS (which must not be disposed/clobbered).
    /// </summary>
    public static bool TryDisposeAndClear(ref CancellationTokenSource? slot, CancellationTokenSource? captured)
    {
        if (captured != null && ReferenceEquals(slot, captured))
        {
            slot = null;
            captured.Dispose();
            return true;
        }

        return false;
    }
}
