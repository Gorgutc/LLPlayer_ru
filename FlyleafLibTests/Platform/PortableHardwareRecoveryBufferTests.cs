using Flyleaf.FFmpeg;
using FlyleafLib.MediaFramework.MediaDecoder;
using static Flyleaf.FFmpeg.Raw;

namespace FlyleafLib.Platform;

[Collection("PortableEngine")]
public unsafe class PortableHardwareRecoveryBufferTests
{
    [Fact]
    public void Side_data_is_included_in_cap_admission_and_released_without_changing_source()
    {
        PortableVideoTestMedia.RequireEngine();
        AVPacket* packet = av_packet_alloc();
        try
        {
            Assert.Equal(0, av_new_packet(packet, 8));
            byte* sideData = av_packet_new_side_data(packet, AVPacketSideDataType.NewExtradata, 16);
            Assert.True(sideData != null);
            sideData[0] = 234;
            using (PortableHardwareRecoveryBuffer tooSmall = new(maxBytes: 16))
            {
                Assert.False(tooSmall.TryRetain(packet, out string? reason));
                Assert.Contains("limit", reason!);
                Assert.Empty(tooSmall.Packets);
                Assert.Equal(0, tooSmall.Bytes);
                Assert.Equal(1, av_buffer_get_ref_count(packet->buf));
            }
            using (PortableHardwareRecoveryBuffer fits = new(maxBytes: 24))
            {
                Assert.True(fits.TryRetain(packet, out _));
                Assert.Equal(24, fits.Bytes);
                AVPacket* retained = (AVPacket*)Assert.Single(fits.Packets);
                Assert.True(retained->side_data[0].data != sideData);
                Assert.Equal(234, retained->side_data[0].data[0]);
                Assert.Equal(2, av_buffer_get_ref_count(packet->buf));
            }
            Assert.Equal(1, av_buffer_get_ref_count(packet->buf));
            Assert.Equal(234, sideData[0]);
        }
        finally { av_packet_free(&packet); }
    }

    [Fact]
    public void Repeated_key_picture_from_existing_anchor_does_not_reset_successful_receive_count()
    {
        PortableVideoTestMedia.RequireEngine();
        AVPacket* packet = av_packet_alloc();
        AVFrame* frame = av_frame_alloc();
        try
        {
            Assert.Equal(0, av_new_packet(packet, 8));
            packet->pts = 0;
            packet->flags = PktFlags.Key;
            using PortableHardwareRecoveryBuffer history = new();
            Assert.True(history.TryRetain(packet, out _));
            frame->pts = frame->best_effort_timestamp = 0;
            frame->flags = FrameFlags.Key;
            history.FrameReceived(frame);
            frame->flags = 0;
            frame->pts = frame->best_effort_timestamp = 50;
            history.FrameReceived(frame);
            frame->pts = frame->best_effort_timestamp = 0;
            frame->flags = FrameFlags.Key;
            history.FrameReceived(frame);
            Assert.Equal(3, history.DecodedFrames);
            Assert.Single(history.Packets);
            Assert.Equal(2, av_buffer_get_ref_count(packet->buf));
        }
        finally
        {
            av_frame_free(&frame);
            av_packet_free(&packet);
        }
    }

    [Theory]
    [InlineData(2, 64)]
    [InlineData(8, 16)]
    public void Packet_and_byte_caps_reject_before_cloning_and_dispose_releases_only_owned_refs(int packets, int bytes)
    {
        PortableVideoTestMedia.RequireEngine();
        AVPacket* packet = av_packet_alloc();
        try
        {
            Assert.Equal(0, av_new_packet(packet, 8));
            packet->data[0] = 123;
            using (PortableHardwareRecoveryBuffer history = new(packets, bytes))
            {
                Assert.True(history.TryRetain(packet, out _));
                Assert.True(history.TryRetain(packet, out _));
                Assert.Equal(3, av_buffer_get_ref_count(packet->buf));
                Assert.False(history.TryRetain(packet, out string? reason));
                Assert.Contains("limit", reason!);
                Assert.Equal(2, history.Packets.Count);
                Assert.Equal(16, history.Bytes);
                Assert.Equal(3, av_buffer_get_ref_count(packet->buf));
            }
            Assert.Equal(1, av_buffer_get_ref_count(packet->buf));
            Assert.Equal(123, packet->data[0]);
        }
        finally { av_packet_free(&packet); }
    }

    [Fact]
    public void Previous_gop_refs_survive_until_matching_key_picture_is_actually_received()
    {
        PortableVideoTestMedia.RequireEngine();
        AVPacket* previous = av_packet_alloc();
        AVPacket* next = av_packet_alloc();
        AVFrame* frame = av_frame_alloc();
        try
        {
            Assert.Equal(0, av_new_packet(previous, 8));
            Assert.Equal(0, av_new_packet(next, 8));
            previous->pts = 0;
            next->pts = 100;
            previous->flags = next->flags = PktFlags.Key;
            using PortableHardwareRecoveryBuffer history = new();
            Assert.True(history.TryRetain(previous, out _));
            frame->pts = frame->best_effort_timestamp = 0;
            frame->flags = FrameFlags.Key;
            history.FrameReceived(frame);
            Assert.True(history.TryRetain(next, out _));
            Assert.Equal(2, av_buffer_get_ref_count(previous->buf));
            Assert.Equal(2, history.Packets.Count);

            frame->pts = frame->best_effort_timestamp = 50;
            frame->flags = 0;
            history.FrameReceived(frame);
            Assert.Equal(2, history.DecodedFrames);
            Assert.Equal(2, av_buffer_get_ref_count(previous->buf));

            frame->pts = frame->best_effort_timestamp = 100;
            frame->flags = FrameFlags.Key;
            history.FrameReceived(frame);
            Assert.Equal(1, history.DecodedFrames);
            Assert.Single(history.Packets);
            Assert.Equal(8, history.Bytes);
            Assert.Equal(1, av_buffer_get_ref_count(previous->buf));
            Assert.Equal(2, av_buffer_get_ref_count(next->buf));
        }
        finally
        {
            av_frame_free(&frame);
            av_packet_free(&previous);
            av_packet_free(&next);
        }
    }
}
