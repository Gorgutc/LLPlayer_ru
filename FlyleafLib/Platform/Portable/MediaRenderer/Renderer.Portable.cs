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
/// <see cref="VideoFrame"/>; <c>RenderPlay</c> prepares the next frame (Renderer.Portable.Convert.cs: deinterlace, HDR to
/// SDR, crop, swscale to BGRA32 at min(native, viewport) size, rotation / mirroring, colour filters) and
/// <c>PresentPlay</c> hands it to the host's <see cref="IVideoSurface"/> at presentation time. The host only scales the
/// ready-to-show frame into <see cref="Viewport"/> (control pixels) and paints <see cref="VPConfig.BackColor"/> around it.
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

    /// <summary>
    /// Effective clockwise rotation in degrees (user + stream display matrix). Informational: the renderer already
    /// applies it, the frames passed to the surface are upright (the host must not rotate them again).
    /// </summary>
    public uint                 Rotation        => rotation;

    /// <summary>Whether the presented frames are mirrored horizontally (user). Already applied by the renderer.</summary>
    public bool                 HFlip           => ucfg.hflip;

    /// <summary>
    /// Whether the presented frames are mirrored vertically (user). Already applied by the renderer. Bottom-up coded
    /// frames (negative line size) are read upright by swscale and need no extra flip.
    /// </summary>
    public bool                 VFlip           => ucfg.vflip;
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

                if (Frames.RendererFrame == null)
                    ReleaseFrameState();

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
                ConvertFrame(frame, secondField);
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

                    ReleaseFrameState();
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
