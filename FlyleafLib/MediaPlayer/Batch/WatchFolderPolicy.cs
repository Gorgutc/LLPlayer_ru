namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public enum WatchEnqueueDecision
{
    /// <summary>The changed path is a fresh, eligible video that should be added to the batch list.</summary>
    Enqueue,
    /// <summary>Not a video file (e.g. a sidecar, a partial download) — ignore.</summary>
    SkipNotVideo,
    /// <summary>Already represented by a job in the list — ignore.</summary>
    SkipDuplicate,
    /// <summary>Output already exists and overwrite is off — ignore.</summary>
    SkipExistingOutput,
}

/// <summary>
/// Pure eligibility rule for the watch-folder watcher (F-09): given a changed path, decide whether it should be
/// added to the batch list. Mirrors the manual-scan rules so a watch-added file behaves exactly like a scanned
/// one. No filesystem or clock access — every input is injected, so it is fully unit-testable.
/// </summary>
public static class WatchFolderPolicy
{
    public static WatchEnqueueDecision ShouldEnqueue(
        string changedPath,
        IEnumerable<string> knownJobPaths,
        Func<string, bool> isVideo,
        bool outputExists,
        bool overwriteExisting)
    {
        ArgumentNullException.ThrowIfNull(changedPath);
        ArgumentNullException.ThrowIfNull(knownJobPaths);
        ArgumentNullException.ThrowIfNull(isVideo);

        if (!isVideo(changedPath))
            return WatchEnqueueDecision.SkipNotVideo;

        foreach (string known in knownJobPaths)
        {
            if (string.Equals(known, changedPath, StringComparison.OrdinalIgnoreCase))
                return WatchEnqueueDecision.SkipDuplicate;
        }

        if (outputExists && !overwriteExisting)
            return WatchEnqueueDecision.SkipExistingOutput;

        return WatchEnqueueDecision.Enqueue;
    }
}
