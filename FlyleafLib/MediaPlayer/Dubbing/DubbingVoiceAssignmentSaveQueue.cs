namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Debounces per-line voice companion saves per media path. A burst for the same media keeps only the latest
/// snapshot, while edits on different media are flushed independently.
/// </summary>
public sealed class DubbingVoiceAssignmentSaveQueue : IDisposable
{
    private sealed record SaveRequest(
        DubbingVoiceAssignmentMediaTarget Target,
        string MediaKey,
        long Revision,
        IReadOnlyList<SubtitleData> Subtitles);

    private readonly Func<bool> _isEnabled;
    private readonly Action<string, IReadOnlyList<SubtitleData>> _save;
    private readonly Action<string>? _onSaveClaimed;
    private readonly TimeSpan _delay;
    private readonly object _lock = new();
    private readonly object _saveLock = new();
    private readonly Dictionary<string, SaveRequest> _pendingByMediaKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _latestRevisionByMediaKey = new(StringComparer.OrdinalIgnoreCase);
    // Kept for the queue lifetime: a late alias request must never outlive and overwrite a newer save to the same
    // resolved file, even after that newer request completed and removed its capture-key marker.
    private readonly Dictionary<string, long> _latestRevisionByResolvedPath = new(StringComparer.OrdinalIgnoreCase);
    private long _nextRevision;
    private int _activeSaves;
    private bool _isDisposed;

    public DubbingVoiceAssignmentSaveQueue(
        Func<bool> isEnabled,
        Action<string, IReadOnlyList<SubtitleData>> save,
        TimeSpan delay)
        : this(isEnabled, save, delay, null)
    {
    }

    internal DubbingVoiceAssignmentSaveQueue(
        Func<bool> isEnabled,
        Action<string, IReadOnlyList<SubtitleData>> save,
        TimeSpan delay,
        Action<string>? onSaveClaimed)
    {
        _isEnabled = isEnabled;
        _save = save;
        _delay = delay;
        _onSaveClaimed = onSaveClaimed;
    }

    /// <summary>
    /// Queues an already-owned immutable snapshot for a resolved media path. The queue deliberately does not
    /// enumerate or clone the snapshot; callers must not mutate it after ownership is transferred.
    /// </summary>
    public void Enqueue(string mediaPath, IReadOnlyList<SubtitleData> ownedSnapshot)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return;

        Enqueue(DubbingVoiceAssignmentMediaTarget.FromResolvedPath(mediaPath), ownedSnapshot);
    }

    /// <summary>
    /// Queues an already-owned immutable snapshot for a media identity captured at edit time. Filesystem probing is
    /// deferred to the background worker so a slow local/UNC path never blocks the WPF dispatcher.
    /// </summary>
    public void Enqueue(DubbingVoiceAssignmentMediaTarget target, IReadOnlyList<SubtitleData> ownedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ownedSnapshot);

        if (target.IsEmpty || !_isEnabled())
            return;

        SaveRequest request;
        lock (_lock)
        {
            if (_isDisposed)
                return;

            long revision = ++_nextRevision;
            request = new SaveRequest(target, target.QueueKey, revision, ownedSnapshot);
            _pendingByMediaKey[target.QueueKey] = request;
            _latestRevisionByMediaKey[target.QueueKey] = revision;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delay).ConfigureAwait(false);
                RunSaveSafely(request);
            }
            catch
            {
                // Best-effort queue: companion persistence must never surface an unobserved background exception.
            }
        });
    }

    public void Dispose()
    {
        List<SaveRequest> pending;
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            pending = [.. _pendingByMediaKey.Values];
        }

        // A sidebar VM is disposed from WPF lifecycle code. Flush on pool threads so File.Exists, path resolution,
        // JSON and filesystem writes never execute on the dispatcher. Waiting preserves the existing durability
        // guarantee; the active-save wait below also covers delayed workers that claimed before this flush.
        if (pending.Count > 0)
        {
            // LongRunning uses a dedicated worker and cannot be inlined by this synchronous wait onto the caller.
            // A plain Task.Run + Wait can be inlined by the default scheduler, reintroducing dispatcher I/O.
            Task flushTask = Task.Factory.StartNew(
                () =>
                {
                    foreach (SaveRequest request in pending)
                        RunSaveSafely(request);
                },
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            flushTask.GetAwaiter().GetResult();
        }

        lock (_lock)
        {
            while (_activeSaves > 0)
                Monitor.Wait(_lock);
        }
    }

    private void SaveIfCurrent(SaveRequest request)
    {
        lock (_lock)
        {
            if (!_pendingByMediaKey.TryGetValue(request.MediaKey, out SaveRequest? current)
                || !ReferenceEquals(current, request))
            {
                return;
            }

            _pendingByMediaKey.Remove(request.MediaKey);
            if (!_isEnabled())
            {
                RemoveLatestRevisionLocked(request);
                return;
            }

            _activeSaves++;
        }

        try
        {
            if (!_isEnabled() || !IsLatestCapture(request))
                return;

            string? mediaPath = request.Target.ResolveLocalMediaPath();
            if (mediaPath is null)
                return;

            RecordResolvedRevision(mediaPath, request.Revision);
            _onSaveClaimed?.Invoke(mediaPath);

            lock (_saveLock)
            {
                // Authoritative checks belong immediately before the side effect. A request may have been claimed
                // while waiting behind another slow save, then superseded or disabled before it acquires this lock.
                if (!_isEnabled()
                    || !IsLatestCapture(request)
                    || !IsLatestResolvedPath(mediaPath, request.Revision))
                    return;

                _save(mediaPath, request.Subtitles);
            }
        }
        finally
        {
            lock (_lock)
            {
                _activeSaves--;
                if (!_pendingByMediaKey.ContainsKey(request.MediaKey)
                    && _latestRevisionByMediaKey.TryGetValue(request.MediaKey, out long latest)
                    && latest == request.Revision)
                {
                    RemoveLatestRevisionLocked(request);
                }
                Monitor.PulseAll(_lock);
            }
        }
    }

    private void RunSaveSafely(SaveRequest request)
    {
        try
        {
            SaveIfCurrent(request);
        }
        catch
        {
            // Best-effort persistence must not escape a background flush or strand Dispose.
        }
    }

    private bool IsLatestCapture(SaveRequest request)
    {
        lock (_lock)
        {
            return _latestRevisionByMediaKey.TryGetValue(request.MediaKey, out long latest)
                   && latest == request.Revision;
        }
    }

    private void RecordResolvedRevision(string mediaPath, long revision)
    {
        lock (_lock)
        {
            if (!_latestRevisionByResolvedPath.TryGetValue(mediaPath, out long latest) || revision > latest)
                _latestRevisionByResolvedPath[mediaPath] = revision;
        }
    }

    private bool IsLatestResolvedPath(string mediaPath, long revision)
    {
        lock (_lock)
        {
            return _latestRevisionByResolvedPath.TryGetValue(mediaPath, out long latest)
                   && latest == revision;
        }
    }

    private void RemoveLatestRevisionLocked(SaveRequest request)
    {
        if (_latestRevisionByMediaKey.TryGetValue(request.MediaKey, out long latest)
            && latest == request.Revision)
        {
            _latestRevisionByMediaKey.Remove(request.MediaKey);
        }
    }
}
