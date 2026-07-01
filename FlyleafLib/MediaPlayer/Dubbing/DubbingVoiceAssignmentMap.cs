using System;
using System.Collections.Generic;
using System.IO;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Applies current-session per-line dub voice choices to subtitles that are about to be rendered by batch dubbing.
/// The map is an immutable snapshot: it carries only media path + millisecond timings + voice id, never live WPF
/// subtitle objects.
/// </summary>
public interface IDubbingVoiceAssignmentProvider
{
    void Apply(string mediaPath, IReadOnlyList<SubtitleData> subtitles);
}

public sealed class DubbingVoiceAssignmentMap : IDubbingVoiceAssignmentProvider
{
    public static DubbingVoiceAssignmentMap Empty { get; } = new([]);

    private readonly Dictionary<string, Dictionary<(long StartMs, long EndMs), string>> _byMediaPath;

    private DubbingVoiceAssignmentMap(Dictionary<string, Dictionary<(long StartMs, long EndMs), string>> byMediaPath)
    {
        _byMediaPath = byMediaPath;
    }

    public static DubbingVoiceAssignmentMap FromCurrentSubtitles(
        string? mediaPath,
        IEnumerable<SubtitleData>? subtitles)
    {
        string? normalizedPath = NormalizePath(mediaPath);
        if (normalizedPath is null || subtitles is null)
            return Empty;

        Dictionary<(long StartMs, long EndMs), string> byTiming = [];
        foreach (SubtitleData sub in subtitles)
        {
            if (string.IsNullOrWhiteSpace(sub.AssignedVoiceId))
                continue;

            var key = TimingKey(sub);
            byTiming.TryAdd(key, sub.AssignedVoiceId.Trim());
        }

        if (byTiming.Count == 0)
            return Empty;

        return new DubbingVoiceAssignmentMap(
            new Dictionary<string, Dictionary<(long StartMs, long EndMs), string>>(StringComparer.OrdinalIgnoreCase)
            {
                [normalizedPath] = byTiming
            });
    }

    public void Apply(string mediaPath, IReadOnlyList<SubtitleData> subtitles)
    {
        string? normalizedPath = NormalizePath(mediaPath);
        if (normalizedPath is null || !_byMediaPath.TryGetValue(normalizedPath, out var byTiming))
            return;

        foreach (SubtitleData sub in subtitles)
        {
            if (!string.IsNullOrWhiteSpace(sub.AssignedVoiceId))
                continue;

            if (byTiming.TryGetValue(TimingKey(sub), out string? voiceId))
                sub.AssignedVoiceId = voiceId;
        }
    }

    private static (long StartMs, long EndMs) TimingKey(SubtitleData sub)
        => (ToSrtMilliseconds(sub.StartTime), ToSrtMilliseconds(sub.EndTime));

    private static long ToSrtMilliseconds(TimeSpan time)
        => (long)Math.Max(0, time.TotalMilliseconds);

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string trimmed = path.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }
}
