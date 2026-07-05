using System;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaStream;
using static FlyleafLib.Logger;

namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>
/// Offline whole-file audio decoder for the opt-in seek-bar waveform (F-12). It opens its OWN isolated
/// <see cref="Demuxer"/> + <see cref="AudioDecoder"/> + <c>SwrContext</c> on the media URL — the sanctioned
/// "second <c>avformat_open_input</c>" pattern the ASR <c>AudioReader</c> already uses (a second decode must
/// never share the playing <c>AVFormatContext</c>; see the media-runtime contract) — and runs a single forward
/// pass t=0..EOF, resampling each frame to S16 mono 16 kHz and folding it into a pure
/// <see cref="WaveformPeakBuilder"/>. It carries NONE of the ASR denoise/silence/chunking machinery: a raw
/// max-abs envelope only. Fully cancellable (the demuxer Interrupter is wired to the token, so a long decode
/// aborts promptly) and <see cref="IDisposable"/>: every native handle is freed on every exit path, exactly
/// like <c>AudioReader</c>. Mirrors that class's open/decode/dispose so the native lifetime stays proven.
/// </summary>
public sealed class WaveformReader : IDisposable
{
    private readonly Config _config;

    private Demuxer? _demuxer;
    private AudioDecoder? _decoder;
    private AudioStream? _stream;

    private unsafe AVPacket* _packet = null;
    private unsafe AVFrame* _frame = null;

    // Shared swr → S16 mono 16k resampler (HC-44 slice 1). Owns the SwrContext + reusable output buffer and
    // rebuilds on a mid-file codec change; the same helper backs AudioReader.ResampleTo.
    private readonly S16MonoResampler _resampler = new();

    private const int TargetSampleRate = 16000;
    private const int TargetChannel = 1;

    private bool _isDisposed;

    private readonly LogHandler Log;

    public WaveformReader(Config config)
    {
        _config = config;
        Log = new LogHandler(("[#1]").PadRight(8, ' ') + " [WaveformReader] ");
    }

    /// <summary>
    /// Opens an independent demuxer + audio decoder on <paramref name="url"/> for <paramref name="streamIndex"/>
    /// (mirrors <c>AudioReader.Open</c>). The cancellation token force-interrupts the demuxer so a slow open aborts.
    /// </summary>
    public void Open(string url, int streamIndex, MediaType type, CancellationToken token)
    {
        _demuxer = OfflineDemuxer.OpenIsolated(_config, type, 1, "DemuxerW:", url, token, out string? error);

        if (error != null)
        {
            if (token.IsCancellationRequested)
                return;

            throw new InvalidOperationException($"demuxer open error: {error}");
        }

        _stream = (AudioStream)_demuxer.AVStreamToStream[streamIndex];

        _decoder = new AudioDecoder(_config, 1);
        _decoder.Log.Prefix = _decoder.Log.Prefix.Replace("Decoder: ", "DecoderW:");

        if (!_decoder.Open(_stream))
        {
            if (token.IsCancellationRequested)
                return;

            throw new InvalidOperationException("decoder open error");
        }
    }

    /// <summary>
    /// Decodes the whole audio stream once (t=0..EOF) into <paramref name="bucketCount"/> max-abs peak buckets
    /// spanning <paramref name="totalDurationTicks"/>. Returns the 0..1 peaks, or <c>null</c> if cancelled.
    /// <paramref name="progress"/> (optional) is reported 0..1 as decoding advances. Throws only on a native
    /// decode failure (the caller runs this in a fail-soft worker).
    /// </summary>
    public unsafe float[]? ReadPeaks(int bucketCount, long totalDurationTicks, IProgress<double>? progress, CancellationToken token)
    {
        if (_demuxer == null || _decoder == null || _stream == null)
            throw new InvalidOperationException("Open() is not called");

        _packet = av_packet_alloc();
        _frame = av_frame_alloc();

        WaveformPeakBuilder builder = new(bucketCount, totalDurationTicks, TargetSampleRate);

        long framePts = NoTs;
        int demuxErrors = 0;
        int decodeErrors = 0;
        int frameCounter = 0;

        while (!token.IsCancellationRequested)
        {
            _demuxer.Interrupter.ReadRequest();
            int ret = av_read_frame(_demuxer.fmtCtx, _packet);

            if (ret != 0)
            {
                av_packet_unref(_packet);

                // EOF, cancellation, or a timed-out interrupt all end the single pass.
                if (ret == AVERROR_EOF || token.IsCancellationRequested || _demuxer.Interrupter.Timedout)
                    break;

                if (CanWarn) Log.Warn($"av_read_frame: {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
                if (++demuxErrors == _config.Demuxer.MaxErrors)
                    break;
                continue;
            }

            // Only the selected audio stream.
            if (_packet->stream_index != _stream.StreamIndex)
            {
                av_packet_unref(_packet);
                continue;
            }

            ret = avcodec_send_packet(_decoder.CodecCtx, _packet);
            av_packet_unref(_packet);

            if (ret != 0)
            {
                if (ret == AVERROR(EAGAIN))
                    break; // send+receive both EAGAIN would be an API violation; stop the pass

                if (CanWarn) Log.Warn($"avcodec_send_packet: {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
                if (++decodeErrors == _config.Decoder.MaxErrors)
                    break;
                continue;
            }

            while (ret >= 0)
            {
                ret = avcodec_receive_frame(_decoder.CodecCtx, _frame);
                if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF || ret < 0)
                    break;

                if (_frame->best_effort_timestamp != NoTs)
                    framePts = _frame->best_effort_timestamp;
                else if (_frame->pts != NoTs)
                    framePts = _frame->pts;
                else
                    framePts = framePts == NoTs ? 0 : framePts + _frame->duration;

                long frameStartTicks = (long)(framePts * _stream.Timebase) - _demuxer.StartTime;
                if (frameStartTicks < 0)
                    frameStartTicks = 0;

                int resampledDataSize = _resampler.Resample(_frame, TargetSampleRate, TargetChannel);
                if (resampledDataSize > 0)
                    builder.Add(_resampler.Buffer, resampledDataSize, frameStartTicks);

                // Report progress occasionally (every 64 frames) to avoid flooding the UI marshaller.
                if (progress != null && totalDurationTicks > 0 && (++frameCounter & 0x3F) == 0)
                {
                    double p = frameStartTicks / (double)totalDurationTicks;
                    progress.Report(p < 0 ? 0 : (p > 1 ? 1 : p));
                }
            }
        }

        if (token.IsCancellationRequested)
            return null;

        progress?.Report(1.0);
        return builder.Build();
    }

    public unsafe void Dispose()
    {
        if (_isDisposed)
            return;

        if (_frame != null)
        {
            fixed (AVFrame** ptr = &_frame)
                av_frame_free(ptr);
        }

        if (_packet != null)
        {
            fixed (AVPacket** ptr = &_packet)
                av_packet_free(ptr);
        }

        _resampler.Dispose();

        _decoder?.Dispose();
        OfflineDemuxer.DisposeIsolated(_demuxer);

        _isDisposed = true;
    }
}
