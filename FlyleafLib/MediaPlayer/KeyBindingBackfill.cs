using System.Collections.Generic;

namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>
/// HC-09: decides whether a newly-introduced default key binding should be backfilled into an existing config on load.
/// The command palette (Ctrl+K) shipped inside 0.3.0 without a config version bump, so returning users' configs lacked
/// it; the original per-launch backfill re-added it on every start (ignoring an intentional removal) and could create a
/// DUPLICATE chord — which <c>SettingsKeys</c> treats as a conflict and blocks the Keys tab's Apply — when the user had
/// reassigned that chord to another action. This makes the backfill one-shot (version-gated) and chord-safe.
/// </summary>
public static class KeyBindingBackfill
{
    /// <param name="loadedConfigVersion">
    /// The config's version as loaded (captured before the load-time version stamp overwrites it). A null / blank /
    /// unparseable value is treated as predating the fix (so the backfill runs).
    /// </param>
    /// <param name="backfillThreshold">
    /// A config at or after this version has already been through the backfill (or was created with the binding), so a
    /// missing binding there is an intentional removal and is NOT re-added.
    /// </param>
    /// <param name="existingBindings">The config's current key bindings.</param>
    /// <param name="candidate">The default binding to backfill (its action name + chord).</param>
    public static bool ShouldBackfill(
        string? loadedConfigVersion,
        Version backfillThreshold,
        IEnumerable<KeyBinding> existingBindings,
        KeyBinding candidate)
    {
        ArgumentNullException.ThrowIfNull(backfillThreshold);
        ArgumentNullException.ThrowIfNull(existingBindings);
        ArgumentNullException.ThrowIfNull(candidate);

        // One-shot version gate: only backfill configs predating the threshold. An unparseable/absent version predates.
        if (Version.TryParse(loadedConfigVersion, out Version? loadedVer) && loadedVer >= backfillThreshold)
            return false;

        foreach (KeyBinding existing in existingBindings)
        {
            if (existing is null)
                continue;

            // Already bound to this action → keep the user's binding, don't duplicate it.
            if (candidate.ActionName != null && existing.ActionName == candidate.ActionName)
                return false;

            // The candidate's chord is already taken (by another action) → adding it would create a duplicate chord,
            // which SettingsKeys treats as a conflict and blocks Apply. Respect the user's reassignment.
            if (existing.Key == candidate.Key
                && existing.Ctrl == candidate.Ctrl
                && existing.Alt == candidate.Alt
                && existing.Shift == candidate.Shift)
                return false;
        }

        return true;
    }
}
