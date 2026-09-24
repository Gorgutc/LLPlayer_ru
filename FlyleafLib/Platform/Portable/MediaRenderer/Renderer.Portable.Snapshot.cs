using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaRenderer;

// F-13 portable snapshots: renders the renderer's current frame through the same software pipeline as the presented
// frames (deinterlace, HDR to SDR, crop, rotation / mirroring, colour filters — like the Windows snapshot, which renders
// through the Flyleaf pixel shader) but at native resolution, and encodes it with FFmpeg's image encoders
// (png / bmp / mjpeg) instead of GDI+ (Windows Renderer.Snapshot.cs).
public unsafe partial class Renderer
{
    /// <summary>
    /// Encodes the current (or the given) frame to <paramref name="filename"/>; the format follows the extension
    /// (.png, .bmp, .jpg/.jpeg). Width/height 0 keep the original (visible, upright) size, one of them 0 keeps the
    /// ratio. Returns false when there is no frame to save.
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

            var stream  = scfg;
            var tb      = stream != null ? stream.AVStream->time_base : new AVRational { Num = 1, Den = 90000 };

            if (FieldType != VideoFrameFormat.Progressive && !VideoDecoder.Demuxer.IsReversePlayback)
            {   // Same picture as presented (cached for the current frame)
                var d = preprocessor.Deinterlace(frame, false, Frames, FieldType == VideoFrameFormat.InterlacedTopFieldFirst, ucfg.DoubleRate, tb);
                if (d != null)
                    src = d;
            }

            var cropped = SoftwareVideoGeometry.ClampCrop(crop, src->width, src->height);
            var t       = transform;
            var (uprightW, uprightH) = t.OutputSize(src->width - (int)cropped.Width, src->height - (int)cropped.Height);
            var (outW, outH) = SoftwareVideoGeometry.SnapshotSize(uprightW, uprightH, width, height);

            AVCodec*        codec   = avcodec_find_encoder(codecId);
            AVCodecContext* ctx     = null;
            AVFrame*        hdrFrame= null;
            AVFrame*        dst     = null;
            AVPacket*       pkt     = null;
            SwsContext*     sws     = null;
            SoftwareFrameConverter conv = new();

            try
            {
                if (codec == null)
                    throw new($"Snapshot encoder {codecId} not found");

                if (stream != null && stream.HDRFormat != HDRFormat.None && SoftwareFramePreprocessor.CanToneMap)
                {
                    var (preW, preH) = t.OutputSize(outW, outH);
                    string filters = SoftwareFramePreprocessor.ToneMapFilters(cropped, src->width, src->height, preW, preH, stream.HDRFormat, stream.ColorRange, ucfg.HDRtoSDRMethod, ucfg.SDRDisplayNits);
                    hdrFrame = SoftwareFramePreprocessor.ToneMapOnce(src, filters, tb, out string error);
                    if (hdrFrame != null)
                    {
                        src     = hdrFrame;
                        cropped = CropRect.Empty;
                    }
                    else
                        Log.Warn($"[Snapshot] HDR to SDR failed ({error})");
                }

                if (!conv.Convert(src, cropped, t, outW, outH,
                    stream != null ? stream.ColorSpace : ColorSpace.None, stream != null ? stream.ColorRange : ColorRange.Limited,
                    colorFilter, highQuality: true))
                    throw new($"Snapshot conversion failed ({conv.LastError})");

                dst = av_frame_alloc();
                dst->format = (int)pixFmt;
                dst->width  = outW;
                dst->height = outH;
                if (av_frame_get_buffer(dst, 0) < 0)
                    throw new("Snapshot frame allocation failed");

                sws = sws_getContext(outW, outH, AVPixelFormat.Bgra, outW, outH, pixFmt, SwsFlags.Bicubic, null, null, null);
                if (sws == null)
                    throw new("Snapshot SwsContext allocation failed");

                _ = sws_scale(sws, [conv.Output, null, null, null], [conv.OutputStride, 0, 0, 0], 0, outH, dst->data.ToRawArray(), dst->linesize.ToArray());

                ctx = avcodec_alloc_context3(codec);
                ctx->width      = outW;
                ctx->height     = outH;
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
                conv.Dispose();
                if (pkt     != null) av_packet_free(&pkt);
                if (ctx     != null) avcodec_free_context(&ctx);
                if (sws     != null) sws_freeContext(sws);
                if (dst     != null) av_frame_free(&dst);
                if (hdrFrame!= null) av_frame_free(&hdrFrame);
            }
        }
    }
}
