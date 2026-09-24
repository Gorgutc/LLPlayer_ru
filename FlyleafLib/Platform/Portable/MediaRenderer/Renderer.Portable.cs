using System.Runtime.InteropServices;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;

using static FlyleafLib.Config;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// F-13 portable (Linux) software renderer. Replaces the Direct3D11 renderer of MediaRenderer/Renderer.*.cs (Windows
/// only) with the same class name, namespace, constructor and the members the shared engine calls.
/// <para>
/// Pipeline: the video decoder decodes in software; <c>FillPlanes</c> moves each decoded AVFrame into a
/// <see cref="VideoFrame"/>; <c>RenderPlay</c> converts the next frame to BGRA32 with FFmpeg swscale (cropped, not
/// scaled) and <c>PresentPlay</c> hands it to the host's <see cref="IVideoSurface"/> at presentation time. The host
/// scales the frame into <see cref="Viewport"/> (control pixels), applying <see cref="Rotation"/>/<see cref="HFlip"/>/
/// <see cref="VFlip"/> and painting <see cref="VPConfig.BackColor"/> around it.
/// </para>
/// <para>Without a <see cref="Surface"/> (headless) frames are still decoded, queued and disposed at playback pace.</para>
/// </summary>
public unsafe partial class Renderer : NotifyPropertyChanged, IVP
{
    public int                  UniqueId        { get; private set; }
    public bool                 Disposed        { get; private set; } = true;
    public SwapChain            SwapChain       { get; private set; }
    public VideoDecoder         VideoDecoder    { get; private set; }
    public readonly VideoCache  Frames;
    public Config               Config          { get; private set; }
    internal VideoConfig ucfg;

    /// <summary>The software adapter (see <see cref="VideoEngine.GPUAdapters"/>).</summary>
    public GPUAdapter           GPUAdapter      => Engine.Video?.GPUAdapters.GetValueOrDefault(VideoEngine.SoftwareAdapterLuid);

    internal object         lockDevice = new();
    internal LogHandler     Log;
    Player                  player;

    public Renderer(VideoDecoder videoDecoder, int uniqueId = -1, Player player = null)
    {
        UniqueId    = uniqueId == -1 ? GetUniqueId() : uniqueId;
        Log         = new(("[#" + UniqueId + "]").PadRight(8, ' ') + " [Renderer      ] ");
        VideoDecoder= videoDecoder;
        Config      = videoDecoder.Config;
        this.player = player;
        ucfg        = Config.Video;
        Frames      = new(Config.Decoder);
        SwapChain   = new(this);
        FillPlanes  = SwFillPlanes;

        SetupLocal();
    }

    #region Host (IVideoSurface)
    IVideoSurface surface;

    /// <summary>
    /// Host video output. Setting it (from any thread) attaches the surface and repaints the current frame;
    /// null detaches it (frames are then decoded and dropped at playback pace).
    /// </summary>
    public IVideoSurface Surface
    {
        get => surface;
        set
        {
            lock (lockRenderLoops)
            {
                if (surface == value)
                    return;

                surface = value;
                SwapChain.presentCount = 0;
            }

            if (value != null)
                RenderIdleStart(true);
        }
    }

    /// <summary>
    /// Reports the host control size in device pixels (call on every resize). Drives <see cref="Viewport"/>,
    /// aspect-ratio fill and bitmap-subtitle positioning.
    /// </summary>
    public void SetControlSize(int width, int height)
    {
        width   = Math.Max(0, width);
        height  = Math.Max(0, height);

        if (width == ControlWidth && height == ControlHeight)
            return;

        ((IVP)this).UpdateSize(width, height);
        VPRequest(VPRequestType.Resize);
    }

    /// <summary>Reports the monitor the host control is on (max resolution for stream suggestion, SDR nits).</summary>
    public void SetMonitor(GPUOutput monitor)
    {
        if (monitor != null)
            ((IVP)this).MonitorChanged(monitor);
    }

    /// <summary>Effective rotation in degrees (user + stream metadata) the host must apply when drawing.</summary>
    public uint                 Rotation        => rotation;

    /// <summary>Whether the host must mirror the frame horizontally when drawing.</summary>
    public bool                 HFlip           => ucfg.hflip;

    /// <summary>Whether the host must mirror the frame vertically when drawing (user + stream metadata).</summary>
    public bool                 VFlip           => ucfg.vflip ^ (scfg != null && scfg.VFlip);
    #endregion

    #region Hardware decoding hooks (never active: the portable build decodes in software)
    internal AVBufferRef*       ffDevice        => null;
    internal AVBufferRef*       ffFrames        => null;
    internal HWFramesInfo       ffFramesInfo    = new();
    internal HWTextureDesc      ffTextureDesc   = new();
    internal bool ConfigHWFrames() => false;

    internal struct HWFramesInfo    { public AVCodecID CodecId; }
    internal struct HWTextureDesc   { public uint Width, Height; }
    #endregion

    #region Setup / Reset / Dispose
    internal void Setup()
    {
        lock (lockDevice)
            SetupLocal();
    }
    void SetupLocal()
    {
        DisposeLocal();

        if (CanDebug) Log.Debug("Initializing (software)");

        Disposed    = false;
        canIdle     = true;
        FLFiltersSetup();

        if (CanInfo) Log.Info("Initialized (software renderer)");
    }

    bool isDeviceReset;
    internal void Reset(bool pausePlayer = true, bool fromDecoder = false)
    {
        lock (lockDevice)
            ResetLocal(pausePlayer, fromDecoder);
    }
    void ResetLocal(bool pausePlayer = true, bool fromDecoder = false)
    {   // Don't call this from VideoDecoder's RunInternal (deadlock)
        var stream      = VideoDecoder.VideoStream;
        var wasPlaying  = pausePlayer && player != null && player.Status == MediaPlayer.Status.Playing;
        var wasRunning  = !fromDecoder && VideoDecoder.IsRunning;

        isDeviceReset = true;

        // Stop loops (Play or Idle)
        if (wasPlaying)
            player.Pause();
        else
            RenderIdleStop();

        if (!fromDecoder)
            VideoDecoder.Dispose();
        SetupLocal();
        isDeviceReset = false;

        if (stream == null)
            return;

        if (!fromDecoder)
        {
            VideoDecoder.Open(stream);
            VideoDecoder.keyPacketRequired  = !VideoDecoder.isIntraOnly;
            VideoDecoder.keyFrameRequired   = false;
        }

        if (wasPlaying)
            player.Play();
        else if (wasRunning)
            VideoDecoder.Start();
    }

    internal void Dispose()
    {
        lock (lockDevice)
            DisposeLocal();
    }
    void DisposeLocal()
    {
        lock (lockDevice)
        {
            if (Disposed)
                return;

            if (CanDebug) Log.Debug("Disposing");

            Disposed = true;

            if (!isDeviceReset)
            {   // Stop loops (deadlock from loop threads)
                RenderIdleStop();
                player?.Pause();
                VideoDecoder.Dispose();
            }

            if (!isDeviceReset)
                RenderIdleStop(); // Ensures it didn't start again (after CanPresent = false)

            lock (lockRenderLoops)
            {
                Frames.Dispose();
                SwsDispose();
                try { surface?.ClearFrame(); } catch (Exception e) { Log.Warn($"[Dispose] Surface clear failed ({e.Message})"); }
            }

            if (CanInfo) Log.Info("Disposed");
        }
    }
    #endregion

    #region Frames (decoder thread)
    internal unsafe delegate VideoFrame FillPlanesDelegate(ref AVFrame* frame);

    /// <summary>Called by the VideoDecoder (decoder thread) for every decoded frame.</summary>
    internal FillPlanesDelegate FillPlanes;

    VideoFrame SwFillPlanes(ref AVFrame* frame)
    {
        var stream = scfg ?? VideoDecoder.VideoStream;

        VideoFrame mFrame = new()
        {
            Timestamp   = (long)(frame->pts * stream.Timebase) - VideoDecoder.Demuxer.StartTime,
            AVFrame     = av_frame_alloc()
        };

        av_frame_move_ref(mFrame.AVFrame, frame); // takes over the decoder's reference (leaves 'frame' blank for reuse)

        return mFrame;
    }
    #endregion

    #region Software conversion (swscale -> BGRA32)
    SwsContext*     swsCtx;
    int             swsWidth, swsHeight;
    AVPixelFormat   swsFormat = AVPixelFormat.None;
    ColorSpace      swsColorSpace;
    ColorRange      swsColorRange;

    byte*           bgraBuffer;
    nuint           bgraBufferSize;
    int             bgraStride;

    // Prepared (rendered, not yet presented) frame region inside bgraBuffer
    byte*           pendingPtr;
    int             pendingWidth, pendingHeight;
    bool            hasPending;

    /// <summary>Converts <paramref name="frame"/> into bgraBuffer and prepares the cropped region for presentation.</summary>
    bool ConvertFrame(VideoFrame frame)
    {
        hasPending = false;

        AVFrame* f = frame == null ? null : frame.AVFrame;
        if (f == null || f->width <= 0 || f->height <= 0 || f->data[0] == 0)
            return false;

        int width   = f->width;
        int height  = f->height;
        var format  = (AVPixelFormat)f->format;

        if (swsCtx == null || width != swsWidth || height != swsHeight || format != swsFormat)
        {
            swsCtx = sws_getCachedContext(swsCtx, width, height, format, width, height, AVPixelFormat.Bgra, SwsFlags.Bilinear, null, null, null);
            if (swsCtx == null)
            {
                Log.Error($"Failed to allocate SwsContext ({format} {width}x{height})");
                swsFormat = AVPixelFormat.None;
                return false;
            }

            swsWidth        = width;
            swsHeight       = height;
            swsFormat       = format;
            swsColorSpace   = ColorSpace.None;
            swsColorRange   = ColorRange.None;
        }

        if (scfg != null && scfg.ColorType == ColorType.YUV && (scfg.ColorSpace != swsColorSpace || scfg.ColorRange != swsColorRange))
        {
            swsColorSpace   = scfg.ColorSpace;
            swsColorRange   = scfg.ColorRange;

            int cs = swsColorSpace switch
            {
                ColorSpace.Bt709    => 1,   // SWS_CS_ITU709
                ColorSpace.Bt2020   => 9,   // SWS_CS_BT2020
                _                   => 5    // SWS_CS_ITU601 (SWS_CS_DEFAULT)
            };

            int* coeffs = sws_getCoefficients(cs);
            _ = sws_setColorspaceDetails(swsCtx, coeffs, swsColorRange == ColorRange.Full ? 1 : 0, sws_getCoefficients(5), 1, 0, 1 << 16, 1 << 16);
        }

        int     stride  = width * 4;
        nuint   size    = (nuint)stride * (nuint)height;
        if (bgraBuffer == null || bgraBufferSize < size)
        {
            if (bgraBuffer != null)
                NativeMemory.AlignedFree(bgraBuffer);

            bgraBuffer      = (byte*)NativeMemory.AlignedAlloc(size, 64);
            bgraBufferSize  = size;
        }
        bgraStride = stride;

        int ret = sws_scale(swsCtx,
            f->data.        ToRawArray(),
            f->linesize.    ToArray(),
            0, height,
            [bgraBuffer, null, null, null],
            [stride, 0, 0, 0]);

        if (ret <= 0)
            return false;

        // Crop (stream + codec + user); clamp in case of an oversized user crop
        uint left   = Math.Min(crop.Left,   (uint)width  - 1);
        uint top    = Math.Min(crop.Top,    (uint)height - 1);
        uint right  = Math.Min(crop.Right,  (uint)width  - 1 - left);
        uint bottom = Math.Min(crop.Bottom, (uint)height - 1 - top);

        pendingPtr      = bgraBuffer + (top * (uint)stride) + (left * 4);
        pendingWidth    = width  - (int)(left + right);
        pendingHeight   = height - (int)(top + bottom);
        hasPending      = true;

        return true;
    }

    /// <summary>Hands the prepared frame to the surface (render/playback thread, lockRenderLoops).</summary>
    void PresentLocal()
    {
        var s = surface;
        if (s == null)
            return;

        if (!hasPending)
            return;

        s.PresentFrame(new ReadOnlySpan<byte>(pendingPtr, (pendingHeight - 1) * bgraStride + pendingWidth * 4), pendingWidth, pendingHeight, bgraStride);
        SwapChain.presentCount++;
    }

    void SwsDispose()
    {
        hasPending = false;

        if (swsCtx != null)
        {
            sws_freeContext(swsCtx);
            swsCtx = null;
        }

        swsFormat = AVPixelFormat.None;

        if (bgraBuffer != null)
        {
            NativeMemory.AlignedFree(bgraBuffer);
            bgraBuffer      = null;
            bgraBufferSize  = 0;
        }
    }
    #endregion

    #region Render loops (same scheduling as the Windows Renderer.Present.cs)
    long            renderRequestAt, lastRenderAt;
    volatile bool   canIdle;
    volatile bool   isIdleRunning;
    object          lockRenderLoops = new();

    internal void RenderRequest(VideoFrame frame = null, bool forceClear = false)
    {
        lock (lockRenderLoops)
        {
            renderRequestAt = DateTime.UtcNow.Ticks;

            if ((frame != null || forceClear))
                Frames.SetRendererFrame(frame);

            if (!SwapChain.CanPresent || !canIdle || isIdleRunning)
                return;

            isIdleRunning = true;
        }

        Task.Run(RenderIdleLoop);
    }
    internal void RenderIdleStart(bool force = false)
    {
        lock (lockRenderLoops)
        {
            canIdle = true;
            if (force)
                renderRequestAt = DateTime.UtcNow.Ticks;

            if (renderRequestAt > lastRenderAt)
                RenderRequest();
        }
    }
    internal void RenderIdleStop()
    {
        canIdle = false;
        while (isIdleRunning)
            { canIdle = false; Thread.Sleep(1); }
    }
    void RenderIdleLoop()
    {
        int rechecks = 1000; // Awake for ~5sec when Idle
        while (SwapChain.CanPresent)
        {
            while (renderRequestAt <= lastRenderAt && rechecks-- > 0)
            {
                if (!canIdle || !SwapChain.CanPresent)
                    { rechecks = 0; break; }

                Thread.Sleep(5);
            }

            if (rechecks < 1)
                break;

            rechecks = 1000;
            RenderIdle();
        }

        lock (lockRenderLoops) // To avoid race condition*?
        {
            isIdleRunning = false;
            if (renderRequestAt > lastRenderAt && canIdle && SwapChain.CanPresent)
                RenderRequest();
        }
    }
    bool RenderIdle()
    {
        try
        {
            lastRenderAt = DateTime.UtcNow.Ticks;

            if (!SwapChain.CanPresent)
                return true;

            lock (lockRenderLoops)
            {
                ProcessRequests();

                if (Frames.RendererFrame != null && ConvertFrame(Frames.RendererFrame))
                {
                    PresentLocal();
                    return true;
                }

                if (!Config.Video.ClearScreen)
                    return true;

                surface?.ClearFrame();
            }

            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[RenderIdle] Failed ({e.Message})");

            return false;
        }
    }

    internal bool RefreshPlay(bool secondField)
    {   // Tries to keep ~60fps refreshes within/during playback
        if (lastRenderAt >= renderRequestAt)
            return false;

        RenderIdle();

        return true;
    }
    internal bool RenderPlay(VideoFrame frame, bool secondField)
    {
        try
        {
            lastRenderAt = DateTime.UtcNow.Ticks;

            if (!SwapChain.CanPresent)
            {
                if (Config.Player.SnapshotAlways)
                    lock (lockRenderLoops)
                    {
                        ProcessRequests();
                        Frames.SetRendererFrame(frame);
                    }

                return true;
            }

            lock (lockRenderLoops)
            {
                ProcessRequests();
                ConvertFrame(frame);
                Frames.SetRendererFrame(frame);
            }

            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[RenderPlay] Failed ({e.Message})");

            return false;
        }
    }
    internal bool PresentPlay()
    {
        try
        {
            if (SwapChain.CanPresent)
                lock (lockRenderLoops)
                    PresentLocal();

            return true;
        }
        catch (Exception e)
        {   // A failing host surface must not stop playback (unlike a lost D3D11 device on Windows)
            Log.Error($"[PresentPlay] Failed ({e.Message})");
            return false;
        }
    }

    public void ClearScreen(bool force = false, bool rendererFrame = true)
    {
        if (force)
        {
            lock (lockDevice)
            {
                if (SwapChain.Disposed)
                    return;

                lock (lockRenderLoops)
                {
                    if (rendererFrame)
                        Frames.SetRendererFrame(null);

                    hasPending = false;
                    surface?.ClearFrame();
                }
            }
        }
        else if (Config.Video.ClearScreen)
            RenderRequest(null, true);
    }

    /// <summary>Bitmap subtitles are drawn by the host UI; nothing is composited into the video frame.</summary>
    internal void SubsDispose() { }

    /// <summary>Bitmap subtitles are drawn by the host UI; nothing is composited into the video frame.</summary>
    internal void SubsConfig(int width, int height) { }
    #endregion
}
