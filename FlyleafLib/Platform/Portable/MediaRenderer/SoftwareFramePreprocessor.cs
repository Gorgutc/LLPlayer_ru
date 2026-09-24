#nullable enable

using System.Globalization;

using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// F-13 portable renderer: the libavfilter stages that run before the BGRA conversion.
/// <list type="bullet">
/// <item><b>Deinterlace</b> (bwdif, yadif as fallback): stateful and one frame behind (the filter needs the next frame).
/// To present frame N in sync, the preprocessor pushes N and — from the <see cref="VideoCache"/> links — N+1, and keeps
/// N's output(s) (one per frame, or both fields with double rate). Sequential frames reuse the running graph (one
/// push per frame); after a seek / discontinuity the graph is rebuilt, warmed up with N-1 when it is still cached, and
/// end-of-stream is signalled when N+1 is not decoded yet (paused, last frame).</item>
/// <item><b>HDR to SDR</b> (zscale + tonemap, PQ / HLG): stateless (one output per input), including the crop and a
/// downscale to the presentation size so the float processing runs on as few pixels as possible.</item>
/// </list>
/// Not thread-safe: the renderer serializes all calls under its render lock.
/// </summary>
internal sealed unsafe class SoftwareFramePreprocessor : IDisposable
{
    #region Deinterlace
    SoftwareFilterGraph?    deint;
    int                     deintW, deintH, deintFmt;
    bool                    deintTff, deintDoubleRate;
    int                     fieldsPerFrame;

    readonly long[]         pushed      = new long[8];  // ids whose outputs are still pending (FIFO)
    int                     pushedHead, pushedCount, outputsForHead;
    long                    lastPushedId = -1;

    AVFrame*                field0, field1;             // outputs of frame 'outId'
    long                    outId       = -1;
    int                     outCount;
    AVFrame*                scratch;                    // look-ahead / warm-up reference and pulled outputs

    /// <summary>The deinterlace filter this FFmpeg build provides (bwdif preferred), or null.</summary>
    public static string? DeinterlaceFilter => deinterlaceFilter ??= SoftwareFilterGraph.IsFilterAvailable("bwdif") ? "bwdif" : SoftwareFilterGraph.IsFilterAvailable("yadif") ? "yadif" : "";
    static string? deinterlaceFilter;

    /// <summary>Number of deinterlace graphs built since creation (diagnostics / tests: sequential frames reuse one).</summary>
    public int      DeinterlaceGraphsCreated    { get; private set; }

    /// <summary>Last error of a filter stage (null when none).</summary>
    public string?  LastError                   { get; private set; }

    /// <summary>
    /// Returns the deinterlaced picture of <paramref name="frame"/> (<paramref name="secondField"/>: the second field's
    /// picture with double rate), or null when deinterlacing is not possible (the caller then shows the frame as is).
    /// The returned frame stays valid until the next call / <see cref="Reset"/>.
    /// </summary>
    public AVFrame* Deinterlace(VideoFrame frame, bool secondField, VideoCache cache, bool topFieldFirst, bool doubleRate, AVRational timeBase)
    {
        AVFrame* f = frame.AVFrame;
        if (f == null || f->width <= 0 || f->height <= 0)
            return null;

        if (frame.Id == outId && outCount > 0)
            return Pick(secondField);

        string? filter = DeinterlaceFilter;
        if (string.IsNullOrEmpty(filter))
            return Error("no deinterlace filter (bwdif / yadif) in this FFmpeg build");

        bool sameParams = deint != null && !deint.Ended &&
            deintW == f->width && deintH == f->height && deintFmt == f->format && deintTff == topFieldFirst && deintDoubleRate == doubleRate;

        bool sequential = false;
        if (sameParams)
        {
            if (IsPushed(frame.Id))
                sequential = true;
            else if (lastPushedId == frame.Id - 1)
                lock (cache) sequential = frame.Prev != null && frame.Prev.Id == lastPushedId;
        }

        if (!sequential)
        {
            ResetDeinterlace();

            string filters = string.Create(CultureInfo.InvariantCulture, $"{filter}=mode={(doubleRate ? "send_field" : "send_frame")}:parity={(topFieldFirst ? "tff" : "bff")}:deint=all");
            deint = SoftwareFilterGraph.Create(f, timeBase, filters, out string? error);
            if (deint == null)
                return Error($"deinterlace graph: {error}");

            DeinterlaceGraphsCreated++;
            deintW          = f->width;
            deintH          = f->height;
            deintFmt        = f->format;
            deintTff        = topFieldFirst;
            deintDoubleRate = doubleRate;
            fieldsPerFrame  = doubleRate ? 2 : 1;

            // Temporal context for the first frame: the previous one when it is still cached
            if (PushNeighbour(cache, frame, prev: true) < 0)
                return Fail();
        }

        bool hasHistory = lastPushedId != -1 && lastPushedId != frame.Id; // a previous frame is in the filter

        if (!IsPushed(frame.Id) && Push(f, frame.Id) < 0)
            return Fail();

        if (Drain() < 0)
            return Fail();

        if (outId != frame.Id)
        {   // Look-ahead: the filter emits N once N+1 (or end of stream) is known
            int ret = PushNeighbour(cache, frame, prev: false);
            if (ret < 0 || Drain() < 0)
                return Fail();

            if (outId != frame.Id)
            {
                // A lone frame (no cached neighbours, e.g. after a paused seek): with prev == cur == next the temporal
                // deinterlacer sees no motion and weaves the fields, so use a spatial (intra-field) one instead.
                if (!hasHistory && !IsPushed(frame.Id + 1))
                    return DeinterlaceSpatial(frame, secondField, topFieldFirst, doubleRate, timeBase);

                if (deint!.PushEof() < 0 || Drain() < 0)
                    return Fail();
            }
        }

        return outId == frame.Id && outCount > 0 ? Pick(secondField) : Error("deinterlacer produced no output");
    }

    /// <summary>Intra-field deinterlacing of a single frame (estdif) when no neighbour frame is available.</summary>
    AVFrame* DeinterlaceSpatial(VideoFrame frame, bool secondField, bool topFieldFirst, bool doubleRate, AVRational timeBase)
    {
        ResetDeinterlace(); // the temporal graph cannot continue from a lone frame

        if (!SoftwareFilterGraph.IsFilterAvailable("estdif"))
            return Error("no spatial deinterlace filter (estdif) for a single frame");

        EnsureScratch();
        string filters = $"estdif=mode={(doubleRate ? "field" : "frame")}:parity={(topFieldFirst ? "tff" : "bff")}:deint=all";
        using var graph = SoftwareFilterGraph.Create(frame.AVFrame, timeBase, filters, out string? error);
        if (graph == null)
            return Error($"spatial deinterlace graph: {error}");

        SpatialFallbacks++;
        if (graph.Push(frame.AVFrame) < 0 || graph.PushEof() < 0)
            return Error("spatial deinterlace push failed");

        while (outCount < 2 && graph.Pull(scratch) >= 0)
        {
            AVFrame* dst = outCount == 0 ? field0 : field1;
            av_frame_unref(dst);
            av_frame_move_ref(dst, scratch);
            outCount++;
        }

        if (outCount == 0)
            return Error("spatial deinterlacer produced no output");

        outId = frame.Id;
        return Pick(secondField);
    }

    /// <summary>Number of lone frames deinterlaced spatially (diagnostics / tests).</summary>
    public int SpatialFallbacks { get; private set; }

    AVFrame* Pick(bool secondField)
        => secondField && outCount > 1 ? field1 : field0;

    int PushNeighbour(VideoCache cache, VideoFrame frame, bool prev)
    {   // References the neighbour under the cache lock (it may be disposed by the cache otherwise)
        long id;
        EnsureScratch();

        lock (cache)
        {
            var n = prev ? frame.Prev : frame.Next;
            AVFrame* nf = n != null ? n.AVFrame : null;
            if (n == null || nf == null || nf->width != deintW || nf->height != deintH || nf->format != deintFmt || av_frame_ref(scratch, nf) < 0)
                return 0;

            id = n.Id;
        }

        int ret = Push(scratch, id);
        av_frame_unref(scratch);

        return ret;
    }

    int Push(AVFrame* f, long id)
    {
        if (pushedCount == pushed.Length)
            return -1; // defensive: outputs stopped following inputs

        int ret = deint!.Push(f);
        if (ret < 0)
        {
            LastError = $"deinterlace push failed ({FFmpegEngine.ErrorCodeToMsg(ret)})";
            return ret;
        }

        pushed[(pushedHead + pushedCount) % pushed.Length] = id;
        pushedCount++;
        lastPushedId = id;

        return 0;
    }

    bool IsPushed(long id)
    {
        for (int i = 0; i < pushedCount; i++)
            if (pushed[(pushedHead + i) % pushed.Length] == id)
                return true;

        return false;
    }

    int Drain()
    {
        EnsureScratch();

        while (true)
        {
            int ret = deint!.Pull(scratch);
            if (ret == AVERROR_EAGAIN || ret == AVERROR_EOF)
                return 0;

            if (ret < 0)
            {
                LastError = $"deinterlace pull failed ({FFmpegEngine.ErrorCodeToMsg(ret)})";
                return ret;
            }

            if (pushedCount == 0)
            {   // Should not happen (more outputs than inputs): drop it
                av_frame_unref(scratch);
                continue;
            }

            long id = pushed[pushedHead];
            if (id != outId)
            {
                ClearOutputs();
                outId = id;
            }

            AVFrame* dst = outputsForHead == 0 ? field0 : field1;
            if (outputsForHead < 2)
            {
                av_frame_unref(dst);
                av_frame_move_ref(dst, scratch);
                outCount = outputsForHead + 1;
            }
            else
                av_frame_unref(scratch);

            if (++outputsForHead >= fieldsPerFrame)
            {
                pushedHead = (pushedHead + 1) % pushed.Length;
                pushedCount--;
                outputsForHead = 0;
            }
        }
    }

    void EnsureScratch()
    {
        if (scratch == null)
        {
            scratch = av_frame_alloc();
            field0  = av_frame_alloc();
            field1  = av_frame_alloc();
        }
    }

    void ClearOutputs()
    {
        if (field0 != null) av_frame_unref(field0);
        if (field1 != null) av_frame_unref(field1);
        outCount = 0;
        outId    = -1;
    }

    AVFrame* Fail()
    {
        ResetDeinterlace();
        return null;
    }

    AVFrame* Error(string error)
    {
        LastError = error;
        return null;
    }

    /// <summary>Drops the deinterlace graph and its cached outputs (seek, stream change, disabled).</summary>
    public void ResetDeinterlace()
    {
        deint?.Dispose();
        deint           = null;
        pushedHead      = pushedCount = outputsForHead = 0;
        lastPushedId    = -1;
        ClearOutputs();
    }
    #endregion

    #region HDR to SDR
    SoftwareFilterGraph?    hdr;
    string?                 hdrFilters;
    int                     hdrInW, hdrInH, hdrInFmt;
    AVFrame*                hdrOut;

    /// <summary>Whether this FFmpeg build can tone-map (zscale + tonemap).</summary>
    public static bool CanToneMap => canToneMap ??= SoftwareFilterGraph.IsFilterAvailable("zscale") && SoftwareFilterGraph.IsFilterAvailable("tonemap");
    static bool? canToneMap;

    /// <summary>Number of HDR graphs built since creation (diagnostics / tests).</summary>
    public int HDRGraphsCreated { get; private set; }

    /// <summary>
    /// Filter chain converting a PQ / HLG BT.2020 frame into 8-bit BT.709 SDR RGB (gbrp) of
    /// <paramref name="outWidth"/> x <paramref name="outHeight"/> (crop first). Mapping of the Windows pixel-shader
    /// methods: Hable -> hable, Reinhard -> reinhard, Aces -> mobius (no ACES curve in libavfilter), None -> clip; the SDR
    /// display peak (<paramref name="sdrNits"/>) sets the nominal peak luminance of the linearization (Windows: HDRTone).
    /// </summary>
    public static string ToneMapFilters(CropRect crop, int width, int height, int outWidth, int outHeight, HDRFormat format, ColorRange range, HDRtoSDRMethod method, float sdrNits)
    {
        var ci = CultureInfo.InvariantCulture;
        string cropFilter = crop.IsEmpty ? "" : string.Create(ci, $"crop=w={width - (int)crop.Width}:h={height - (int)crop.Height}:x={crop.Left}:y={crop.Top}:exact=1,");
        string tin  = format == HDRFormat.HLG ? "arib-std-b67" : "smpte2084";
        string rin  = range == ColorRange.Full ? "pc" : "tv";
        string algo = method switch
        {
            HDRtoSDRMethod.Hable    => "hable",
            HDRtoSDRMethod.Reinhard => "reinhard",
            HDRtoSDRMethod.Aces     => "mobius",
            _                       => "clip"
        };
        float nits = sdrNits > 0 ? sdrNits : 200;

        return string.Create(ci,
            $"{cropFilter}zscale=w={outWidth}:h={outHeight}:f=bilinear:tin={tin}:min=bt2020nc:pin=bt2020:rin={rin}:t=linear:npl={nits:0.###}," +
            $"format=gbrpf32le,zscale=p=bt709,tonemap={algo}:desat=0,zscale=t=bt709:m=bt709:r=pc,format=gbrp");
    }

    /// <summary>
    /// Tone-maps <paramref name="src"/> with <paramref name="filters"/> (see <see cref="ToneMapFilters"/>); the graph is
    /// reused while the filters and the input format stay the same. Returns null on failure (the caller then converts
    /// the frame without tone mapping). The result stays valid until the next call / <see cref="Reset"/>.
    /// </summary>
    public AVFrame* ToneMap(AVFrame* src, string filters, AVRational timeBase)
    {
        if (src == null)
            return null;

        if (hdr == null || hdrFilters != filters || hdrInW != src->width || hdrInH != src->height || hdrInFmt != src->format)
        {
            ResetToneMap();
            hdr = SoftwareFilterGraph.Create(src, timeBase, filters, out string? error);
            if (hdr == null)
                return Error($"HDR graph: {error}");

            HDRGraphsCreated++;
            hdrFilters  = filters;
            hdrInW      = src->width;
            hdrInH      = src->height;
            hdrInFmt    = src->format;
        }

        if (hdrOut == null)
            hdrOut = av_frame_alloc();
        else
            av_frame_unref(hdrOut);

        int ret = hdr.Push(src);
        if (ret >= 0)
            ret = hdr.Pull(hdrOut);

        if (ret < 0)
        {
            LastError = $"HDR filter failed ({FFmpegEngine.ErrorCodeToMsg(ret)})";
            ResetToneMap();
            return null;
        }

        return hdrOut;
    }

    /// <summary>One-shot tone mapping (snapshots): a temporary graph, the result is owned by the caller (av_frame_free).</summary>
    public static AVFrame* ToneMapOnce(AVFrame* src, string filters, AVRational timeBase, out string? error)
    {
        using var graph = SoftwareFilterGraph.Create(src, timeBase, filters, out error);
        if (graph == null)
            return null;

        AVFrame* dst = av_frame_alloc();
        int ret = graph.Push(src);
        if (ret >= 0)
            ret = graph.PushEof();
        if (ret >= 0)
            ret = graph.Pull(dst);

        if (ret < 0)
        {
            error = FFmpegEngine.ErrorCodeToMsg(ret);
            av_frame_free(&dst);
            return null;
        }

        return dst;
    }

    public void ResetToneMap()
    {
        hdr?.Dispose();
        hdr         = null;
        hdrFilters  = null;

        if (hdrOut != null)
            av_frame_unref(hdrOut);
    }
    #endregion

    /// <summary>Whether a filter graph or a frame is allocated (false after <see cref="Dispose"/>).</summary>
    public bool HasNativeResources => deint != null || hdr != null || scratch != null || field0 != null || field1 != null || hdrOut != null;

    /// <summary>Drops all graphs and cached frames (keeps nothing referenced).</summary>
    public void Reset()
    {
        ResetDeinterlace();
        ResetToneMap();
    }

    public void Dispose()
    {
        Reset();

        if (scratch != null) { fixed (AVFrame** p = &scratch) av_frame_free(p); }
        if (field0  != null) { fixed (AVFrame** p = &field0)  av_frame_free(p); }
        if (field1  != null) { fixed (AVFrame** p = &field1)  av_frame_free(p); }
        if (hdrOut  != null) { fixed (AVFrame** p = &hdrOut)  av_frame_free(p); }
    }
}
