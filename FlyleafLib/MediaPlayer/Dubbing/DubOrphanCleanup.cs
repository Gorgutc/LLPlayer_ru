using System;
using System.Collections.Generic;
using System.Linq;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Tracks dub outputs whose <c>/assemble</c> was canceled or failed so they can be re-deleted once the sidecar is
/// guaranteed stopped (HC-43). Cancelling the HTTP request does NOT stop the python worker — it still finishes and
/// does <c>os.replace(output)</c> a few seconds later, so the immediate best-effort delete in the render catch can
/// miss the file. That orphaned-but-complete dub then makes the next run skip rendering (<c>DubExistsAnyFormat</c>)
/// and the auto-loader pick it up. This is a pure, unit-testable bookkeeping helper; the actual delete is injected
/// so the caller keeps its own best-effort <c>File.Delete</c>. A C#-only mitigation — it narrows the window without
/// touching the frozen sidecar HTTP contract; the two safe re-clean points are the start of the next render of the
/// same target and the renderer's <c>DisposeAsync</c> (after the sidecar process is reaped).
/// </summary>
internal sealed class DubOrphanCleanup
{
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records <paramref name="outputPath"/> as a target whose assemble was canceled/failed and whose dub may still
    /// materialize after the fact. No-op for a null/blank path.
    /// </summary>
    public void MarkCanceled(string? outputPath)
    {
        if (string.IsNullOrEmpty(outputPath))
            return;

        lock (_gate)
            _pending.Add(outputPath);
    }

    /// <summary>
    /// Called at the start of a (re)render of <paramref name="outputPath"/>: forgets any pending record for it (so a
    /// fresh successful render is NOT re-cleaned by <see cref="CleanAll"/> afterwards) and deletes an orphan that
    /// already landed. Returns true if a record was pending. The delete runs only when a record was pending, so a
    /// normal first render of a brand-new target does not touch the filesystem.
    /// </summary>
    public bool ClearAndClean(string outputPath, Action<string> delete)
    {
        ArgumentNullException.ThrowIfNull(delete);

        bool wasPending;
        lock (_gate)
            wasPending = _pending.Remove(outputPath);

        if (wasPending)
            delete(outputPath);

        return wasPending;
    }

    /// <summary>
    /// Called after the sidecar process is provably stopped (renderer <c>DisposeAsync</c>): deletes every recorded
    /// orphan and clears the set. Because the worker is dead, an orphan that was going to land already has, so this
    /// is the reliable catch. Returns the paths it attempted to delete (for logging/tests).
    /// </summary>
    public IReadOnlyList<string> CleanAll(Action<string> delete)
    {
        ArgumentNullException.ThrowIfNull(delete);

        string[] paths;
        lock (_gate)
        {
            paths = _pending.ToArray();
            _pending.Clear();
        }

        foreach (string path in paths)
            delete(path);

        return paths;
    }
}
