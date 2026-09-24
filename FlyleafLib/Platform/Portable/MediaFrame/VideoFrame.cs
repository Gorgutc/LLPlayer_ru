namespace FlyleafLib.MediaFramework.MediaFrame;

/// <summary>
/// F-13 portable (Linux) counterpart of MediaFrame/VideoFrame.cs (which holds Direct3D11 textures / views).
/// A software-decoded frame: <see cref="AVFrame"/> owns the decoder's frame reference (moved out of the decoder in
/// <c>Renderer.FillPlanes</c>) until the frame is disposed by the <see cref="MediaRenderer.VideoCache"/>.
/// </summary>
public unsafe class VideoFrame : FrameBase
{
    /// <summary>The decoded (software) frame; freed by <see cref="Dispose"/>.</summary>
    public AVFrame* AVFrame;

    public VideoFrame Prev, Next;
    public long Id;

    public void Dispose()
    {   // Manually dipose only when not in VC
        Prev = Next = null; // Could null Next.Prev here

        DisposeTexture();

        if (AVFrame != null)
        {
            fixed(AVFrame** ptr = &AVFrame) av_frame_free(ptr);
            AVFrame = null;
        }
    }

    /// <summary>No GPU resources on the portable build (kept for API parity with the Windows VideoFrame).</summary>
    public void DisposeTexture() { }
}
