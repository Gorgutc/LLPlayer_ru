namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Debounces per-line voice companion saves per media path. A burst for the same media keeps only the latest
/// snapshot, while edits on different media are flushed independently.
/// </summary>
public sealed class DubbingVoiceAssignmentSaveQueue : IDisposable
{
    private sealed record SaveRequest(string MediaPath, IReadOnlyList<SubtitleData> Subtitles);

    private readonly Func<bool> _isEnabled;
    private readonly Action<string, IReadOnlyList<SubtitleData>> _save;
    private readonly Action<string>? _onSaveClaimed;
    private readonly TimeSpan _delay;
    private readonly object _lock = new();
    private readonly object _saveLock = new();
    private readonly Dictionary<string, SaveRequest> _pendingByMediaPath = new(StringComparer.OrdinalIgnoreCase);
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

    public void Enqueue(string mediaPath, IEnumerable<SubtitleData> subtitles)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !_isEnabled())
            return;

        SaveRequest request = new(mediaPath, Snapshot(subtitles));
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _pendingByMediaPath[mediaPath] = request;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delay).ConfigureAwait(false);
                SaveIfCurrent(request);
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
            pending = [.. _pendingByMediaPath.Values];
        }

        foreach (SaveRequest request in pending)
            SaveIfCurrent(request);

        lock (_lock)
        {
            while (_activeSaves > 0)
                Monitor.Wait(_lock);
        }
    }

    private void SaveIfCurrent(SaveRequest request)
    {
        bool shouldSave;
        lock (_lock)
        {
            if (!_pendingByMediaPath.TryGetValue(request.MediaPath, out SaveRequest? current)
                || !ReferenceEquals(current, request))
            {
                return;
            }

            _pendingByMediaPath.Remove(request.MediaPath);
            shouldSave = _isEnabled();
            if (shouldSave)
            {
                _activeSaves++;
                _onSaveClaimed?.Invoke(request.MediaPath);
            }
        }

        if (!shouldSave)
            return;

        try
        {
            lock (_saveLock)
            {
                _save(request.MediaPath, request.Subtitles);
            }
        }
        finally
        {
            lock (_lock)
            {
                _activeSaves--;
                Monitor.PulseAll(_lock);
            }
        }
    }

    private static IReadOnlyList<SubtitleData> Snapshot(IEnumerable<SubtitleData> subtitles)
    {
        List<SubtitleData> snapshot = [];
        foreach (SubtitleData sub in subtitles)
        {
            snapshot.Add(new SubtitleData
            {
                StartTime = sub.StartTime,
                EndTime = sub.EndTime,
                AssignedVoiceId = sub.AssignedVoiceId,
            });
        }

        return snapshot;
    }
}
