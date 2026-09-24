using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaRenderer;

// F-13 portable software presentation pipeline (render / playback thread, under lockRenderLoops):
//   decoded AVFrame -> [deinterlace: bwdif] -> [HDR to SDR: crop + downscale + zscale/tonemap]
//                   -> crop + swscale to BGRA at min(native, viewport) size -> rotation / mirror pass -> colour filters
//                   -> IVideoSurface.PresentFrame (upright, ready to show)
public unsafe partial class Renderer
{
    readonly SoftwareFrameConverter     converter       = new();
    readonly SoftwareFramePreprocessor  preprocessor    = new();

    /// <summary>Colour filters to apply (null: all at default). Replaced atomically by <see cref="FLSetFilter"/>.</summary>
    volatile SoftwareColorFilter        colorFilter;

    // Prepared (rendered, not yet presented) frame inside the converter's output buffer
    byte*           pendingPtr;
    int             pendingWidth, pendingHeight, pendingStride;
    bool            hasPending;

    // Cached HDR filter chain (rebuilt only when one of its inputs changes)
    string          hdrFilters;
    CropRect        hdrCrop;
    int             hdrW, hdrH, hdrOutW, hdrOutH;
    HDRFormat       hdrFormat;
    ColorRange      hdrRange;
    HDRtoSDRMethod  hdrMethod;
    float           hdrNits;

    string          lastConvertError;

    /// <summary>Diagnostics (tests): the filter-graph stage.</summary>
    internal SoftwareFramePreprocessor Preprocessor => preprocessor;

    /// <summary>Diagnostics (tests): whether conversion contexts, graphs or buffers are still allocated.</summary>
    internal bool HasNativeVideoResources => converter.HasNativeResources || preprocessor.HasNativeResources;

    /// <summary>Size of the last frame handed to the surface (upright, after downscaling), 0 when none.</summary>
    public int      PresentedWidth  { get; private set; }
    /// <summary>Size of the last frame handed to the surface (upright, after downscaling), 0 when none.</summary>
    public int      PresentedHeight { get; private set; }

    /// <summary>Converts <paramref name="frame"/> (and prepares it for presentation); false when there is nothing to show.</summary>
    bool ConvertFrame(VideoFrame frame, bool secondField = false)
    {
        hasPending = false;

        AVFrame* f = frame == null ? null : frame.AVFrame;
        if (f == null || f->width <= 0 || f->height <= 0 || f->data[0] == 0)
            return false;

        var stream  = scfg;
        var tb      = stream != null ? stream.AVStream->time_base : new AVRational { Num = 1, Den = 90000 };
        AVFrame* src= f;

        // 1. Deinterlace (full frame, before crop: field parity)
        if (FieldType != VideoFrameFormat.Progressive && !VideoDecoder.Demuxer.IsReversePlayback)
        {
            var d = preprocessor.Deinterlace(frame, secondField, Frames, FieldType == VideoFrameFormat.InterlacedTopFieldFirst, ucfg.DoubleRate, tb);
            if (d != null)
                src = d;
            else
                LogConvertError(preprocessor.LastError);
        }

        // 2. Presentation size: min(native, viewport) in output (rotated) orientation
        var cropped         = SoftwareVideoGeometry.ClampCrop(crop, src->width, src->height);
        int visibleW        = src->width  - (int)cropped.Width;
        int visibleH        = src->height - (int)cropped.Height;
        var t               = transform;
        var (uprightW, uprightH) = t.OutputSize(visibleW, visibleH);
        var viewport        = Viewport;
        var (outW, outH)    = SoftwareVideoGeometry.PresentationSize(uprightW, uprightH, viewport.Width, viewport.Height);

        // 3. HDR to SDR (also crops and downscales, so the float processing runs on the presentation size)
        var cropForConvert = cropped;
        if (stream != null && stream.HDRFormat != HDRFormat.None && SoftwareFramePreprocessor.CanToneMap)
        {
            var (preW, preH) = t.OutputSize(outW, outH);
            var h = preprocessor.ToneMap(src, GetHDRFilters(src->width, src->height, cropped, preW, preH, stream), tb);
            if (h != null)
            {
                src             = h;
                cropForConvert  = CropRect.Empty;
            }
            else
                LogConvertError(preprocessor.LastError);
        }

        // 4. Crop + swscale (BGRA, scaled) + orientation + colour filters
        var colorSpace = stream != null ? stream.ColorSpace : ColorSpace.None;
        var colorRange = stream != null ? stream.ColorRange : ColorRange.Limited;
        if (!converter.Convert(src, cropForConvert, t, outW, outH, colorSpace, colorRange, colorFilter))
        {
            LogConvertError(converter.LastError);
            return false;
        }

        pendingPtr      = converter.Output;
        pendingWidth    = converter.OutputWidth;
        pendingHeight   = converter.OutputHeight;
        pendingStride   = converter.OutputStride;
        hasPending      = true;

        return true;
    }

    string GetHDRFilters(int width, int height, CropRect cropped, int outW, int outH, MediaStream.VideoStream stream)
    {
        var nits = ucfg.SDRDisplayNits;
        if (hdrFilters == null || hdrW != width || hdrH != height || hdrCrop != cropped || hdrOutW != outW || hdrOutH != outH ||
            hdrFormat != stream.HDRFormat || hdrRange != stream.ColorRange || hdrMethod != ucfg.HDRtoSDRMethod || hdrNits != nits)
        {
            hdrW        = width;
            hdrH        = height;
            hdrCrop     = cropped;
            hdrOutW     = outW;
            hdrOutH     = outH;
            hdrFormat   = stream.HDRFormat;
            hdrRange    = stream.ColorRange;
            hdrMethod   = ucfg.HDRtoSDRMethod;
            hdrNits     = nits;
            hdrFilters  = SoftwareFramePreprocessor.ToneMapFilters(cropped, width, height, outW, outH, hdrFormat, hdrRange, hdrMethod, nits);
        }

        return hdrFilters;
    }

    void LogConvertError(string error)
    {   // Once per distinct error (per-frame failures must not flood the log)
        if (error == null || error == lastConvertError)
            return;

        lastConvertError = error;
        Log.Error($"[Convert] {error}");
    }

    /// <summary>Hands the prepared frame to the surface (render/playback thread, lockRenderLoops).</summary>
    void PresentLocal()
    {
        var s = surface;
        if (s == null)
            return;

        if (!hasPending)
            return;

        s.PresentFrame(new ReadOnlySpan<byte>(pendingPtr, (pendingHeight - 1) * pendingStride + pendingWidth * 4), pendingWidth, pendingHeight, pendingStride);
        PresentedWidth  = pendingWidth;
        PresentedHeight = pendingHeight;
        SwapChain.presentCount++;
    }

    /// <summary>Releases the per-frame state kept for the current picture (no frame to show).</summary>
    void ReleaseFrameState()
    {
        hasPending = false;
        preprocessor.Reset();
    }

    void SwsDispose()
    {
        hasPending      = false;
        PresentedWidth  = PresentedHeight = 0;
        converter.Dispose();
        preprocessor.Dispose();
        hdrFilters      = null;
        lastConvertError= null;
    }
}
