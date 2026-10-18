namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// Present statistics of the portable renderer (subset of DXGI_FRAME_STATISTICS used by the player).
/// </summary>
public struct FrameStatistics
{
    /// <summary>Number of frames presented to the <see cref="IVideoSurface"/> since the surface was attached.</summary>
    public uint PresentCount;
}

/// <summary>
/// F-13 portable stand-in for the Direct3D11 swap chain (MediaRenderer/SwapChain.cs). It represents the attached
/// <see cref="IVideoSurface"/>: <see cref="CanPresent"/> is true while a surface is attached.
/// </summary>
public class SwapChain
{
    readonly Renderer renderer;
    internal uint presentCount;

    internal SwapChain(Renderer renderer) => this.renderer = renderer;

    /// <summary>True while no <see cref="IVideoSurface"/> is attached to the renderer.</summary>
    public bool Disposed    => renderer.Surface == null;

    /// <summary>True while an <see cref="IVideoSurface"/> is attached and the renderer is alive.</summary>
    public bool CanPresent  => renderer.Surface != null && !renderer.Disposed;

    public FrameStatistics GetFrameStatistics() => new() { PresentCount = presentCount };
}
