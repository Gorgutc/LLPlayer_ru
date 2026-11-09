using Flyleaf.FFmpeg;
using System.Reflection;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using static Flyleaf.FFmpeg.Raw;

namespace FlyleafLib.Platform;

// Real software H.264 decoding exercises a driver-failure request without requiring/probing a GPU.
// The final GOP has no replacement key frame: waiting for one or resending only the current P packet loses it.
[Collection("PortableEngine")]
public unsafe class PortableHardwareFallbackRecoveryTests
{
    sealed class TestingState : IDisposable
    {
        public void Dispose() => Utils.IsTesting = true;
    }

    static Player OpenSingleGop(bool accelerated = true, bool withBFrames = false)
    {
        PortableVideoTestMedia.RequireEngine();
        Utils.IsTesting = false;
        Config config = new();
        config.Player.AutoPlay = false;
        config.Decoder.VideoThreads = 1;
        Player player = new(config);
        var opened = player.Open(withBFrames ? PortableVideoTestMedia.SingleGopBFrames : PortableVideoTestMedia.SingleGop);
        if (!opened.Success)
        {
            player.Dispose();
            Assert.Fail(opened.Error);
        }
        // Keep the real software codec, but enter the accelerated decoder's existing packet/receive branches.
        // No device/context is fabricated or probed; RequestSoftwareFallback supplies the failure itself.
        player.VideoDecoder.VideoAccelerated = accelerated;
        player.Renderer.ffTextureDesc.Width = (uint)player.VideoDecoder.Width;
        player.Renderer.ffTextureDesc.Height = (uint)player.VideoDecoder.Height;
        return player;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Frame_step_fallback_in_final_gop_preserves_every_remaining_timestamp(bool withBFrames)
    {
        using TestingState state = new();
        using Player player = OpenSingleGop(withBFrames: withBFrames);
        var initialStatus = player.Status;
        VideoDecoder decoder = player.VideoDecoder;
        List<long> timestamps = [];
        VideoFrame? queued = null;
        long startTime = 0;
        var frame = decoder.GetFrame(0);
        while (frame != null)
        {
            timestamps.Add(frame.Timestamp);
            if (timestamps.Count == 8)
            {
                queued = new() { Timestamp = frame.Timestamp, AVFrame = av_frame_clone(frame.AVFrame) };
                Assert.True(queued.AVFrame != null);
                player.Renderer.Frames.Enqueue(queued);
                startTime = decoder.StartTime;
                decoder.RequestSoftwareFallback("Injected frame-download failure");
            }
            frame.Dispose();
            frame = decoder.GetFrameNext();
        }

        Assert.Equal(ExpectedTimestamps(), timestamps);
        Assert.Equal(initialStatus, player.Status);
        Assert.Equal(PortableVideoDecodeMode.Software, player.Renderer.DecodeStatus.EffectiveMode);
        Assert.Contains("Injected", player.Renderer.DecodeStatus.FallbackReason!);
        Assert.Equal(startTime, decoder.StartTime);
        Assert.Equal(1, player.Renderer.Frames.Count);
        Assert.True(queued!.AVFrame != null && queued.AVFrame->buf[0] != 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Packet_loop_fallback_in_final_gop_preserves_every_remaining_timestamp(bool requestBeforeSend, bool withBFrames)
    {
        using TestingState state = new();
        using Player player = OpenSingleGop(withBFrames: withBFrames);
        VideoDecoder decoder = player.VideoDecoder;
        var demuxer = player.VideoDemuxer;
        List<long> timestamps = [];
        bool injected = false;
        while (demuxer.GetNextVideoPacket() == 0)
        {
            // Sending takes ownership of a separately allocated packet, unlike Demuxer's reusable packet.
            AVPacket* packet = av_packet_clone(demuxer.packet);
            Assert.True(packet != null);
            av_packet_unref(demuxer.packet);
            bool injectNow = !injected && timestamps.Count >= 8;
            if (injectNow && requestBeforeSend)
                decoder.RequestSoftwareFallback("Injected packet-send failure");
            int sent = decoder.SendAVPacket(packet);
            Assert.True(sent == 0 || sent == AVERROR_EAGAIN, $"Unexpected decoder send result {sent}");
            if (injectNow && !requestBeforeSend)
                decoder.RequestSoftwareFallback("Injected frame-download failure");
            injected |= injectNow;
            if (sent == 0)
                DrainFrames(decoder, timestamps);
        }
        AVPacket* end = av_packet_alloc();
        decoder.keyPacketRequired = false; // The ordinary DecoderContext drain also bypasses key admission.
        Assert.Equal(0, decoder.SendAVPacket(end));
        DrainFrames(decoder, timestamps);

        Assert.True(injected);
        Assert.Equal(ExpectedTimestamps(), timestamps);
    }

    static void DrainFrames(VideoDecoder decoder, List<long> timestamps)
    {
        int result;
        while ((result = decoder.RecvAVFrame()) == 0)
        {
            timestamps.Add((long)(decoder.frame->pts * decoder.VideoStream.Timebase) - decoder.Demuxer.StartTime);
            av_frame_unref(decoder.frame);
        }
        Assert.True(result == AVERROR_EAGAIN || result == AVERROR_EOF, $"Unexpected decoder receive result {result}");
    }

    static long[] ExpectedTimestamps()
        => Enumerable.Range(0, 50).Select(index => index * TimeSpan.TicksPerSecond / 25).ToArray();

    [Fact]
    public void Bounded_history_switches_once_before_losing_references_and_preserves_the_final_gop()
    {
        using TestingState state = new();
        using Player player = OpenSingleGop();
        VideoDecoder decoder = player.VideoDecoder;
        using PortableHardwareRecoveryBuffer history = new(maxPackets: 8);
        var historyField = typeof(VideoDecoder).GetField("portableRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!;
        historyField.SetValue(decoder, history);
        int softwareOpens = 0;
        decoder.OpeningCodec = () => softwareOpens++;
        List<long> timestamps = [];
        var frame = decoder.GetFrameNext();
        while (frame != null)
        {
            timestamps.Add(frame.Timestamp);
            frame.Dispose();
            frame = decoder.GetFrameNext();
        }
        Assert.Equal(ExpectedTimestamps(), timestamps);
        Assert.Equal(1, softwareOpens);
        Assert.False(decoder.VideoAccelerated);
        Assert.Contains("limit", player.Renderer.DecodeStatus.FallbackReason!);
        Assert.Empty(history.Packets);
        Assert.Equal(0, history.Bytes);
    }

    [Fact]
    public void Default_software_decoding_does_not_allocate_hardware_recovery_history()
    {
        using TestingState state = new();
        using Player player = OpenSingleGop(accelerated: false);
        int frames = 0;
        while (player.VideoDecoder.GetFrameNext() is { } frame)
        {
            frames++;
            frame.Dispose();
        }
        Assert.Equal(50, frames);
        Assert.Null(typeof(VideoDecoder).GetField("portableRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(player.VideoDecoder));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stop_or_interrupt_during_reopen_aborts_replay_and_releases_retained_native_refs(bool forceInterrupt)
    {
        using TestingState state = new();
        using Player player = OpenSingleGop();
        VideoDecoder decoder = player.VideoDecoder;
        for (int index = 0; index < 8; index++)
        {
            var frame = decoder.GetFrameNext();
            Assert.NotNull(frame);
            frame.Dispose();
        }
        var history = (PortableHardwareRecoveryBuffer)typeof(VideoDecoder)
            .GetField("portableRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(decoder)!;
        AVPacket* retained = (AVPacket*)history.Packets[0];
        AVBufferRef* held = av_buffer_ref(retained->buf);
        Assert.True(held != null);
        try
        {
            decoder.OpeningCodec = () =>
            {
                if (forceInterrupt)
                    decoder.Demuxer.Interrupter.ForceInterrupt = 1;
                else
                    decoder.Status = FlyleafLib.MediaFramework.Status.Stopping;
            };
            decoder.RequestSoftwareFallback("Injected failure before stop");
            Assert.Equal(-1234, decoder.RecvAVFrame());
            Assert.Empty(history.Packets);
            Assert.Equal(0, history.Bytes);
            Assert.Equal(1, av_buffer_get_ref_count(held));
        }
        finally
        {
            decoder.Demuxer.Interrupter.ForceInterrupt = 0;
            decoder.Status = FlyleafLib.MediaFramework.Status.Stopped;
            av_buffer_unref(&held);
        }
    }
}
