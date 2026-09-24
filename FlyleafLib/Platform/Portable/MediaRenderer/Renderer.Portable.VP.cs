using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaFramework.MediaRenderer;

// F-13 portable video-processing state (viewport / aspect ratio / crop / rotation). Mirrors the math of the Windows
// Renderer.VP.cs + Renderer.VP.FL.cs (FLSetCrop / FLProcessRequests) without Direct3D11.
public unsafe partial class Renderer
{
    public event EventHandler ViewportChanged;

    /// <summary>Always <see cref="VideoProcessors.SwsScale"/> once a stream is configured (software conversion).</summary>
    public VideoProcessors  VideoProcessor  { get; private set; } = VideoProcessors.Auto; // Ensures we catch the 'change' initially

    /// <summary>Rectangle (host-control pixels) the host draws the video frame into; may exceed the control when zoomed/panned.</summary>
    public Viewport         Viewport        { get; private set; }
    public int              ControlWidth    { get; private set; }
    public int              ControlHeight   { get; private set; }

    /// <summary>Frame size after crop (the size of the frames passed to <see cref="IVideoSurface.PresentFrame"/>).</summary>
    public uint             VisibleWidth    { get; private set; }
    public uint             VisibleHeight   { get; private set; }
    public AspectRatio      DAR             { get; private set; }

    /// <summary>No software deinterlacing: always progressive (DoubleRate is never used).</summary>
    public VideoFrameFormat FieldType       { get; private set; } = VideoFrameFormat.Progressive;

    VideoStream     scfg;

    CropRect        crop;
    uint            rotation;

    public int      SideXPixels     => sideXPixels;
    public int      SideYPixels     => sideYPixels;
    int             sideXPixels, sideYPixels;
    internal double curRatio, keepRatio, fillRatio;

    VPRequestType   vpRequestsIn, vpRequests; // In: From User | ProcessRequests Copy

    void IVP.VPRequest(VPRequestType request)
        => VPRequest(request);
    internal void VPRequest(VPRequestType request)
    {
        vpRequestsIn |= request;

        if (!SwapChain.CanPresent)
        {   // No render loop without a surface: apply now so Viewport/visible size stay current (headless / before attach)
            lock (lockRenderLoops)
                ProcessRequests();

            return;
        }

        RenderRequest();
    }

    internal bool VPConfig(VideoStream videoStream, AVFrame* frame)
    {
        lock (lockRenderLoops)
            Frames.SetRendererFrame(null);

        scfg = videoStream;
        VPConfigHelper();

        return true;
    }

    /// <summary>The portable renderer has a single (software) video processor: nothing to switch.</summary>
    internal void VPSwitchCheck() { }

    void VPConfigHelper()
    {
        vpRequestsIn   &= ~VPRequestType.ReConfigVP;
        var oldVP       = VideoProcessor;
        var vpRequests  = VPRequestType.RotationFlip | VPRequestType.Crop | VPRequestType.Resize | VPRequestType.AspectRatio;
        VideoProcessor  = VideoProcessors.SwsScale;

        if (VideoProcessor != oldVP)
            RaiseUI(nameof(VideoProcessor));

        if (CanDebug) Log.Debug($"Prepared {scfg.PixelFormatStr} for software conversion (BGRA)");

        // Software path: the requests need no device/surface, so apply them now (visible size, DAR, viewport are
        // then valid even while no surface is attached, e.g. headless or before the host control is created).
        vpRequestsIn |= vpRequests;
        lock (lockRenderLoops)
            ProcessRequests();

        if (player != null)
            RenderRequest();
    }

    void ProcessRequests()
    {   // lockRenderLoops
        while (vpRequestsIn != VPRequestType.Empty)
        {
            vpRequests  = vpRequestsIn;
            vpRequestsIn= VPRequestType.Empty;

            if (vpRequests.HasFlag(VPRequestType.BackColor))
                SetBackColor();

            if (scfg != null)
            {
                if (vpRequests.HasFlag(VPRequestType.RotationFlip))
                    SetRotation();

                if (vpRequests.HasFlag(VPRequestType.Crop))
                    SetCrop();
            }

            if (vpRequests.HasFlag(VPRequestType.Resize))
                SetSize();

            if (vpRequests.HasFlag(VPRequestType.AspectRatio))
                SetAspectRatio();

            if (vpRequests.HasFlag(VPRequestType.Viewport))
                SetViewport(ControlWidth, ControlHeight);

            // Deinterlace / HDRtoSDR / UpdatePS / UpdateVS: shader-only requests, not applicable to the software path
        }
    }

    void IVP.MonitorChanged(GPUOutput monitor)
    {
        ucfg.MaxVerticalResolutionAuto  = monitor.Height;
        ucfg.SDRDisplayNitsAuto         = monitor.MaxLuminance;
    }
    void IVP.UpdateSize(int width, int height)
    {
        ControlWidth    = width;
        ControlHeight   = height;
    }
    void SetSize()
    {
        fillRatio = ControlHeight == 0 ? 0 : ControlWidth / (double)ControlHeight;
        if (ucfg.AspectRatio == AspectRatio.Fill)
            curRatio = fillRatio;

        vpRequests &= ~VPRequestType.Resize;
        vpRequests |=  VPRequestType.Viewport;
    }
    void SetViewport(int width, int height)
    {
        int x, y, newWidth, newHeight, xZoomPixels, yZoomPixels;

        var shouldFill = player?.Host?.Player_HandlesRatioResize(width, height);

        if (curRatio < fillRatio)
        {
            newHeight   = (int)(height * ucfg.zoom);
            newWidth    = (shouldFill.HasValue && shouldFill.Value) ? (int)(width * ucfg.zoom) : (int)(newHeight * curRatio);

            sideXPixels = ((int) (width - (height * curRatio))) & ~1;
            sideYPixels = 0;

            y = (int)(height * ucfg.panYOffset);
            x = (int)(width  * ucfg.panXOffset) + (sideXPixels / 2);

            yZoomPixels = newHeight - height;
            xZoomPixels = newWidth - (width - sideXPixels);
        }
        else
        {
            newWidth    = (int)(width * ucfg.zoom);
            newHeight   = (shouldFill.HasValue && shouldFill.Value) || curRatio == fillRatio || curRatio == 0 ? (int)(height * ucfg.zoom) : (int)(newWidth / curRatio);

            sideYPixels = curRatio == 0 ? 0 : ((int) (height - (width / curRatio))) & ~1;
            sideXPixels = 0;

            x = (int)(width  * ucfg.panXOffset);
            y = (int)(height * ucfg.panYOffset) + (sideYPixels / 2);

            xZoomPixels = newWidth - width;
            yZoomPixels = newHeight - (height - sideYPixels);
        }

        Viewport = new((int)(x - xZoomPixels * (float)ucfg.zoomCenter.X), (int)(y - yZoomPixels * (float)ucfg.zoomCenter.Y), newWidth, newHeight);
        vpRequests &= ~VPRequestType.Viewport;
        ViewportChanged?.Invoke(this, new());
    }
    void SetRotation()
    {
        bool was0_180   = rotation == 0 || rotation == 180;
        rotation        = (ucfg.rotation + scfg.Rotation) % 360;
        bool is0_180    = rotation == 0 || rotation == 180;

        if (was0_180 != is0_180 && !vpRequests.HasFlag(VPRequestType.Crop)) // TBR: Crop / AspectRatio will check too
        {
            curRatio = 1 / curRatio;
            if (ucfg.AspectRatio == AspectRatio.Keep)
                player?.Host?.Player_RatioChanged(curRatio);
        }

        vpRequests &= ~VPRequestType.RotationFlip;
        vpRequests |=  VPRequestType.Viewport;
    }
    void SetAspectRatio()
    {
        bool isKeep = ucfg.AspectRatio == AspectRatio.Keep;
        if (isKeep)
            curRatio = keepRatio;
        else if (ucfg.AspectRatio == AspectRatio.Fill)
            curRatio = fillRatio;
        else
            curRatio = ucfg.AspectRatio == AspectRatio.Custom ? ucfg.AspectRatioCustom.Value : ucfg.AspectRatio.Value;

        if ((rotation == 90 || rotation == 270) && curRatio != 0)
            curRatio = 1 / curRatio;

        if (isKeep && curRatio != 0)
            player?.Host?.Player_RatioChanged(curRatio);

        vpRequests &= ~VPRequestType.AspectRatio;
        vpRequests |=  VPRequestType.Viewport;
    }
    void SetBackColor()
    {   // The host paints Config.Video.BackColor around the viewport / on ClearFrame
        vpRequests &= ~VPRequestType.BackColor;
        vpRequests |=  VPRequestType.Viewport;
    }
    void SetCrop()
    {
        crop            = scfg.Crop + ucfg.crop;
        VisibleWidth    = scfg.txtWidth  - crop.Width;
        VisibleHeight   = scfg.txtHeight - crop.Height;

        SetVisibleSizeAndRatioHelper();

        vpRequests &= ~VPRequestType.Crop;
        vpRequests |=  VPRequestType.Viewport;
    }

    void SetVisibleSizeAndRatioHelper()
    {
        int x, y;
        _ = av_reduce(&x, &y, VisibleWidth * scfg.SAR.Num, VisibleHeight * scfg.SAR.Den, 1024 * 1024);
        DAR = new(x, y);
        keepRatio = DAR.Value;

        player?.Video.SetUISize((int)VisibleWidth, (int)VisibleHeight, DAR);

        if (ucfg.AspectRatio == AspectRatio.Keep)
        {
            curRatio = rotation == 0 || rotation == 180 ? keepRatio : 1 / keepRatio;
            player?.Host?.Player_RatioChanged(curRatio);
        }
    }

    /// <summary>Single video processor: nothing to synchronize.</summary>
    internal void SyncFilters() { }

    #region Flyleaf filters (brightness / contrast / hue / saturation)
    void FLFiltersSetup()
    {
        if (!ucfg.flFiltersFilled)
        {
            ucfg.flFiltersFilled = true;

            foreach (var filterSpec in FLFilter.FLFilterSpecs)
            {
                if (ucfg.FLFilters.TryGetValue(filterSpec.Filter, out var userFilter))
                {
                    userFilter.Initialize(this);
                    if (userFilter.Value != userFilter.Default)
                        ucfg.hasFLFilters = true;
                }
                else
                {
                    lock (ucfg.lockFLFilters)
                        ucfg.FLFilters.Add(filterSpec.Filter, new(this, filterSpec));
                }
            }
        }
        else
        {
            ucfg.hasFLFilters = false;
            foreach (var userFilter in ucfg.FLFilters.Values)
            {
                userFilter.Initialize(this);
                if (userFilter.Value != userFilter.Default)
                    ucfg.hasFLFilters = true;
            }
        }
    }

    /// <summary>
    /// Called when a Flyleaf filter value changes. The software conversion does not apply color filters yet
    /// (TODO(F-13 video): map to swscale colorspace details or an FFmpeg filter graph); the value is kept in config.
    /// </summary>
    internal void FLSetFilter(FLFilter cfgFilter, bool request = false)
    {
        bool hasFilters = false;
        foreach (var filter in ucfg.FLFilters.Values)
            if (filter.Value != filter.Default)
                { hasFilters = true; break; }

        ucfg.hasFLFilters = hasFilters;

        if (request)
            VPRequest(VPRequestType.UpdatePS);
    }
    #endregion
}
