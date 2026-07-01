using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>One persisted per-line dub voice assignment: the cue's SRT-millisecond [start, end] window + voice id.</summary>
public sealed class VoiceAssignmentEntry
{
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string VoiceId { get; set; } = string.Empty;
}

/// <summary>Companion-file payload: a schema version + the per-line voice assignments for one media file.</summary>
public sealed class VoiceAssignmentFile
{
    public int SchemaVersion { get; set; } = 1;
    public List<VoiceAssignmentEntry> Assignments { get; set; } = [];
}

/// <summary>
/// F-16: opt-in persistence of per-line dub voice overrides (<see cref="SubtitleData.AssignedVoiceId"/>) to a
/// companion JSON file beside the media (video.ru.voices.json), so a user's sidebar voice choices survive an app
/// restart and a dub re-render (which otherwise only see the interactive/in-memory value). Pure logic (path / JSON
/// round-trip) lives here so it is unit-testable; the WPF/batch layers own WHEN to save (interactive edit + batch
/// run) and WHEN to restore (media open + batch dub). Byte-identical when off: no assignments → no file written.
///
/// The timing key mirrors <see cref="DubbingVoiceAssignmentMap"/> (SRT-millisecond [start, end]) so a persisted
/// assignment matches the same cue the in-memory snapshot would. The file name is ".ru.voices.json" (NOT
/// ".ru.dub.*") on purpose: the dub-audio detection glob "{name}.ru.dub.*" (DubbingOutputPathBuilder) would
/// otherwise treat this metadata file as an already-rendered dub and skip dubbing / mis-attach it as audio.
/// </summary>
public static class DubbingVoiceAssignmentStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>e.g. "C:\videos\movie.mkv" → "C:\videos\movie.ru.voices.json".</summary>
    public static string BuildVoicesPath(string mediaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        string? directory = Path.GetDirectoryName(mediaPath);
        string fileName = Path.GetFileNameWithoutExtension(mediaPath);

        if (string.IsNullOrWhiteSpace(directory))
            directory = Directory.GetCurrentDirectory();

        return Path.Combine(directory, $"{fileName}.ru.voices.json");
    }

    /// <summary>
    /// Serializes the non-blank per-line voice assignments of <paramref name="subtitles"/>, or returns null when
    /// there are none (the caller then writes no file, keeping the default byte-identical). The first assignment
    /// per timing window wins (mirrors the in-memory map's TryAdd).
    /// </summary>
    public static string? ToJson(IEnumerable<SubtitleData>? subtitles)
    {
        if (subtitles is null)
            return null;

        HashSet<(long, long)> seen = [];
        List<VoiceAssignmentEntry> entries = [];
        foreach (SubtitleData sub in subtitles)
        {
            if (sub is null || string.IsNullOrWhiteSpace(sub.AssignedVoiceId))
                continue;

            long startMs = DubbingVoiceAssignmentMap.ToTimingMilliseconds(sub.StartTime);
            long endMs = DubbingVoiceAssignmentMap.ToTimingMilliseconds(sub.EndTime);
            if (!seen.Add((startMs, endMs)))
                continue;

            entries.Add(new VoiceAssignmentEntry
            {
                StartMs = startMs,
                EndMs = endMs,
                VoiceId = sub.AssignedVoiceId.Trim()
            });
        }

        if (entries.Count == 0)
            return null;

        return JsonSerializer.Serialize(new VoiceAssignmentFile { Assignments = entries }, JsonOpts);
    }

    /// <summary>
    /// Parses the companion payload. Tolerant: null/blank/corrupt/wrong-shape JSON yields an empty list and never
    /// throws. Entries with a blank voice id are dropped.
    /// </summary>
    public static IReadOnlyList<VoiceAssignmentEntry> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            VoiceAssignmentFile? file = JsonSerializer.Deserialize<VoiceAssignmentFile>(json, JsonOpts);
            if (file?.Assignments is null)
                return [];

            List<VoiceAssignmentEntry> entries = [];
            foreach (VoiceAssignmentEntry entry in file.Assignments)
            {
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.VoiceId))
                    entries.Add(entry);
            }
            return entries;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Writes the companion file beside <paramref name="mediaPath"/> using an atomic temp-file+move (mirrors
    /// <c>SrtSubtitleWriter</c>). When there are no assignments, any existing companion is deleted so a cleared
    /// override does not persist. Best-effort: I/O errors are swallowed (same posture as the word-list / crash-log
    /// writes).
    /// </summary>
    public static void SaveAtomic(string? mediaPath, IEnumerable<SubtitleData>? subtitles)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return;

        string voicesPath;
        try
        {
            voicesPath = BuildVoicesPath(mediaPath);
        }
        catch
        {
            return;
        }

        string? json = ToJson(subtitles);

        try
        {
            if (json is null)
            {
                if (File.Exists(voicesPath))
                    File.Delete(voicesPath);
                return;
            }

            string? directory = Path.GetDirectoryName(voicesPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(
                directory ?? Directory.GetCurrentDirectory(),
                $".{Path.GetFileName(voicesPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                File.Move(tempPath, voicesPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
    }

    /// <summary>
    /// Reads the companion file beside <paramref name="mediaPath"/> into an immutable voice map keyed by that media
    /// path. Missing/corrupt/unreadable file → <see cref="DubbingVoiceAssignmentMap.Empty"/> (never throws).
    /// </summary>
    public static DubbingVoiceAssignmentMap LoadMap(string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return DubbingVoiceAssignmentMap.Empty;

        string voicesPath;
        try
        {
            voicesPath = BuildVoicesPath(mediaPath);
        }
        catch
        {
            return DubbingVoiceAssignmentMap.Empty;
        }

        if (!File.Exists(voicesPath))
            return DubbingVoiceAssignmentMap.Empty;

        try
        {
            string json = File.ReadAllText(voicesPath);
            return DubbingVoiceAssignmentMap.FromEntries(mediaPath, FromJson(json));
        }
        catch (IOException) { return DubbingVoiceAssignmentMap.Empty; }
        catch (UnauthorizedAccessException) { return DubbingVoiceAssignmentMap.Empty; }
        catch (System.Security.SecurityException) { return DubbingVoiceAssignmentMap.Empty; }
    }
}
