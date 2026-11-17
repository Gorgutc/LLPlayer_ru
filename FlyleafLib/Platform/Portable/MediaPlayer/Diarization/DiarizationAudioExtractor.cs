#nullable enable

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaStream;
using static FlyleafLib.MediaPlayer.Diarization.DiarizationWaveWriter;

namespace FlyleafLib.MediaPlayer.Diarization;

public static class DiarizationAudioExtractor
{
    private static readonly AVRational Ticks = new() { Num = 1, Den = (int)TimeSpan.TicksPerSecond };
    private static readonly AVRational Samples = new() { Num = 1, Den = SampleRate };

    internal static TimeSpan WriteWave(DiarizationAudioSource source, Stream wave, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(source.Path) || !File.Exists(source.Path))
            throw new FileNotFoundException("Diarization requires an existing local media file.");

        Config config = new();
        // A local manifest can otherwise load remote resources through nested demuxers.
        config.Demuxer.FormatOpt["protocol_whitelist"] = "file";
        return WriteWave(config, MediaType.Video, source.Path, source.StreamIndex, wave, token, fromNetwork: false);
    }

    /// <summary>Snapshots one finite, reopenable HTTP audio stream into timestamped mono 16 kHz PCM.</summary>
    public static TimeSpan WriteNetworkWave(Config config, MediaType demuxerType, string url, int streamIndex,
        Stream wave, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")
            || demuxerType is not (MediaType.Video or MediaType.Audio) || streamIndex < 0)
            throw new InvalidDataException("A selected HTTP audio stream is required.");

        config.Demuxer.GetFormatOptPtr(demuxerType)["protocol_whitelist"] = "http,https,tcp,tls,crypto,httpproxy";
        return WriteWave(config, demuxerType, url, streamIndex, wave, token, fromNetwork: true);
    }

    private static unsafe TimeSpan WriteWave(Config config, MediaType demuxerType, string url, int streamIndex,
        Stream wave, CancellationToken token, bool fromNetwork)
    {
        using var isolated = OfflineDemuxer.OpenIsolated(config, demuxerType, 1,
            fromNetwork ? "DemuxerControlAudio:" : "DemuxerDiarization:", url, token, out string? error);
        token.ThrowIfCancellationRequested();
        if (error is not null)
            throw new IOException(fromNetwork ? "Could not reopen the selected audio source."
                : $"Could not open local audio for diarization. {error}");

        var demuxer = isolated.Demuxer;
        if (fromNetwork && (demuxer.IsLive || demuxer.Duration <= 0))
            throw new InvalidDataException("Recognition requires a finite audio source.");
        if (!demuxer.AVStreamToStream.TryGetValue(streamIndex, out var stream) || stream is not AudioStream audio)
            throw new InvalidDataException("The selected audio track is no longer available.");

        var decoder = new AudioDecoder(config, 1);

        using var resampler = new S16MonoResampler();
        var writer = new DiarizationWaveWriter(wave);
        AVRational timebase = audio.AVStream->time_base;
        long timestampPrecision = Math.Max(av_rescale_q(1, timebase, Ticks), TimeSpan.TicksPerSecond / SampleRate);
        long segmentStartTicks = 0;
        long segmentInputSamples = 0;
        long outputPosition = 0;
        int lastRate = 0;
        int lastFormat = -1;
        ulong lastLayout = 0;

        AVPacket* packet = null;
        AVFrame* frame = null;
        try
        {
            if (!decoder.Open(audio))
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidDataException("Could not decode the selected audio track.");
            }

            packet = av_packet_alloc();
            frame = av_frame_alloc();
            if (packet == null || frame == null)
                throw new OutOfMemoryException("Could not allocate the audio decoding buffers.");

            while (true)
            {
                token.ThrowIfCancellationRequested();
                demuxer.Interrupter.ReadRequest();
                int result = av_read_frame(demuxer.fmtCtx, packet);
                token.ThrowIfCancellationRequested();
                if (result == AVERROR_EOF)
                    break;
                result.ThrowExceptionIfError("Could not read the selected audio");

                if (packet->stream_index == streamIndex)
                {
                    avcodec_send_packet(decoder.CodecCtx, packet).ThrowExceptionIfError("Could not submit an audio packet");
                    Receive(decoder.CodecCtx, frame);
                }
                av_packet_unref(packet);
            }

            avcodec_send_packet(decoder.CodecCtx, null).ThrowExceptionIfError("Could not flush the audio decoder");
            Receive(decoder.CodecCtx, frame);
            DrainResampler();
            return writer.Finish();
        }
        finally
        {
            av_frame_free(&frame);
            av_packet_free(&packet);
            decoder.Dispose();
        }

        void Receive(AVCodecContext* codec, AVFrame* decoded)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int result = avcodec_receive_frame(codec, decoded);
                if (result == AVERROR(EAGAIN) || result == AVERROR_EOF)
                    return;
                result.ThrowExceptionIfError("Could not decode the selected audio");

                int rate = decoded->sample_rate;
                if (rate <= 0)
                    throw new InvalidDataException("The decoded audio has an invalid sample rate.");

                long expected = lastRate == 0
                    ? audio.StartTime - demuxer.StartTime
                    : segmentStartTicks + av_rescale(segmentInputSamples, TimeSpan.TicksPerSecond, lastRate);
                long pts = decoded->best_effort_timestamp != NoTs ? decoded->best_effort_timestamp : decoded->pts;
                long position = pts == NoTs ? expected : checked(av_rescale_q(pts, timebase, Ticks) - demuxer.StartTime);
                // The cumulative source clock absorbs rounding in coarse container timebases (e.g. Matroska's
                // milliseconds). A real discontinuity starts a fresh resampler segment at its own PTS.
                bool continuous = lastRate != 0 && Math.Abs((double)position - expected) <= timestampPrecision;
                if (!continuous || rate != lastRate || decoded->format != lastFormat || decoded->ch_layout.u.mask != lastLayout)
                {
                    DrainResampler();
                    resampler.Reset();
                    segmentStartTicks = continuous ? expected : position;
                    segmentInputSamples = 0;
                    outputPosition = av_rescale_q(segmentStartTicks, Ticks, Samples);
                    lastRate = rate;
                    lastFormat = decoded->format;
                    lastLayout = decoded->ch_layout.u.mask;
                }

                WriteResampled(resampler.Resample(decoded, SampleRate, 1));
                segmentInputSamples += decoded->nb_samples;
                av_frame_unref(decoded);
            }
        }

        void DrainResampler()
        {
            int size;
            while ((size = resampler.DrainMono(SampleRate)) != 0)
                WriteResampled(size);
        }

        void WriteResampled(int size)
        {
            writer.WriteAt(outputPosition, resampler.Buffer.AsSpan(0, size), token);
            outputPosition += size / sizeof(short);
        }
    }
}
