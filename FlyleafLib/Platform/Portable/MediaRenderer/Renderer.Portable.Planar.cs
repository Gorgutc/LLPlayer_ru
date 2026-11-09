using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    // A reference to the PREPARED CPU picture, not the reusable preprocessor output object.
    // Protected by lockRenderLoops, like the existing BGRA pending buffer.
    AVFrame* pendingPlanarFrame;
    bool pendingIsPlanar;
    PlanarVideoPixelFormat pendingPlanarFormat;
    VideoPresentationParameters pendingPresentation;
    int pendingPlanarOutputWidth, pendingPlanarOutputHeight;
    SoftwareColorFilter pendingPlanarColorFilter;

    /// <summary>Schedules a repaint through the existing idle/playback request owner, without reattaching a surface.</summary>
    public void RequestSurfaceRefresh() => RenderRequest();

    internal bool HasPendingPlanarFrame => pendingIsPlanar && pendingPlanarFrame != null;
    internal long SoftwareConversionCount { get; private set; }

    bool PreparePlanar(AVFrame* source, CropRect sourceCrop, VideoTransform orientation, int outputWidth, int outputHeight,
        ColorSpace sourceColorSpace, ColorRange sourceColorRange)
    {
        if (colorFilter != null || scfg != null && scfg.HDRFormat != HDRFormat.None ||
            surface is not IVideoPlanarSurface planar)
            return false;

        PlanarVideoPixelFormat format;
        switch ((AVPixelFormat)source->format)
        {
            case AVPixelFormat.Yuv420p: format = PlanarVideoPixelFormat.Yuv420P; break;
            case AVPixelFormat.Yuvj420p:
                format = PlanarVideoPixelFormat.Yuv420P;
                sourceColorRange = ColorRange.Full;
                break;
            case AVPixelFormat.Nv12: format = PlanarVideoPixelFormat.Nv12; break;
            default: return false;
        }
        if (!planar.CanPresentPlanar(format))
            return false;
        if (pendingPlanarFrame == null)
            pendingPlanarFrame = av_frame_alloc();
        if (pendingPlanarFrame == null || av_frame_ref(pendingPlanarFrame, source) < 0)
            return false;
        pendingPlanarFormat = format;
        pendingPresentation = new(sourceCrop, orientation.Transpose, orientation.FlipX, orientation.FlipY, sourceColorSpace, sourceColorRange);
        pendingPlanarOutputWidth = outputWidth;
        pendingPlanarOutputHeight = outputHeight;
        pendingPlanarColorFilter = colorFilter;
        pendingIsPlanar = true;
        hasPending = true;
        return true;
    }

    bool PresentPendingPlanar(IVideoSurface target)
    {
        var source = pendingPlanarFrame;
        if (source == null)
            return false;
        if (target is IVideoPlanarSurface planar && planar.CanPresentPlanar(pendingPlanarFormat))
        {
            int cw = (source->width + 1) / 2, ch = (source->height + 1) / 2;
            if (TryBorrowPlane(source, 0, source->width, source->height, source->width, out var y) &&
                TryBorrowPlane(source, 1, cw, ch, pendingPlanarFormat == PlanarVideoPixelFormat.Nv12 ? cw * 2 : cw, out var u))
            {
                VideoPlaneData v = default;
                if (pendingPlanarFormat == PlanarVideoPixelFormat.Nv12 || TryBorrowPlane(source, 2, cw, ch, cw, out v))
                {
                    var frame = new PlanarVideoFrame(pendingPlanarFormat, source->width, source->height, y, u, v);
                    try
                    {
                        if (frame.IsValid && planar.TryPresentPlanar(frame, pendingPresentation))
                        {
                            PresentedWidth = pendingPlanarOutputWidth;
                            PresentedHeight = pendingPlanarOutputHeight;
                            return true;
                        }
                    }
                    catch (Exception error) { LogConvertError($"Planar surface rejected the picture: {error.Message}"); }
                }
            }
        }

        // Context loss or live mode change between prepare and timed present must use THIS SAME owned picture.
        var t = new VideoTransform(pendingPresentation.Transpose, pendingPresentation.FlipX, pendingPresentation.FlipY);
        SoftwareConversionCount++;
        if (!converter.Convert(source, pendingPresentation.Crop, t, pendingPlanarOutputWidth, pendingPlanarOutputHeight,
            pendingPresentation.ColorSpace, pendingPresentation.ColorRange, pendingPlanarColorFilter))
        {
            LogConvertError(converter.LastError);
            return false;
        }
        target.PresentFrame(new ReadOnlySpan<byte>(converter.Output, checked((converter.OutputHeight - 1) * converter.OutputStride + converter.OutputWidth * 4)),
            converter.OutputWidth, converter.OutputHeight, converter.OutputStride);
        PresentedWidth = converter.OutputWidth;
        PresentedHeight = converter.OutputHeight;
        return true;
    }

    static bool TryBorrowPlane(AVFrame* frame, int plane, int width, int height, int rowBytes, out VideoPlaneData result)
    {
        result = default;
        long stride = frame->linesize[plane];
        long positiveStride = Math.Abs(stride);
        long bytes = (height - 1L) * positiveStride + rowBytes;
        if (width <= 0 || height <= 0 || frame->data[plane] == 0 || positiveStride < rowBytes ||
            positiveStride > int.MaxValue || bytes <= 0 || bytes > int.MaxValue)
            return false;
        byte* lowestRow = (byte*)frame->data[plane] + (stride < 0 ? (height - 1L) * stride : 0);
        result = new(new ReadOnlySpan<byte>(lowestRow, (int)bytes), width, height, rowBytes, (int)positiveStride, stride < 0);
        return true;
    }

    void ReleasePendingPlanar()
    {
        pendingIsPlanar = false;
        pendingPlanarColorFilter = null;
        if (pendingPlanarFrame != null)
            av_frame_unref(pendingPlanarFrame);
    }

    void DisposePendingPlanar()
    {
        ReleasePendingPlanar();
        if (pendingPlanarFrame != null)
            fixed (AVFrame** address = &pendingPlanarFrame) av_frame_free(address);
    }
}
