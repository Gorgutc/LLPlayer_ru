namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>
/// Reusable, isolated <c>SwrContext</c> wrapper that resamples one decoded <see cref="AVFrame"/> to interleaved
/// S16 PCM at a target sample rate + channel count. It is the single source of the swr block that was previously
/// copied byte-for-byte into <c>AudioReader.ResampleTo</c> (ASR → whisper 16 kHz mono input) and
/// <c>WaveformReader.ResampleFrame</c> (F-12 seek-bar waveform) — HC-44 slice 1. Owns its <c>SwrContext</c> and a
/// reusable output buffer (grown, never shrunk), and rebuilds the context when the incoming frame's
/// format/sample-rate/channel-layout changes mid-file. NOT thread-safe: construct one instance per decode worker
/// (as both callers do). Carries none of the ASR denoise / T-09 silence / WAV-write machinery — those stay in the
/// caller, operating on <see cref="Buffer"/> after <see cref="Resample"/>.
/// </summary>
public sealed class S16MonoResampler : IDisposable
{
    private unsafe SwrContext* _swrContext = null;

    // Reusable output buffer. Grown to the largest converted frame, never shrunk. Exposed so the caller can read
    // the converted PCM (waveform envelope, T-09 silence) or mutate it in place (F-02 high-pass) without a copy.
    private byte[] _buffer = [];
    private int _bufferSize;

    // Codec-change guard: when any of these differs from the current frame the SwrContext is rebuilt (a fresh swr
    // is required after a format/rate/layout change, exactly like AudioDecoder::RunInternal). Seeded to -1/-1/0 so
    // the very first frame is always treated as a change; the first Resample also allocates the context because
    // _swrContext is null, so the seed value never affects output.
    private int _lastFormat = -1;
    private int _lastSampleRate = -1;
    private ulong _lastChannelLayout;

    private bool _isDisposed;

    /// <summary>The converted interleaved S16 PCM from the last <see cref="Resample"/> call. Valid for the number
    /// of bytes that call returned; the array reference may change between calls when the buffer grows, so re-read
    /// this after every <see cref="Resample"/>.</summary>
    public byte[] Buffer => _buffer;

    /// <summary>
    /// Resamples <paramref name="frame"/> to interleaved S16 at <paramref name="targetSampleRate"/> Hz /
    /// <paramref name="targetChannel"/> channels into <see cref="Buffer"/>, rebuilding the <c>SwrContext</c> on a
    /// codec change. Returns the number of PCM bytes produced. Throws on a native swr failure.
    /// </summary>
    public unsafe int Resample(AVFrame* frame, int targetSampleRate, int targetChannel)
    {
        bool codecChanged = DetectCodecChange(frame->format, frame->sample_rate, frame->ch_layout.u.mask);

        // Reinitialize SwrContext because the codec changed (a native error occurs otherwise).
        if (_swrContext != null && codecChanged)
        {
            fixed (SwrContext** ptr = &_swrContext)
            {
                swr_free(ptr);
            }
            _swrContext = null;
        }

        if (_swrContext == null)
        {
            AVChannelLayout outLayout;
            av_channel_layout_default(&outLayout, targetChannel);

            // NOTE: important to reuse this context across frames.
            fixed (SwrContext** ptr = &_swrContext)
            {
                swr_alloc_set_opts2(
                    ptr,
                    &outLayout,
                    AVSampleFormat.S16,
                    targetSampleRate,
                    &frame->ch_layout,
                    (AVSampleFormat)frame->format,
                    frame->sample_rate,
                    0, null)
                    .ThrowExceptionIfError("swr_alloc_set_opts2");

                swr_init(_swrContext)
                    .ThrowExceptionIfError("swr_init");
            }
        }

        // ffmpeg ref: https://github.com/FFmpeg/FFmpeg/blob/504df09c34607967e4109b7b114ee084cf15a3ae/libavfilter/af_aresample.c#L171-L227
        long delay = swr_get_delay(_swrContext, targetSampleRate);
        int nOut = ComputeOutputSampleCapacity(frame->nb_samples, frame->sample_rate, targetSampleRate, delay);

        int needed = nOut * targetChannel * sizeof(ushort);
        EnsureCapacity(needed);

        int samplesPerChannel;
        fixed (byte* dst = _buffer)
        {
            samplesPerChannel = swr_convert(
                _swrContext,
                &dst,
                nOut,
                frame->extended_data,
                frame->nb_samples);
        }
        samplesPerChannel.ThrowExceptionIfError("swr_convert");

        return samplesPerChannel * targetChannel * sizeof(ushort);
    }

    /// <summary>Pure codec-change detector: records the frame's format/sample-rate/channel-layout and returns whether
    /// any of them differed from the previous frame (so the caller rebuilds the SwrContext). Internal for testing.</summary>
    internal bool DetectCodecChange(int format, int sampleRate, ulong channelLayout)
    {
        bool codecChanged = false;

        if (_lastFormat != format) { _lastFormat = format; codecChanged = true; }
        if (_lastSampleRate != sampleRate) { _lastSampleRate = sampleRate; codecChanged = true; }
        if (_lastChannelLayout != channelLayout) { _lastChannelLayout = channelLayout; codecChanged = true; }

        return codecChanged;
    }

    /// <summary>Pure output-sample capacity for one conversion: the resampled sample count plus a 32-sample pad and
    /// swr's reported internal delay (clamped so a large delay cannot exceed the buffer it is padding). Internal for
    /// testing.</summary>
    internal static int ComputeOutputSampleCapacity(int inNbSamples, int inSampleRate, int targetSampleRate, long delay)
    {
        double ratio = targetSampleRate * 1.0 / inSampleRate; // 16000:44100 = 0.36281179138321995
        int nOut = (int)(inNbSamples * ratio) + 32;

        if (delay > 0)
        {
            nOut += (int)Math.Min(delay, Math.Max(4096, nOut));
        }

        return nOut;
    }

    /// <summary>Grows <see cref="Buffer"/> to at least <paramref name="neededBytes"/>, never shrinking it. Internal
    /// for testing.</summary>
    internal void EnsureCapacity(int neededBytes)
    {
        if (_bufferSize < neededBytes)
        {
            _buffer = new byte[neededBytes];
            _bufferSize = neededBytes;
        }
    }

    public unsafe void Dispose()
    {
        if (_isDisposed)
            return;

        if (_swrContext != null)
        {
            fixed (SwrContext** ptr = &_swrContext)
            {
                swr_free(ptr);
            }
            _swrContext = null;
        }

        _isDisposed = true;
    }
}
