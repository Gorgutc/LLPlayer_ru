namespace FlyleafLib;

/// <summary>
/// Field order of a video frame (numeric values match D3D11_VIDEO_FRAME_FORMAT / Vortice.Direct3D11.VideoFrameFormat,
/// which the Windows build uses; <see cref="DeInterlace"/> relies on the same values).
/// </summary>
public enum VideoFrameFormat
{
    Progressive                 = 0,
    InterlacedTopFieldFirst     = 1,
    InterlacedBottomFieldFirst  = 2
}
