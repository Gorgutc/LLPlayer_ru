#nullable enable

using System.Globalization;
using System.Runtime.InteropServices;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// F-13 portable renderer: a minimal libavfilter graph (<c>buffer -> filters -> buffersink</c>) for software frames,
/// used for deinterlacing (bwdif / yadif) and HDR to SDR (zscale + tonemap). Not thread-safe: the owner serializes all
/// calls (the renderer does so under its render lock).
/// </summary>
internal sealed unsafe class SoftwareFilterGraph : IDisposable
{
    AVFilterGraph*      graph;
    AVFilterContext*    src;
    AVFilterContext*    sink;

    /// <summary>The filter chain this graph was built with (between the buffer source and the sink).</summary>
    public string       Filters     { get; }

    /// <summary>True once end-of-stream was pushed (no more input is accepted).</summary>
    public bool         Ended       { get; private set; }

    SoftwareFilterGraph(string filters) => Filters = filters;

    /// <summary>Whether this FFmpeg build provides the filter <paramref name="name"/>.</summary>
    public static bool IsFilterAvailable(string name)
        => avfilter_get_by_name(name) != null;

    /// <summary>
    /// Builds and configures a graph whose input matches <paramref name="sample"/> (size, pixel format, aspect ratio,
    /// colour properties). Returns null (with <paramref name="error"/>) on failure.
    /// </summary>
    public static SoftwareFilterGraph? Create(AVFrame* sample, AVRational timeBase, string filters, out string? error)
    {
        SoftwareFilterGraph fg = new(filters);
        if (fg.Configure(sample, timeBase, out error))
            return fg;

        fg.Dispose();
        return null;
    }

    bool Configure(AVFrame* sample, AVRational timeBase, out string? error)
    {
        error = null;

        graph = avfilter_graph_alloc();
        if (graph == null)
            { error = "avfilter_graph_alloc failed"; return false; }

        if (timeBase.Num <= 0 || timeBase.Den <= 0)
            timeBase = new() { Num = 1, Den = 90000 };

        AVRational sar = sample->sample_aspect_ratio;
        if (sar.Num <= 0 || sar.Den <= 0)
            sar = new() { Num = 1, Den = 1 };

        var ci = CultureInfo.InvariantCulture;
        string desc = string.Create(ci,
            $"buffer=video_size={sample->width}x{sample->height}:pix_fmt={sample->format}:time_base={timeBase.Num}/{timeBase.Den}" +
            $":pixel_aspect={sar.Num}/{sar.Den}:colorspace={(int)sample->colorspace}:range={(int)sample->color_range}," +
            $"{Filters},buffersink");

        int ret = avfilter_graph_parse_ptr(graph, desc, (AVFilterInOut**)null, (AVFilterInOut**)null, null);
        if (ret < 0)
            { error = $"parse '{Filters}' failed ({FFmpegEngine.ErrorCodeToMsg(ret)})"; return false; }

        for (uint i = 0; i < graph->nb_filters; i++)
        {
            AVFilterContext* ctx = graph->filters[i];
            string? name = Marshal.PtrToStringAnsi((nint)ctx->filter->name);
            if (name == "buffer")
                src = ctx;
            else if (name == "buffersink")
                sink = ctx;
        }

        if (src == null || sink == null)
            { error = "buffer/buffersink not found"; return false; }

        ret = avfilter_graph_config(graph, null);
        if (ret < 0)
            { error = $"config '{Filters}' failed ({FFmpegEngine.ErrorCodeToMsg(ret)})"; return false; }

        return true;
    }

    /// <summary>Pushes a new reference of <paramref name="frame"/> (the caller keeps its own reference).</summary>
    public int Push(AVFrame* frame)
        => Ended ? AVERROR_EOF : av_buffersrc_add_frame_flags(src, frame, AVBuffersrcFlag.KeepRef);

    /// <summary>Signals end of stream: filters with look-ahead (deinterlacers) then output their last frame.</summary>
    public int PushEof()
    {
        if (Ended)
            return 0;

        Ended = true;
        return av_buffersrc_add_frame_flags(src, null, 0);
    }

    /// <summary>Moves the next available output into <paramref name="dst"/> (must be unreferenced). 0, EAGAIN or EOF.</summary>
    public int Pull(AVFrame* dst)
        => av_buffersink_get_frame_flags(sink, dst, 0);

    public void Dispose()
    {
        if (graph != null)
        {
            fixed (AVFilterGraph** ptr = &graph)
                avfilter_graph_free(ptr);

            graph = null;
        }

        src = sink = null;
    }
}
