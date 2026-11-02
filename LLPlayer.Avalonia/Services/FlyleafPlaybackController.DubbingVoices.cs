using FlyleafLib.MediaPlayer.Dubbing;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    readonly DubbingVoiceRestoreQueue voiceRestoreQueue = new();
    sealed class VoiceClearClaims
    {
        public SubtitleCueSource? Source;
        public Dictionary<FlyleafLib.MediaPlayer.SubtitleData, long> Edits { get; } = [];
    }
    readonly object voiceClearClaimsGate = new();
    readonly VoiceClearClaims[] voiceClearClaims = [new(), new()];
    long voiceEditRevision;

    long RecordDubbingVoiceEdit(SubtitleCueToken token)
    {
        lock (voiceClearClaimsGate)
        {
            VoiceClearClaims claims = voiceClearClaims[token.Source.Slot];
            if (!Equals(claims.Source, token.Source)) { claims.Source = token.Source; claims.Edits.Clear(); }
            PruneVoiceEdits(claims, bridge.Snapshot(token.Source.Manager.Subs));
            return claims.Edits[token.Cue] = ++voiceEditRevision;
        }
    }

    static void PruneVoiceEdits(VoiceClearClaims claims, IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> current)
    {
        var retained = current.ToHashSet();
        foreach (var cue in claims.Edits.Keys.Where(cue => !retained.Contains(cue)).ToArray()) claims.Edits.Remove(cue);
    }

    IReadOnlyList<DubbingVoiceClaim> CaptureDubbingVoiceEdits(SubtitleCueSource?[] sources)
    {
        List<DubbingVoiceClaim> edits = [];
        lock (voiceClearClaimsGate)
            for (int slot = 0; slot < sources.Length; slot++)
            {
                VoiceClearClaims claims = voiceClearClaims[slot];
                if (sources[slot] == null || !Equals(claims.Source, sources[slot])) continue;
                PruneVoiceEdits(claims, bridge.Snapshot(sources[slot]!.Manager.Subs));
                edits.AddRange(claims.Edits.Select(edit => new DubbingVoiceClaim(edit.Value,
                    edit.Key.StartTime, edit.Key.EndTime, edit.Key.AssignedVoiceId)));
            }
        return DubbingVoicePersistence.MergeEditClaims(edits, []);
    }

    /// <summary>Called inside a validated per-cue edit before assignment, including an explicit clear to null.</summary>
    public void NotifyDubbingVoiceEdited(int slot)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        if (!player.Config.Subtitles.PersistPerLineVoices) return;
        SubtitleCueSource? source = CaptureSubtitleCueSource(slot);
        string? media = LocalDubbingMediaPath(CurrentMediaPath);
        if (source != null && media != null) voiceRestoreQueue.Suppress(slot, (source, media));
    }

    /// <summary>Restore opt-in companion assignments after a concrete non-ASR cue source becomes ready.</summary>
    public Task RestoreDubbingVoicesAsync(int slot)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        if (disposed || !player.Config.Subtitles.PersistPerLineVoices) return Task.CompletedTask;
        SubtitleCueSource? source = CaptureSubtitleCueSource(slot);
        if (source == null || source.IsASR || source.Manager.IsLoading || bridge.Snapshot(source.Manager.Subs).Count == 0)
            return Task.CompletedTask;
        string? media = LocalDubbingMediaPath(CurrentMediaPath);
        if (media == null || !IsCurrentSubtitleCueSource(source)) return Task.CompletedTask;

        return voiceRestoreQueue.RestoreAsync(slot, (source, media),
            () => File.Exists(media) ? DubbingVoiceAssignmentStore.LoadMap(media) : DubbingVoiceAssignmentMap.Empty,
            async (map, requestCurrent) =>
            {
                bool applied = false;
                await Recognition.MutateAsync(cancellation =>
                {
                    bool Current() => requestCurrent() && !disposed && player.Config.Subtitles.PersistPerLineVoices
                        && !source.IsASR && !source.Manager.IsLoading && IsCurrentSubtitleCueSource(source)
                        && string.Equals(LocalDubbingMediaPath(CurrentMediaPath), media,
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                    if (!Current()) return;
                    player.TryMutateMedia(source.Media.Generation, source.Media.Item, Current, () =>
                    {
                        var cues = bridge.Snapshot(source.Manager.Subs);
                        if (cues.Count == 0) return;
                        // Existing Apply matches exact SRT-ms timing and fills only empty overrides, so a user
                        // edit that happened while disk I/O or the mutation queue was pending always wins.
                        lock (voiceClearClaimsGate)
                        {
                            VoiceClearClaims claims = voiceClearClaims[slot];
                            IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> restoreTargets = Equals(claims.Source, source)
                                ? cues.Where(cue => !claims.Edits.ContainsKey(cue)).ToArray() : cues;
                            map.Apply(media, restoreTargets);
                        }
                        applied = true;
                    }, cancellation);
                }).ConfigureAwait(false);
                if (applied) QueueCueNotification(slot);
                return applied;
            });
    }

    public Task DrainVoiceRestoreAsync() => voiceRestoreQueue.DrainAsync();

    static string? LocalDubbingMediaPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            if (!uri.IsFile) return null;
            value = uri.LocalPath;
        }
        try { return Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null; }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return null; }
    }
}

/// <summary>
/// Two bounded slot caches. Repeated notifications share one read; source replacement coalesces to the newest
/// request while a previous read drains. A deferred apply can prove it still owns its slot before mutation.
/// </summary>
internal sealed class DubbingVoiceRestoreQueue
{
    sealed class Request(object key, Func<DubbingVoiceAssignmentMap> load,
        Func<DubbingVoiceAssignmentMap, Func<bool>, Task<bool>> apply)
    {
        public object Key { get; } = key;
        public Func<DubbingVoiceAssignmentMap> Load { get; } = load;
        public Func<DubbingVoiceAssignmentMap, Func<bool>, Task<bool>> Apply { get; } = apply;
        public DubbingVoiceAssignmentMap? Map;
        public bool Applied;
    }
    sealed class Slot
    {
        public Request? Current;
        public object? SuppressedKey;
        public bool Dirty, Running;
        public Task Pump = Task.CompletedTask;
    }
    readonly object gate = new();
    readonly Slot[] slots = [new(), new()];
    bool stopped;

    public Task RestoreAsync(int slotIndex, object key, Func<DubbingVoiceAssignmentMap> load,
        Func<DubbingVoiceAssignmentMap, Func<bool>, Task<bool>> apply)
    {
        lock (gate)
        {
            if (stopped) return Task.CompletedTask;
            Slot slot = slots[slotIndex];
            if (Equals(slot.SuppressedKey, key)) return slot.Pump;
            if (slot.Current == null || !Equals(slot.Current.Key, key))
            {
                slot.Current = new(key, load, apply);
                slot.SuppressedKey = null;
            }
            if (slot.Current.Applied) return slot.Pump;
            slot.Dirty = true;
            if (!slot.Running)
            {
                slot.Running = true;
                slot.Pump = Task.Run(() => PumpAsync(slot));
            }
            return slot.Pump;
        }
    }

    public void Suppress(int slotIndex, object key)
    {
        lock (gate)
        {
            if (stopped) return;
            Slot slot = slots[slotIndex];
            slot.SuppressedKey = key;
            if (slot.Current != null && Equals(slot.Current.Key, key)) slot.Dirty = false;
        }
    }

    async Task PumpAsync(Slot slot)
    {
        while (true)
        {
            Request request;
            lock (gate)
            {
                if (stopped || !slot.Dirty || slot.Current == null) { slot.Running = false; return; }
                slot.Dirty = false;
                request = slot.Current;
            }
            bool Current()
            {
                lock (gate) return !stopped && ReferenceEquals(slot.Current, request)
                    && !Equals(slot.SuppressedKey, request.Key);
            }
            try
            {
                request.Map ??= request.Load(); // Pump always starts on the pool; never performs UI-thread I/O.
                if (!Current()) continue;
                bool applied = await request.Apply(request.Map, Current).ConfigureAwait(false);
                lock (gate)
                {
                    if (Current() && applied)
                    {
                        request.Applied = true;
                        slot.Dirty = false;
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                // Convenience-only restore. Keep the cached read for a later ready notification, and never
                // surface stale/shutdown exceptions through a fire-and-forget media event.
            }
        }
    }

    public async Task DrainAsync()
    {
        Task[] pending;
        lock (gate)
        {
            stopped = true;
            foreach (Slot slot in slots) { slot.Current = null; slot.SuppressedKey = null; slot.Dirty = false; }
            pending = slots.Select(slot => slot.Pump).ToArray();
        }
        await Task.WhenAll(pending).ConfigureAwait(false);
    }
}

/// <summary>Merge owned cue timing claims (including explicit clears) over untouched companion entries.</summary>
internal static class DubbingVoicePersistence
{
    internal static IReadOnlyList<DubbingVoiceClaim> MergeEditClaims(IReadOnlyList<DubbingVoiceClaim> current,
        IReadOnlyList<DubbingVoiceClaim> pending)
    {
        HashSet<(long, long)> seen = [];
        return current.Concat(pending).OrderByDescending(claim => claim.Revision).Where(claim => seen.Add((
            DubbingVoiceAssignmentMap.ToTimingMilliseconds(claim.Start),
            DubbingVoiceAssignmentMap.ToTimingMilliseconds(claim.End)))).ToArray();
    }
    internal static IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> MergeClaims(
        IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> newest,
        IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> previous)
    {
        HashSet<(long, long)> seen = [];
        List<FlyleafLib.MediaPlayer.SubtitleData> merged = [];
        foreach (var cue in newest.Concat(previous))
            if (seen.Add((DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.StartTime),
                DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.EndTime))))
                merged.Add(new() { StartTime = cue.StartTime, EndTime = cue.EndTime, AssignedVoiceId = cue.AssignedVoiceId });
        return merged;
    }
    public static void SaveMerged(string mediaPath, IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> snapshot)
    {
        IReadOnlyList<VoiceAssignmentEntry> persisted = [];
        try { persisted = DubbingVoiceAssignmentStore.FromJson(File.ReadAllText(DubbingVoiceAssignmentStore.BuildVoicesPath(mediaPath))); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return; }
        DubbingVoiceAssignmentStore.SaveAtomic(mediaPath, Merge(snapshot, persisted));
    }

    internal static IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> Merge(
        IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> snapshot, IReadOnlyList<VoiceAssignmentEntry> persisted)
    {
        HashSet<(long, long)> seen = [];
        List<FlyleafLib.MediaPlayer.SubtitleData> merged = [];
        foreach (var cue in snapshot)
        {
            var key = (DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.StartTime), DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.EndTime));
            if (seen.Add(key)) merged.Add(new() { StartTime = cue.StartTime, EndTime = cue.EndTime, AssignedVoiceId = cue.AssignedVoiceId });
        }
        foreach (var entry in persisted)
            if (seen.Add((entry.StartMs, entry.EndMs)))
                merged.Add(new() { StartTime = TimeSpan.FromMilliseconds(entry.StartMs), EndTime = TimeSpan.FromMilliseconds(entry.EndMs), AssignedVoiceId = entry.VoiceId });
        return merged;
    }
}

internal sealed record DubbingVoiceClaim(long Revision, TimeSpan Start, TimeSpan End, string? VoiceId)
{
    public FlyleafLib.MediaPlayer.SubtitleData ToSubtitle() => new()
    { StartTime = Start, EndTime = End, AssignedVoiceId = VoiceId };
}

/// <summary>Preserve all accepted pending timing claims when the same media changes subtitle source before debounce.</summary>
internal sealed class DubbingVoiceSaveQueue : IDisposable
{
    readonly object gate = new();
    readonly Dictionary<string, IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData>> pending = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    readonly Dictionary<string, IReadOnlyList<DubbingVoiceClaim>> claimSnapshots = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    readonly DubbingVoiceAssignmentSaveQueue queue;
    bool stopped;
    long sequence;

    public DubbingVoiceSaveQueue(Func<bool> enabled, TimeSpan delay)
        => queue = new(enabled, Save, delay);

    public void Enqueue(string media, IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> snapshot, long revision = 0)
    {
        lock (gate)
        {
            if (stopped) return;
            string path = Path.GetFullPath(media);
            if (revision <= 0) revision = ++sequence;
            else sequence = Math.Max(sequence, revision);
            claimSnapshots.TryGetValue(path, out var previous);
            var claims = DubbingVoicePersistence.MergeEditClaims(snapshot.Select(cue =>
                new DubbingVoiceClaim(revision, cue.StartTime, cue.EndTime, cue.AssignedVoiceId)).ToArray(), previous ?? []);
            var merged = claims.Select(claim => claim.ToSubtitle()).ToArray();
            claimSnapshots[path] = claims;
            pending[path] = merged;
            queue.Enqueue(path, merged);
        }
    }

    public IReadOnlyList<DubbingVoiceClaim> SnapshotClaims(string media)
    {
        lock (gate) return claimSnapshots.TryGetValue(Path.GetFullPath(media), out var claims) ? claims.ToArray() : [];
    }

    void Save(string media, IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> snapshot)
    {
        DubbingVoicePersistence.SaveMerged(media, snapshot);
        lock (gate)
            if (pending.TryGetValue(media, out var latest) && ReferenceEquals(latest, snapshot))
            {
                pending.Remove(media);
                claimSnapshots.Remove(media);
            }
    }

    public void Dispose()
    {
        lock (gate) stopped = true;
        queue.Dispose();
        lock (gate) { pending.Clear(); claimSnapshots.Clear(); }
    }
}

/// <summary>Current-session clear claims exclude only their timings from the disk underlay before a queued save flushes.</summary>
internal sealed class DubbingBatchVoiceAssignmentProvider : IDubbingVoiceAssignmentProvider
{
    readonly DubbingVoiceAssignmentMap current;
    readonly string? sourcePath;
    readonly HashSet<(long, long)> cleared;
    readonly bool persist;

    public DubbingBatchVoiceAssignmentProvider(string? mediaPath,
        IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> snapshot,
        IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> clearSnapshot, bool persist)
    {
        sourcePath = string.IsNullOrWhiteSpace(mediaPath) ? null : Path.GetFullPath(mediaPath);
        current = DubbingVoiceAssignmentMap.FromCurrentSubtitles(mediaPath, snapshot);
        cleared = clearSnapshot.Select(cue => (DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.StartTime),
            DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.EndTime))).ToHashSet();
        this.persist = persist;
    }

    public void Apply(string mediaPath, IReadOnlyList<FlyleafLib.MediaPlayer.SubtitleData> subtitles)
    {
        current.Apply(mediaPath, subtitles);
        if (!persist) return;
        bool sameMedia = string.Equals(sourcePath, Path.GetFullPath(mediaPath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        var diskTargets = sameMedia ? subtitles.Where(cue => !cleared.Contains((
            DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.StartTime),
            DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.EndTime)))).ToArray() : subtitles;
        DubbingVoiceAssignmentStore.LoadMap(mediaPath).Apply(mediaPath, diskTargets);
    }
}
