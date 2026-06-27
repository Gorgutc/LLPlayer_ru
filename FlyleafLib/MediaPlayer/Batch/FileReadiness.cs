namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

/// <summary>
/// Snapshot of a watched file's size + last-write time across stability polls. <see cref="StableTicks"/> counts
/// how many consecutive polls observed the file unchanged, non-empty, and openable.
/// </summary>
public readonly record struct FileStabilityState(long Size, DateTime WriteUtc, int StableTicks)
{
    /// <summary>State before the first poll (negative size means "no snapshot yet").</summary>
    public static readonly FileStabilityState Initial = new(-1, default, 0);

    public bool IsReady => StableTicks >= FileReadiness.RequiredStableTicks;
}

/// <summary>
/// Pure, deterministic file-readiness logic for the watch-folder watcher (F-09). The caller injects the probed
/// size, last-write time, and whether the file currently opens for shared read, so this is fully unit-testable
/// without touching the filesystem or the clock. A file becomes "ready" once it has been openable, non-empty, and
/// unchanged in both size AND last-write time across <see cref="RequiredStableTicks"/> consecutive polls — which
/// rules out a copy/download still in progress (size keeps growing) and a writer still holding the file (the
/// shared-read open fails).
/// </summary>
public static class FileReadiness
{
    /// <summary>Consecutive unchanged+openable polls required before a file is considered ready.</summary>
    public const int RequiredStableTicks = 2;

    public static FileStabilityState Step(FileStabilityState prev, long currentSize, DateTime currentWriteUtc, bool canOpen)
    {
        // Not yet a complete, readable file: reset the streak but remember the latest snapshot.
        if (!canOpen || currentSize <= 0)
            return new FileStabilityState(currentSize, currentWriteUtc, 0);

        bool unchanged = prev.Size == currentSize && prev.WriteUtc == currentWriteUtc;
        int ticks = unchanged ? prev.StableTicks + 1 : 0;
        return new FileStabilityState(currentSize, currentWriteUtc, ticks);
    }
}
