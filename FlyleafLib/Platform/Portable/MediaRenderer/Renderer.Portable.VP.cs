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

    /// <summary>
    /// Field order the software deinterlacer (bwdif) works with: <see cref="VideoConfig.DeInterlace"/> (Auto = the
    /// stream's field order), Progressive = no deinterlacing. With <see cref="VideoConfig.DoubleRate"/> the player
    /// presents both fields of every frame (as with the Windows D3D11 video processor).
    /// </summary>
    public VideoFrameFormat FieldType       { get; private set; } = VideoFrameFormat.Progressive;

    VideoStream     scfg;

    CropRect        crop;
    uint            rotation;
    VideoTransform  transform = VideoTransform.Identity;

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

        // New stream / format: drop filter graphs of the previous one, colour filters follow the new colour type
        lock (lockRenderLoops)
            preprocessor.Reset();

        UpdateColorFilter();

        // Software path: the requests need no device/surface, so apply them now (visible size, DAR, viewport are
        // then valid even while no surface is attached, e.g. headless or before the host control is created).
        vpRequestsIn |= vpRequests | VPRequestType.Deinterlace;
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

            if (vpRequests.HasFlag(VPRequestType.Deinterlace) && scfg != null)
                SetFieldType();

            // HDRtoSDR / UpdatePS: read by the next conversion (the render request re-renders the current frame)
            // UpdateVS: shader-only request, not applicable to the software path
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
        transform       = VideoTransform.From(rotation, ucfg.hflip, ucfg.vflip);

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
    void SetFieldType()
    {
        var fieldType = ucfg.DeInterlace == DeInterlace.Auto ? scfg.FieldOrder : (VideoFrameFormat)ucfg.DeInterlace;

        if (fieldType != VideoFrameFormat.Progressive && string.IsNullOrEmpty(SoftwareFramePreprocessor.DeinterlaceFilter))
        {
            Log.Warn("Interlaced video but this FFmpeg build has no deinterlace filter (bwdif / yadif)");
            fieldType = VideoFrameFormat.Progressive;
        }

        vpRequests &= ~VPRequestType.Deinterlace;

        if (fieldType == FieldType)
            return;

        if (fieldType == VideoFrameFormat.Progressive)
            preprocessor.ResetDeinterlace();

        FieldType = fieldType;
        RaiseUI(nameof(FieldType));
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

        UpdateColorFilter();
    }

    /// <summary>
    /// Called when a Flyleaf filter value changes (any thread). Rebuilds the software colour filter (applied by the
    /// conversion, see <see cref="SoftwareColorFilter"/>) and re-renders the current frame.
    /// </summary>
    internal void FLSetFilter(FLFilter cfgFilter, bool request = false)
    {
        UpdateColorFilter();

        if (request)
            VPRequest(VPRequestType.UpdatePS);
    }

    void UpdateColorFilter()
    {   // Any thread (UI: filter value, decoder: new stream); serialized so the last update wins with the latest values
        lock (ucfg.lockFLFilters)
        {
            float brightness = 0, contrast = 1, hue = 0, saturation = 1;
            bool hasFilters = false;

            foreach (var filter in ucfg.FLFilters.Values)
            {
                if (filter.Value == filter.Default)
                    continue;

                hasFilters = true;
                float value = Scale(filter.Value, filter.Minimum, filter.Maximum, filter.MinimumPS, filter.MaximumPS);
                switch (filter.Filter)
                {
                    case FLFilters.Brightness:  brightness  = value; break;
                    case FLFilters.Contrast:    contrast    = value; break;
                    case FLFilters.Hue:         hue         = value; break;
                    case FLFilters.Saturation:  saturation  = value; break;
                }
            }

            ucfg.hasFLFilters = hasFilters;

            var stream  = scfg;
            colorFilter = hasFilters
                ? SoftwareColorFilter.Create(brightness, contrast, hue, saturation,
                    stream?.ColorType ?? ColorType.YUV, stream?.ColorSpace ?? ColorSpace.None, stream?.ColorRange ?? ColorRange.Limited)
                : null;
        }
    }
    #endregion
}
