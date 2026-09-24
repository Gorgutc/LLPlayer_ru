#nullable enable

using System.Runtime.InteropServices;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// F-13 portable renderer: converts a software <see cref="AVFrame"/> into a ready-to-show BGRA32 image — crop, colour
/// conversion and scaling (swscale), orientation (<see cref="VideoTransform"/>) and colour filters
/// (<see cref="SoftwareColorFilter"/>). All native buffers and the swscale context are reused between calls; a
/// conversion performs no managed allocations. Not thread-safe (the renderer serializes calls under its render lock).
/// </summary>
internal sealed unsafe class SoftwareFrameConverter : IDisposable
{
    const int   StrideAlign = 64;

    SwsContext*     sws;
    int             swsSrcW, swsSrcH, swsDstW, swsDstH;
    SwsFlags        swsFlags;
    AVPixelFormat   swsSrcFmt = AVPixelFormat.None;
    ColorSpace      swsColorSpace;
    ColorRange      swsColorRange;
    bool            swsDetailsSet;

    byte*           passBuf;
    nuint           passBufSize;
    byte*           outBuf;
    nuint           outBufSize;

    // Reused marshalling arrays for sws_scale (pinned by the P/Invoke marshaller, no per-call allocation)
    readonly byte*[]    srcData     = new byte*[4];
    readonly int[]      srcStride   = new int[4];
    readonly byte*[]    dstData     = new byte*[4];
    readonly int[]      dstStride   = new int[4];

    /// <summary>The last converted image (valid until the next <see cref="Convert"/> / <see cref="Dispose"/>).</summary>
    public byte*    Output          { get; private set; }
    public int      OutputWidth     { get; private set; }
    public int      OutputHeight    { get; private set; }
    public int      OutputStride    { get; private set; }

    /// <summary>Why the last <see cref="Convert"/> failed (null on success).</summary>
    public string?  LastError       { get; private set; }

    /// <summary>True when the swscale context was (re)created by the last conversion (format / size change).</summary>
    public bool     ContextChanged  { get; private set; }

    /// <summary>Whether a swscale context or a pixel buffer is allocated (false after <see cref="Dispose"/>).</summary>
    public bool     HasNativeResources => sws != null || passBuf != null || outBuf != null;

    /// <summary>
    /// Converts <paramref name="src"/> (cropped by <paramref name="crop"/>, which is clamped to the frame) to a BGRA
    /// image of <paramref name="outWidth"/> x <paramref name="outHeight"/> in output orientation (after
    /// <paramref name="transform"/>); scaling is bilinear, bicubic with <paramref name="highQuality"/>.
    /// <paramref name="colorSpace"/>/<paramref name="colorRange"/> select the YUV matrix and range (only the range matters
    /// for gray input; ignored for RGB input). <paramref name="filter"/> (optional) is applied to the result.
    /// </summary>
    public bool Convert(AVFrame* src, CropRect crop, VideoTransform transform, int outWidth, int outHeight,
        ColorSpace colorSpace, ColorRange colorRange, SoftwareColorFilter? filter, bool highQuality = false)
    {
        LastError       = null;
        ContextChanged  = false;

        if (src == null || src->width <= 0 || src->height <= 0 || src->data[0] == 0)
            return Fail("no frame data");

        if (outWidth <= 0 || outHeight <= 0)
            return Fail($"invalid output size {outWidth}x{outHeight}");

        var format  = (AVPixelFormat)src->format;
        var desc    = av_pix_fmt_desc_get(format);
        if (desc == null || (desc->flags & PixFmtFlags.Hwaccel) != 0)
            return Fail($"unsupported pixel format {format}");

        crop = SoftwareVideoGeometry.ClampCrop(crop, src->width, src->height);
        int srcW = src->width  - (int)(crop.Left + crop.Right);
        int srcH = src->height - (int)(crop.Top  + crop.Bottom);

        FillSource(src, desc, crop, srcH, transform.SourceVFlip);

        var (sw, sh) = transform.OutputSize(outWidth, outHeight); // pre-transform (converted) size == inverse of output
        if (!EnsureContext(srcW, srcH, format, sw, sh, highQuality ? SwsFlags.Bicubic : SwsFlags.Bilinear))
            return false;

        if (!IsRgb(desc) && (!swsDetailsSet || swsColorSpace != colorSpace || swsColorRange != colorRange))
        {
            swsColorSpace   = colorSpace;
            swsColorRange   = colorRange;
            swsDetailsSet   = true;

            int cs = colorSpace switch
            {
                ColorSpace.Bt709    => 1,   // SWS_CS_ITU709
                ColorSpace.Bt2020   => 9,   // SWS_CS_BT2020
                _                   => 5    // SWS_CS_ITU601 (SWS_CS_DEFAULT)
            };

            _ = sws_setColorspaceDetails(sws, sws_getCoefficients(cs), colorRange == ColorRange.Full ? 1 : 0, sws_getCoefficients(5), 1, 0, 1 << 16, 1 << 16);
        }

        int outStride = Align(outWidth * 4);
        EnsureBuffer(ref outBuf, ref outBufSize, (nuint)outStride * (nuint)outHeight);

        byte*   convBuf;
        int     convStride;
        if (transform.NeedsPass)
        {
            convStride = Align(sw * 4);
            EnsureBuffer(ref passBuf, ref passBufSize, (nuint)convStride * (nuint)sh);
            convBuf = passBuf;
        }
        else
        {
            convStride  = outStride;
            convBuf     = outBuf;
        }

        dstData[0]  = convBuf;
        dstStride[0]= convStride;
        dstData[1]  = dstData[2] = dstData[3] = null;
        dstStride[1]= dstStride[2] = dstStride[3] = 0;

        int ret = sws_scale(sws, srcData, srcStride, 0, srcH, dstData, dstStride);
        if (ret <= 0)
            return Fail($"sws_scale failed ({ret})");

        if (transform.NeedsPass)
            BgraPixelOps.ApplyPass(transform, convBuf, convStride, sw, sh, outBuf, outStride);

        filter?.Apply(outBuf, outStride, outWidth, outHeight);

        Output          = outBuf;
        OutputWidth     = outWidth;
        OutputHeight    = outHeight;
        OutputStride    = outStride;

        return true;
    }

    void FillSource(AVFrame* src, AVPixFmtDescriptor* desc, CropRect crop, int srcH, bool vflip)
    {
        int* steps      = stackalloc int[4];
        int* stepComps  = stackalloc int[4];
        av_image_fill_max_pixsteps(steps, stepComps, desc);

        bool pal        = (desc->flags & PixFmtFlags.Pal) != 0;
        bool bitstream  = (desc->flags & PixFmtFlags.Bitstream) != 0;
        int  planes     = pal ? 1 : av_pix_fmt_count_planes((AVPixelFormat)src->format);

        for (int p = 0; p < 4; p++)
        {
            if (p >= planes)
            {   // Palette (PAL8) stays as is; unused planes cleared
                srcData[p]  = pal && p == 1 ? (byte*)src->data[1] : null;
                srcStride[p]= pal && p == 1 ? src->linesize[1] : 0;
                continue;
            }

            bool    chroma  = p == 1 || p == 2;
            int     shiftX  = chroma ? desc->log2_chroma_w : 0;
            int     shiftY  = chroma ? desc->log2_chroma_h : 0;
            int     ls      = src->linesize[p];
            byte*   ptr     = (byte*)src->data[p] + (long)(crop.Top >> shiftY) * ls;

            if (!bitstream)
                ptr += (long)(crop.Left >> shiftX) * steps[p];

            if (vflip)
            {   // Start at the last row of the (cropped) plane and walk upwards
                int rows = -((-srcH) >> shiftY); // AV_CEIL_RSHIFT
                ptr += (long)(rows - 1) * ls;
                ls = -ls;
            }

            srcData[p]  = ptr;
            srcStride[p]= ls;
        }
    }

    bool EnsureContext(int srcW, int srcH, AVPixelFormat format, int dstW, int dstH, SwsFlags flags)
    {
        if (sws != null && srcW == swsSrcW && srcH == swsSrcH && format == swsSrcFmt && dstW == swsDstW && dstH == swsDstH && flags == swsFlags)
            return true;

        sws = sws_getCachedContext(sws, srcW, srcH, format, dstW, dstH, AVPixelFormat.Bgra, flags, null, null, null);
        if (sws == null)
        {
            swsSrcFmt = AVPixelFormat.None;
            return Fail($"failed to allocate SwsContext ({format} {srcW}x{srcH} -> {dstW}x{dstH})");
        }

        swsSrcW         = srcW;
        swsSrcH         = srcH;
        swsSrcFmt       = format;
        swsDstW         = dstW;
        swsDstH         = dstH;
        swsFlags        = flags;
        swsDetailsSet   = false;
        ContextChanged  = true;

        return true;
    }

    static bool IsRgb(AVPixFmtDescriptor* desc)
        => (desc->flags & PixFmtFlags.Rgb) != 0;

    static int Align(int bytes)
        => (bytes + StrideAlign - 1) & ~(StrideAlign - 1);

    static void EnsureBuffer(ref byte* buffer, ref nuint size, nuint required)
    {
        if (buffer != null && size >= required)
            return;

        if (buffer != null)
            NativeMemory.AlignedFree(buffer);

        buffer  = (byte*)NativeMemory.AlignedAlloc(required, StrideAlign);
        size    = required;
    }

    bool Fail(string error)
    {
        LastError = error;
        return false;
    }

    public void Dispose()
    {
        if (sws != null)
        {
            sws_freeContext(sws);
            sws = null;
        }

        swsSrcFmt = AVPixelFormat.None;

        if (passBuf != null)
        {
            NativeMemory.AlignedFree(passBuf);
            passBuf     = null;
            passBufSize = 0;
        }

        if (outBuf != null)
        {
            NativeMemory.AlignedFree(outBuf);
            outBuf      = null;
            outBufSize  = 0;
        }

        Output = null;
        OutputWidth = OutputHeight = OutputStride = 0;
    }
}
