using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>Owns physical app presses, retaining the selected delegate until release or explicit cancellation.</summary>
public sealed class AppShortcutDispatcher(TimeProvider? timeProvider = null)
{
    readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    readonly Dictionary<WpfKey, Press> pressed = [];
    readonly HashSet<WpfKey> orphanedReleases = [];
    static readonly TimeSpan MissedReleaseGuard = TimeSpan.FromSeconds(2);

    sealed class Press(AppShortcut shortcut, Action action, long timestamp)
    {
        public AppShortcut Shortcut { get; } = shortcut;
        public Action? Action { get; set; } = action;
        public long Timestamp { get; } = timestamp;
    }

    public bool HandleKeyDown(AppShortcutMap map, WpfKey key, bool ctrl, bool alt, bool shift)
    {
        orphanedReleases.Remove(key);
        // Like the native guard, another trigger after a lost release cancels stale executable work.
        // Retain bounded per-physical-key ownership so its eventual up cannot reach a new app/native map.
        long now = time.GetTimestamp();
        foreach ((WpfKey heldKey, Press held) in pressed)
            if (held.Shortcut.IsKeyUp && heldKey != key && time.GetElapsedTime(held.Timestamp, now) >= MissedReleaseGuard)
                held.Action = null;

        if (pressed.TryGetValue(key, out Press? existing))
        {
            if (!existing.Shortcut.IsKeyUp && existing.Shortcut.Matches(key, ctrl, alt, shift))
                existing.Action?.Invoke();
            return true;
        }

        if (!map.TryResolve(key, ctrl, alt, shift, out AppShortcut? shortcut, out Action? action)) return false;
        // Record before executing: a handler may open a modal surface and synchronously cancel pending input.
        pressed[key] = new(shortcut!, action!, now);
        if (!shortcut!.IsKeyUp) action!();
        return true;
    }

    public bool HandleKeyUp(WpfKey key)
    {
        if (!pressed.Remove(key, out Press? press)) return orphanedReleases.Remove(key);
        if (press.Shortcut.IsKeyUp) press.Action?.Invoke();
        return true;
    }

    /// <summary>Cancels executable snapshots without transferring their outstanding releases to the engine.</summary>
    public void CancelPending()
    {
        foreach (Press press in pressed.Values) press.Action = null;
    }

    /// <summary>A new active context admits fresh downs, while old unpaired ups remain consume-only.</summary>
    public void BeginInputEpoch()
    {
        orphanedReleases.UnionWith(pressed.Keys);
        pressed.Clear();
    }
}
