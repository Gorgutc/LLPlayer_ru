using Flyleaf.FFmpeg;
using FlyleafLib.MediaFramework.MediaRenderer;
using static Flyleaf.FFmpeg.Raw;

namespace FlyleafLib.Platform;

// Real FFmpeg buffer/property ownership with an injected download. These tests do not create a GPU device.
[Collection("PortableEngine")]
public unsafe class PortableHardwareFrameTransferTests
{
    static AVFrame* Source()
    {
        PortableVideoTestMedia.RequireEngine();
        AVFrame* frame = av_frame_alloc();
        frame->format = (int)AVPixelFormat.Yuv420p;
        frame->width = 13;
        frame->height = 9;
        Assert.Equal(0, av_frame_get_buffer(frame, 32));
        frame->format = (int)AVPixelFormat.D3d11;
        frame->pts = 12345;
        frame->best_effort_timestamp = 12200;
        frame->sample_aspect_ratio = new(4, 3);
        frame->color_range = AVColorRange.Jpeg;
        frame->colorspace = AVColorSpace.Bt709;
        frame->crop_left = 1;
        return frame;
    }

    [Fact]
    public void Injected_download_preserves_timestamps_color_crop_and_one_owned_cpu_buffer()
    {
        AVFrame* source = Source();
        try
        {
            var buffer = ((AVBufferRef*)source->buf[0])->data;
            bool transferred = PortableHardwareFrameTransfer.Download(source, (destination, input) =>
            {
                int result = av_frame_ref(destination, input);
                destination->format = (int)AVPixelFormat.Yuv420p;
                destination->pts = -1;
                destination->color_range = AVColorRange.Unspecified;
                destination->crop_left = 0;
                return result;
            }, out int error);
            Assert.True(transferred);
            Assert.Equal(0, error);
            Assert.Equal(AVPixelFormat.Yuv420p, (AVPixelFormat)source->format);
            Assert.Equal(12345, source->pts);
            Assert.Equal(12200, source->best_effort_timestamp);
            Assert.Equal(AVColorRange.Jpeg, source->color_range);
            Assert.Equal(AVColorSpace.Bt709, source->colorspace);
            Assert.Equal((nuint)1, source->crop_left);
            Assert.Equal(4, source->sample_aspect_ratio.Num);
            Assert.True(buffer == ((AVBufferRef*)source->buf[0])->data);
            Assert.Equal(1, av_buffer_get_ref_count((AVBufferRef*)source->buf[0]));
        }
        finally { av_frame_free(&source); }
    }

    [Fact]
    public void Failed_download_releases_temporary_reference_and_leaves_source_owned_for_fallback()
    {
        AVFrame* source = Source();
        try
        {
            bool transferred = PortableHardwareFrameTransfer.Download(source, (destination, input) =>
            {
                _ = av_frame_ref(destination, input);
                return -5;
            }, out int error);
            Assert.False(transferred);
            Assert.Equal(-5, error);
            Assert.Equal(AVPixelFormat.D3d11, (AVPixelFormat)source->format);
            Assert.Equal(12345, source->pts);
            Assert.Equal(1, av_buffer_get_ref_count((AVBufferRef*)source->buf[0]));
        }
        finally { av_frame_free(&source); }
    }
}
