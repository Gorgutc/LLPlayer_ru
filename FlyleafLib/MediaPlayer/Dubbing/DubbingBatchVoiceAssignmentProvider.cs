using System.Collections.Generic;
using System.Linq;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>Current-session clear claims exclude only their timings from the disk underlay before a queued save flushes.</summary>
public sealed class DubbingBatchVoiceAssignmentProvider : IDubbingVoiceAssignmentProvider
{
    readonly DubbingVoiceAssignmentMap current;
    readonly string? sourcePath;
    readonly HashSet<(long, long)> cleared;
    readonly bool persist;

    public DubbingBatchVoiceAssignmentProvider(string? mediaPath, IReadOnlyList<SubtitleData> snapshot,
        IReadOnlyList<SubtitleData> clearSnapshot, bool persist)
    {
        sourcePath = string.IsNullOrWhiteSpace(mediaPath) ? null : Path.GetFullPath(mediaPath);
        current = DubbingVoiceAssignmentMap.FromCurrentSubtitles(mediaPath, snapshot);
        cleared = clearSnapshot.Select(cue => (DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.StartTime),
            DubbingVoiceAssignmentMap.ToTimingMilliseconds(cue.EndTime))).ToHashSet();
        this.persist = persist;
    }

    public void Apply(string mediaPath, IReadOnlyList<SubtitleData> subtitles)
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
