#nullable enable
using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafLib.MediaFramework.MediaDecoder;

public unsafe partial class VideoDecoder
{
    PortableHardwareRecoveryBuffer? portableRecovery;
    Queue<nint>? portableRecoveredFrames;
    int portableReplayDiscardFrames;
    readonly record struct NamedHardwareCodecKey(string Name, AVHWDeviceType Device, AVPixelFormat Format);
    static readonly ConcurrentDictionary<PortableHardwareCodecKey, CodecSpec> portableHardwareCodecs = [];
    static readonly ConcurrentDictionary<NamedHardwareCodecKey, CodecSpec> portableNamedCodecs = [];

    static CodecSpec FindPortableHWDecoder(AVCodecID id, AVHWDeviceType device, AVPixelFormat format)
    {
        var key = new PortableHardwareCodecKey(id, device, format);
        if (portableHardwareCodecs.TryGetValue(key, out var known))
            return known;
        void* opaque = null;
        AVCodec* codec;
        while ((codec = av_codec_iterate(ref opaque)) != null)
        {
            if (codec->id != id || av_codec_is_decoder(codec) == 0 || !SupportsPortableHardware(codec, device, format))
                continue;
            CodecSpec selected = new() { Codec = codec, Name = BytePtrToStringUTF8(codec->name), IsHW = true };
            portableHardwareCodecs.TryAdd(key, selected);
            return selected;
        }
        portableHardwareCodecs.TryAdd(key, CodecSpec.Empty);
        return CodecSpec.Empty;
    }

    static CodecSpec FindPortableDecoder(string name, AVHWDeviceType device, AVPixelFormat format)
    {
        var key = new NamedHardwareCodecKey(name, device, format);
        if (portableNamedCodecs.TryGetValue(key, out var known))
            return known;
        AVCodec* codec = avcodec_find_decoder_by_name(name);
        CodecSpec selected = codec == null ? CodecSpec.Empty : new()
        {
            Codec = codec, Name = BytePtrToStringUTF8(codec->name),
            IsHW = SupportsPortableHardware(codec, device, format)
        };
        portableNamedCodecs.TryAdd(key, selected);
        return selected;
    }

    static bool SupportsPortableHardware(AVCodec* codec, AVHWDeviceType device, AVPixelFormat format)
    {
        if (device == AVHWDeviceType.None || format == AVPixelFormat.None)
            return false;
        AVCodecHWConfig* configuration;
        for (int index = 0; (configuration = avcodec_get_hw_config(codec, index)) != null; index++)
            if (PortableHardwareDecodePolicy.SupportsConfiguration(configuration->device_type,
                configuration->pix_fmt, configuration->methods, device, format))
                return true;
        return false;
    }

    AVPixelFormat GetFormat(AVCodecContext* context, AVPixelFormat* candidates)
    {
        try
        {
            if (context->hw_frames_ctx != null)
                av_buffer_unref(&context->hw_frames_ctx);
            int count = 0;
            while (candidates[count] != AVPixelFormat.None) count++;
            var originalCandidates = new ReadOnlySpan<AVPixelFormat>(candidates, count);
            AVPixelFormat selected = PortableHardwareDecodePolicy.SelectFormat(originalCandidates,
                Renderer.HardwarePixelFormat, () => Renderer.ConfigHWFrames(context), IsSoftwarePixelFormat);
            if (selected == Renderer.HardwarePixelFormat && selected != AVPixelFormat.None)
            {
                context->hw_frames_ctx = av_buffer_ref(Renderer.ffFrames);
                if (context->hw_frames_ctx != null)
                    return selected;
            }
            RequestSoftwareFallback(Renderer.DecodeStatus.FallbackReason ?? "The codec did not negotiate a compatible hardware format.");
            return selected == Renderer.HardwarePixelFormat ? AVPixelFormat.None : selected;
        }
        catch (Exception error)
        {
            RequestSoftwareFallback($"Hardware format negotiation failed: {error.Message}");
            return AVPixelFormat.None;
        }
    }

    static bool IsSoftwarePixelFormat(AVPixelFormat format)
    {
        var descriptor = av_pix_fmt_desc_get(format);
        return descriptor != null && (descriptor->flags & PixFmtFlags.Hwaccel) == 0;
    }

    internal void RequestSoftwareFallback(string reason)
    {
        Renderer.MarkHardwareFallback(reason);
        swFallback = true;
    }

    bool SetupPortableSoftwareFallback(string reason)
    {
        Renderer.MarkHardwareFallback(reason);
        fixed (AVCodecContext** context = &codecCtx) avcodec_free_context(context);
        swFallback = true;
        try { return Setup(); }
        finally { swFallback = false; }
    }

    int CompletePortableSoftwareFallback()
    {
        av_frame_unref(frame);
        if (PortableRecoveryInterrupted())
        {
            ResetPortableRecovery();
            return -1234;
        }
        portableReplayDiscardFrames = portableRecovery?.DecodedFrames ?? 0;
        if (!SWFallback())
        {
            ResetPortableRecovery();
            return -1234;
        }
        bool completed = false;
        try
        {
            if (portableRecovery != null)
                foreach (nint pointer in portableRecovery.Packets)
                {
                    if (PortableRecoveryInterrupted()) return -1234;
                    AVPacket* packet = (AVPacket*)pointer;
                    int result = avcodec_send_packet(codecCtx, packet);
                    if (result == AVERROR_EAGAIN)
                    {
                        if (!DrainPortableReplay()) return -1234;
                        if (PortableRecoveryInterrupted()) return -1234;
                        result = avcodec_send_packet(codecCtx, packet);
                    }
                    if (result < 0 || PortableRecoveryInterrupted() || !DrainPortableReplay())
                        return -1234;
                }
            completed = true;
            return 0;
        }
        finally
        {
            portableRecovery?.Dispose();
            portableRecovery = null;
            if (!completed)
            {
                av_frame_unref(frame);
                ResetPortableRecovery();
            }
        }
    }

    // Pause finishes this finite admitted pass so its fresh codec remains resumable. Stop/new media
    // interrupts between native calls; a paused frame-step is valid even though it has no running thread.
    bool PortableRecoveryInterrupted()
        => Status == Status.Stopping || demuxer.Interrupter.ForceInterrupt != 0;

    bool DrainPortableReplay()
    {
        while (true)
        {
            if (PortableRecoveryInterrupted()) return false;
            int result = avcodec_receive_frame(codecCtx, frame);
            if (result != 0)
                return result == AVERROR_EAGAIN || result == AVERROR_EOF;
            if (PortableRecoveryInterrupted())
            {
                av_frame_unref(frame);
                return false;
            }
            if (portableReplayDiscardFrames > 0)
            {
                portableReplayDiscardFrames--;
                av_frame_unref(frame);
                continue;
            }
            // Enqueue cannot allocate after the native frame has taken ownership of its references.
            try
            {
                portableRecoveredFrames ??= [];
                portableRecoveredFrames.EnsureCapacity(portableRecoveredFrames.Count + 1);
            }
            catch (OutOfMemoryException)
            {
                av_frame_unref(frame);
                return false;
            }
            AVFrame* recovered = av_frame_alloc();
            if (recovered == null)
            {
                av_frame_unref(frame);
                return false;
            }
            av_frame_move_ref(recovered, frame);
            portableRecoveredFrames.Enqueue((nint)recovered);
        }
    }

    int SendPortableAVPacket(AVPacket* packet)
    {
        bool retained = false;
        if (VideoAccelerated)
        {
            portableRecovery ??= new();
            retained = portableRecovery.TryRetain(packet, out string? reason);
            if (!retained)
                RequestSoftwareFallback(reason!);
        }

        if (swFallback)
        {
            if (CompletePortableSoftwareFallback() != 0)
                return -1234;
            // A retained packet (including one rejected by hardware) was already submitted during replay.
            return retained ? 0 : avcodec_send_packet(codecCtx, packet);
        }

        int result = avcodec_send_packet(codecCtx, packet);
        if (VideoAccelerated && result < 0 && result != AVERROR_EAGAIN && result != AVERROR_EOF)
            RequestSoftwareFallback($"Hardware packet decoding failed ({result}).");
        if (swFallback)
            return CompletePortableSoftwareFallback();
        return result;
    }

    int ReceivePortableAVFrame()
    {
        while (true)
        {
            if (swFallback && CompletePortableSoftwareFallback() != 0)
                return -1234;
            if (portableRecoveredFrames != null && portableRecoveredFrames.TryDequeue(out nint pointer))
            {
                AVFrame* recovered = (AVFrame*)pointer;
                av_frame_move_ref(frame, recovered);
                av_frame_free(&recovered);
                return 0;
            }

            int result = avcodec_receive_frame(codecCtx, frame);
            if (VideoAccelerated && result < 0 && result != AVERROR_EAGAIN && result != AVERROR_EOF)
                RequestSoftwareFallback($"Hardware frame decoding failed ({result}).");
            if (swFallback || result == 0 && !Renderer.TransferHardwareFrame(frame))
                continue; // Reopen once outside FFmpeg callbacks, then receive reconstructed output normally.
            if (result != 0)
                return result;
            if (portableReplayDiscardFrames > 0)
            {
                portableReplayDiscardFrames--;
                av_frame_unref(frame);
                continue;
            }
            if (VideoAccelerated)
                portableRecovery?.FrameReceived(frame);
            return 0;
        }
    }

    void ResetPortableRecovery()
    {
        portableRecovery?.Dispose();
        portableRecovery = null;
        if (portableRecoveredFrames != null)
            while (portableRecoveredFrames.TryDequeue(out nint pointer))
            {
                AVFrame* recovered = (AVFrame*)pointer;
                av_frame_free(&recovered);
            }
        portableReplayDiscardFrames = 0;
    }
}
