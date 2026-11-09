#nullable enable
namespace FlyleafLib.MediaFramework.MediaRenderer;

internal unsafe delegate int PortableHardwareFrameDownload(AVFrame* destination, AVFrame* source);

internal static unsafe class PortableHardwareFrameTransfer
{
    /// <summary>Replaces the source reference only after both pixels and all frame properties were copied.</summary>
    internal static bool Download(AVFrame* source, PortableHardwareFrameDownload download, out int error)
    {
        AVFrame* software = av_frame_alloc();
        if (software == null)
        {
            error = AVERROR_ENOMEM;
            return false;
        }
        try
        {
            error = download(software, source);
            if (error < 0)
                return false;
            var descriptor = av_pix_fmt_desc_get((AVPixelFormat)software->format);
            if (descriptor == null || (descriptor->flags & PixFmtFlags.Hwaccel) != 0)
            {
                error = AVERROR_EINVAL;
                return false;
            }
            error = av_frame_copy_props(software, source);
            if (error < 0)
                return false;
            av_frame_unref(source);
            av_frame_move_ref(source, software);
            return true;
        }
        finally { av_frame_free(&software); }
    }
}
