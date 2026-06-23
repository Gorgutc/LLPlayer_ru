using System.Linq;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

/// <summary>One selectable audio track of a media file (for the batch audio-track picker).</summary>
public sealed record MediaAudioTrack(
    int StreamIndex,
    Language Language,
    string? Title,
    string? Codec,
    int Channels,
    long BitRate = 0)
{
    /// <summary>True when this track's language tag is Russian (matches both "ru" and "rus" via ISO6391).</summary>
    public bool IsRussian => Language?.ISO6391 == "ru";

    /// <summary>Human-readable label for the picker, e.g. "#2: Russian — Commentary (2ch)".</summary>
    public string Display
    {
        get
        {
            if (StreamIndex < 0)
                return Title ?? "Auto"; // the "Auto" sentinel used by the batch picker

            string lang = Language is { ISO6391.Length: > 0 } ? Language.TopEnglishName : "Unknown";
            string title = string.IsNullOrWhiteSpace(Title) ? string.Empty : $" — {Title}";
            string ch = Channels > 0 ? $" ({Channels}ch)" : string.Empty;
            return $"#{StreamIndex}: {lang}{title}{ch}";
        }
    }
}

public sealed record MediaAudioProbeResult(
    string MediaPath,
    int StreamIndex,
    MediaType MediaType,
    Language Language,
    TimeSpan Duration,
    IReadOnlyList<MediaAudioTrack> Tracks,
    bool LanguageTagged);

public sealed class MediaAudioProbe
{
    private readonly Config _config;

    public MediaAudioProbe(Config config)
    {
        _config = config;
    }

    /// <summary>
    /// Probe a media file: enumerate its audio tracks and choose the one to transcribe.
    /// </summary>
    /// <param name="forcedStreamIndex">When set and present, use exactly this track (manual override).</param>
    /// <param name="preferRussian">When true, auto-select a Russian-tagged track if one exists (so ASR yields
    /// Russian subtitles and translation is skipped) before falling back to the configured language order.</param>
    public MediaAudioProbeResult Probe(
        string mediaPath, CancellationToken token, int? forcedStreamIndex = null, bool preferRussian = false)
    {
        token.ThrowIfCancellationRequested();

        Demuxer demuxer = new(_config.Demuxer, MediaType.Video, -1, false);
        using CancellationTokenRegistration registration = token.Register(() =>
        {
            demuxer.Interrupter.ForceInterrupt = 1;
        });

        try
        {
            string? error = demuxer.Open(mediaPath);
            if (error != null)
            {
                if (token.IsCancellationRequested)
                    throw new OperationCanceledException(token);

                throw new InvalidOperationException($"demuxer open error: {error}");
            }

            List<AudioStream> streams = demuxer.AudioStreams.ToList();

            List<MediaAudioTrack> tracks = streams
                .OrderBy(s => s.StreamIndex)
                .Select(s => new MediaAudioTrack(
                    s.StreamIndex,
                    s.Language ?? Language.Unknown,
                    s.Title,
                    s.Codec,
                    s.Channels,
                    s.BitRate))
                .ToList();

            bool languageTagged = tracks.Any(t => t.Language != null && t.Language != Language.Unknown);

            int? chosenIndex = SelectTrackIndex(tracks, forcedStreamIndex, preferRussian, _config.Audio.Languages);
            AudioStream? stream = chosenIndex == null
                ? null
                : streams.FirstOrDefault(s => s.StreamIndex == chosenIndex.Value);
            if (stream == null)
                throw new InvalidOperationException("No audio stream found.");

            return new MediaAudioProbeResult(
                mediaPath,
                stream.StreamIndex,
                demuxer.Type,
                stream.Language ?? Language.Unknown,
                demuxer.Duration > 0 ? new TimeSpan(demuxer.Duration) : TimeSpan.Zero,
                tracks,
                languageTagged);
        }
        finally
        {
            demuxer.Dispose();
        }
    }

    // Precedence: explicit per-file override -> a Russian-tagged track (when preferRussian) -> the configured
    // Audio.Languages preference order -> highest-bitrate / most-channels / lowest-index. Pure and static so
    // the selection policy is unit-testable without constructing native AudioStream objects.
    public static int? SelectTrackIndex(
        IReadOnlyList<MediaAudioTrack> tracks,
        int? forcedStreamIndex,
        bool preferRussian,
        IReadOnlyList<Language> preferredLanguages)
    {
        if (tracks.Count == 0)
            return null;

        if (forcedStreamIndex.HasValue && tracks.Any(t => t.StreamIndex == forcedStreamIndex.Value))
            return forcedStreamIndex.Value;
        // A stale override (track no longer present) falls through to automatic selection.

        if (preferRussian)
        {
            MediaAudioTrack? russian = tracks.FirstOrDefault(t => t.IsRussian);
            if (russian != null)
                return russian.StreamIndex;
        }

        foreach (Language language in preferredLanguages)
        {
            MediaAudioTrack? match = tracks.FirstOrDefault(t => t.Language == language);
            if (match != null)
                return match.StreamIndex;
        }

        return tracks
            .OrderByDescending(t => t.BitRate > 0)
            .ThenByDescending(t => t.BitRate)
            .ThenByDescending(t => t.Channels)
            .ThenBy(t => t.StreamIndex)
            .First()
            .StreamIndex;
    }
}
