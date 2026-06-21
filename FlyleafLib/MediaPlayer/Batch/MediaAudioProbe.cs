using System.Linq;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public sealed record MediaAudioProbeResult(
    string MediaPath,
    int StreamIndex,
    MediaType MediaType,
    Language Language,
    TimeSpan Duration);

public sealed class MediaAudioProbe
{
    private readonly Config _config;

    public MediaAudioProbe(Config config)
    {
        _config = config;
    }

    public MediaAudioProbeResult Probe(string mediaPath, CancellationToken token)
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

            AudioStream? stream = SelectBestAudioStream(demuxer.AudioStreams);
            if (stream == null)
                throw new InvalidOperationException("No audio stream found.");

            return new MediaAudioProbeResult(
                mediaPath,
                stream.StreamIndex,
                demuxer.Type,
                stream.Language ?? Language.Unknown,
                demuxer.Duration > 0 ? new TimeSpan(demuxer.Duration) : TimeSpan.Zero);
        }
        finally
        {
            demuxer.Dispose();
        }
    }

    private AudioStream? SelectBestAudioStream(IEnumerable<AudioStream> audioStreams)
    {
        List<AudioStream> streams = audioStreams.ToList();

        foreach (Language language in _config.Audio.Languages)
        {
            AudioStream? languageMatch = streams.FirstOrDefault(stream => stream.Language == language);
            if (languageMatch != null)
                return languageMatch;
        }

        return streams
            .OrderByDescending(s => s.BitRate > 0)
            .ThenByDescending(s => s.BitRate)
            .ThenByDescending(s => s.Channels)
            .ThenBy(s => s.StreamIndex)
            .FirstOrDefault();
    }
}
