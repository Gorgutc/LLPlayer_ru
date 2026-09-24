namespace FlyleafLib.MediaFramework.MediaDevice;

/// <summary>
/// F-13 portable (Linux) counterpart of MediaDevice/VideoDeviceStream.cs, whose format enumeration uses
/// MediaFoundation. Same public properties; capture-format enumeration (e.g. v4l2) is not implemented yet, so
/// <see cref="GetVideoFormatsForVideoDevice"/> returns an empty list.
/// </summary>
public class VideoDeviceStream : DeviceStreamBase
{
    public string   MajorType           { get; }
    public string   SubType             { get; }
    public int      FrameSizeWidth      { get; }
    public int      FrameSizeHeight     { get; }
    public int      FrameRate           { get; }

    public VideoDeviceStream(string deviceName, string majorType, string subType, int frameSizeWidth, int frameSizeHeight, int frameRate, string url) : base(deviceName)
    {
        MajorType       = majorType;
        SubType         = subType;
        FrameSizeWidth  = frameSizeWidth;
        FrameSizeHeight = frameSizeHeight;
        FrameRate       = frameRate;
        Url             = url;
    }

    public override string ToString() => $"{SubType}, {FrameSizeWidth}x{FrameSizeHeight}, {FrameRate}FPS";

    public static IList<VideoDeviceStream> GetVideoFormatsForVideoDevice(string friendlyName, string symbolicLink)
        => [];
}
