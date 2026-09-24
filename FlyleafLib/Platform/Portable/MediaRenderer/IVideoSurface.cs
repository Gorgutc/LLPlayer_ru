namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// Video output of the portable (software) renderer, implemented by the host UI (e.g. an Avalonia control backed by a
/// WriteableBitmap). Assign it to <see cref="Renderer.Surface"/> and report the control size with
/// <see cref="Renderer.SetControlSize"/>.
/// <para>
/// Threading: frames are presented from the renderer's playback / idle-refresh threads, but both methods may also be
/// called on the thread that stops/disposes the player (often the UI thread) while renderer locks are held.
/// Implementations must return quickly and must never wait synchronously for the UI thread (no Dispatcher.Invoke):
/// copy the pixels and post the UI update. <see cref="PresentFrame"/>'s span is only valid during the call.
/// </para>
/// </summary>
public interface IVideoSurface
{
    /// <summary>
    /// Presents a decoded frame. <paramref name="bgra"/> holds <paramref name="height"/> rows of
    /// <paramref name="width"/> BGRA32 pixels (premultiplication not applied, alpha = 255 for opaque video), each row
    /// starting <paramref name="stride"/> bytes after the previous one. The frame is already cropped (stream/codec +
    /// user crop); the host scales it into <see cref="Renderer.Viewport"/> (control pixels) and fills the rest of the
    /// control with <see cref="VPConfig.BackColor"/>.
    /// </summary>
    void PresentFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride);

    /// <summary>Clears the surface to the background color (no frame to show, e.g. after stop/close).</summary>
    void ClearFrame();
}
