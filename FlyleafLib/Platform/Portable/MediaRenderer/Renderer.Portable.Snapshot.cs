using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaRenderer;

// F-13 portable snapshots: encodes the renderer's current frame with FFmpeg's image encoders (png / bmp / mjpeg)
// instead of GDI+ (Windows Renderer.Snapshot.cs).
public unsafe partial class Renderer
{
    /// <summary>
    /// Encodes the current (or the given) frame to <paramref name="filename"/>; the format follows the extension
    /// (.png, .bmp, .jpg/.jpeg). Width/height 0 keep the original size (one of them keeps the ratio).
    /// Returns false when there is no frame to save.
    /// </summary>
    public bool TakeSnapshotToFile(string filename, uint width = 0, uint height = 0, VideoFrame frame = null)
    {
        string ext = GetUrlExtention(filename);

        (AVCodecID codecId, AVPixelFormat pixFmt) = ext switch
        {
            "png"           => (AVCodecID.Png,  AVPixelFormat.Rgb24),
            "bmp"           => (AVCodecID.Bmp,  AVPixelFormat.Bgr24),
            "jpg" or "jpeg" => (AVCodecID.Mjpeg,AVPixelFormat.Yuvj420p),
            _ => throw new($"Invalid snapshot extention '{ext}' (valid .bmp, .png, .jpeg, .jpg"),
        };

        lock (lockRenderLoops)
        {
            frame ??= Frames.RendererFrame;
            AVFrame* src = frame == null ? null : frame.AVFrame;
            if (src == null || src->width <= 0 || src->height <= 0)
                return false;

            // Crop (stream/codec + user), same as the presented frame
            uint left   = Math.Min(crop.Left,   (uint)src->width  - 1);
            uint top    = Math.Min(crop.Top,    (uint)src->height - 1);
            uint right  = Math.Min(crop.Right,  (uint)src->width  - 1 - left);
            uint bottom = Math.Min(crop.Bottom, (uint)src->height - 1 - top);
            int srcW    = src->width  - (int)(left + right);
            int srcH    = src->height - (int)(top + bottom);

            if (width == 0 && height == 0)
                { width = (uint)srcW; height = (uint)srcH; }
            else if (width == 0)
                width  = (uint)(srcW * (height / (double)srcH));
            else if (height == 0)
                height = (uint)(srcH * (width  / (double)srcW));

            width   = Math.Max(2, width  & ~1u);
            height  = Math.Max(2, height & ~1u);

            AVCodec*        codec   = avcodec_find_encoder(codecId);
            AVCodecContext* ctx     = null;
            AVFrame*        cropped = null;
            AVFrame*        dst     = null;
            AVPacket*       pkt     = null;
            SwsContext*     sws     = null;

            try
            {
                if (codec == null)
                    throw new($"Snapshot encoder {codecId} not found");

                cropped = av_frame_clone(src);
                cropped->crop_left  = left;
                cropped->crop_top   = top;
                cropped->crop_right = right;
                cropped->crop_bottom= bottom;
                if (av_frame_apply_cropping(cropped, 1 /* AV_FRAME_CROP_UNALIGNED */) < 0)
                    throw new("Snapshot cropping failed");

                dst = av_frame_alloc();
                dst->format = (int)pixFmt;
                dst->width  = (int)width;
                dst->height = (int)height;
                if (av_frame_get_buffer(dst, 0) < 0)
                    throw new("Snapshot frame allocation failed");

                sws = sws_getContext(cropped->width, cropped->height, (AVPixelFormat)cropped->format, (int)width, (int)height, pixFmt, SwsFlags.Bicubic, null, null, null);
                if (sws == null)
                    throw new("Snapshot SwsContext allocation failed");

                _ = sws_scale(sws, cropped->data.ToRawArray(), cropped->linesize.ToArray(), 0, cropped->height, dst->data.ToRawArray(), dst->linesize.ToArray());

                ctx = avcodec_alloc_context3(codec);
                ctx->width      = (int)width;
                ctx->height     = (int)height;
                ctx->pix_fmt    = pixFmt;
                ctx->time_base  = new() { Num = 1, Den = 25 };
                if (avcodec_open2(ctx, codec, null) < 0)
                    throw new($"Snapshot encoder {codecId} failed to open");

                pkt = av_packet_alloc();
                if (avcodec_send_frame(ctx, dst) < 0 || avcodec_send_frame(ctx, null) < 0 || avcodec_receive_packet(ctx, pkt) < 0)
                    throw new("Snapshot encoding failed");

                using FileStream fs = new(filename, FileMode.Create, FileAccess.Write);
                fs.Write(new ReadOnlySpan<byte>(pkt->data, pkt->size));

                return true;
            }
            finally
            {
                if (pkt     != null) av_packet_free(&pkt);
                if (ctx     != null) avcodec_free_context(&ctx);
                if (sws     != null) sws_freeContext(sws);
                if (dst     != null) av_frame_free(&dst);
                if (cropped != null) av_frame_free(&cropped);
            }
        }
    }
}
