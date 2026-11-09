using Avalonia;
using Avalonia.Threading;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace LLPlayer.Avalonia.Controls;

public sealed partial class VideoView : IVideoPlanarSurface
{
    public static readonly StyledProperty<PortableVideoPresentationMode> PresentationModeProperty =
        AvaloniaProperty.Register<VideoView, PortableVideoPresentationMode>(nameof(PresentationMode), PortableVideoPresentationMode.Software);

    readonly PlanarFrameMailbox planarMailbox = new();
    PlanarGlControl? glChild;
    PortableVideoPresentationStatus presentationStatus = new(PortableVideoPresentationMode.Software, PortableVideoPresentationMode.Software, false, false, null);
    int requestedPresentation, glReady, planarVisible, attached;
    string? glFailure;
    string? driverDescription;

    public PortableVideoPresentationMode PresentationMode
    {
        get => GetValue(PresentationModeProperty);
        set => SetValue(PresentationModeProperty, value);
    }

    /// <summary>Cached snapshot. Reading diagnostics does not probe a context or driver.</summary>
    public PortableVideoPresentationStatus PresentationStatus => Volatile.Read(ref presentationStatus);
    /// <summary>Raw GL vendor/renderer/version, read only in the successful init callback. May describe a CPU driver.</summary>
    public string? DriverDescription => Volatile.Read(ref driverDescription);

    public bool CanPresentPlanar(PlanarVideoPixelFormat format) =>
        Volatile.Read(ref requestedPresentation) == (int)PortableVideoPresentationMode.OpenGL &&
        Volatile.Read(ref glReady) != 0 && Volatile.Read(ref attached) != 0 &&
        format is PlanarVideoPixelFormat.Yuv420P or PlanarVideoPixelFormat.Nv12;

    public bool TryPresentPlanar(in PlanarVideoFrame frame, in VideoPresentationParameters presentation)
    {
        if (!CanPresentPlanar(frame.Format))
            return false;
        lock (writeLock)
        {
            if (!CanPresentPlanar(frame.Format) || !planarMailbox.Publish(frame, presentation))
                return false;
            var size = PlanarTextureGeometry.VisibleSize(frame.Width, frame.Height, presentation);
            if (renderer != null)
            {
                var viewport = renderer.Viewport;
                if (viewport.Width >= 1 && viewport.Height >= 1)
                    size = new PixelSize(Math.Min(size.Width, Math.Max(1, (int)Math.Ceiling(viewport.Width))),
                        Math.Min(size.Height, Math.Max(1, (int)Math.Ceiling(viewport.Height))));
            }
            FrameSize = size;
            Volatile.Write(ref planarVisible, 1);
            Interlocked.Increment(ref framesPresented);
            Volatile.Write(ref presentationStatus, new(RequestedPresentation, PortableVideoPresentationMode.OpenGL, true, true, null));
        }
        RequestInvalidate();
        return true;
    }

    PortableVideoPresentationMode RequestedPresentation => (PortableVideoPresentationMode)Volatile.Read(ref requestedPresentation);
    internal bool ShowPlanarFrame => Volatile.Read(ref planarVisible) != 0;

    internal void OpenGlReady(string description)
    {
        Volatile.Write(ref driverDescription, description);
        Volatile.Write(ref glFailure, null);
        Volatile.Write(ref glReady, 1);
        SoftwarePresentationStatus("OpenGL is ready; waiting for a supported frame.");
        renderer?.RequestSurfaceRefresh();
    }

    internal void OpenGlFailed(string reason)
    {
        lock (writeLock)
        {
            Volatile.Write(ref glReady, 0);
            Volatile.Write(ref glFailure, reason);
            planarMailbox.Clear();
            Volatile.Write(ref planarVisible, 0);
            SoftwarePresentationStatus(reason);
        }
        RequestInvalidate();
        renderer?.RequestSurfaceRefresh();
    }

    void SoftwarePresentationStatus(string? reason = null)
    {
        bool requested = RequestedPresentation == PortableVideoPresentationMode.OpenGL;
        Volatile.Write(ref presentationStatus, new(RequestedPresentation, PortableVideoPresentationMode.Software,
            true, false, requested ? Volatile.Read(ref glFailure) ?? reason ??
                (Volatile.Read(ref glReady) == 0 ? "OpenGL context is initializing or unavailable; software presentation is active." :
                    "This frame uses software conversion because its format, HDR processing or color controls require it.") : null));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != PresentationModeProperty)
            return;
        Volatile.Write(ref requestedPresentation, (int)PresentationMode);
        planarMailbox.Clear();
        Volatile.Write(ref planarVisible, 0);
        if (PresentationMode == PortableVideoPresentationMode.OpenGL)
        {
            if (glChild == null || Volatile.Read(ref glFailure) != null)
            {
                Child = null;
                glChild = new PlanarGlControl(this, planarMailbox) { IsHitTestVisible = false };
                Child = glChild;
            }
            Volatile.Write(ref glFailure, null);
            glChild.IsVisible = true;
        }
        else if (glChild != null)
            glChild.IsVisible = false;
        SoftwarePresentationStatus();
        RequestInvalidate();
        renderer?.RequestSurfaceRefresh();
    }

    void PresentationAttached()
    {
        Volatile.Write(ref attached, 1);
        renderer?.RequestSurfaceRefresh();
    }

    void PresentationDetached()
    {
        lock (writeLock)
        {
            Volatile.Write(ref attached, 0);
            planarMailbox.Clear();
            Volatile.Write(ref planarVisible, 0);
        }
    }

    void SoftwareFrameAccepted()
    {
        planarMailbox.Clear();
        Volatile.Write(ref planarVisible, 0);
        SoftwarePresentationStatus();
    }

    void ClearPlanarFrame()
    {
        planarMailbox.Clear();
        Volatile.Write(ref planarVisible, 0);
    }

    void RequestPlanarRender()
    {
        if (glChild == null)
            return;
        if (Volatile.Read(ref glFailure) != null)
            glChild.IsVisible = false;
        else
            glChild.RequestFrame();
    }
}
