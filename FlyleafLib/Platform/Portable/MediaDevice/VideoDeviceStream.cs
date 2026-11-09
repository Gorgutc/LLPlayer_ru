namespace FlyleafLib.MediaFramework.MediaDevice;

/// <summary>
/// F-13 portable (Linux) counterpart of MediaDevice/VideoDeviceStream.cs, whose format enumeration uses
/// MediaFoundation. Portable formats retain their exact native rational frame rate.
/// </summary>
public class VideoDeviceStream : DeviceStreamBase
{
    public string   MajorType           { get; }
    public string   SubType             { get; }
    public int      FrameSizeWidth      { get; }
    public int      FrameSizeHeight     { get; }
    public int      FrameRate           { get; }
    public int      FrameRateNumerator  { get; }
    public int      FrameRateDenominator { get; }

    public VideoDeviceStream(string deviceName, string majorType, string subType, int frameSizeWidth, int frameSizeHeight, int frameRate, string url) : base(deviceName)
    {
        MajorType       = majorType;
        SubType         = subType;
        FrameSizeWidth  = frameSizeWidth;
        FrameSizeHeight = frameSizeHeight;
        FrameRate       = frameRate;
        FrameRateNumerator = frameRate;
        FrameRateDenominator = 1;
        Url             = url;
    }

    public VideoDeviceStream(CaptureDeviceInfo device, CaptureVideoFormatInfo format) : base(device.Label)
    {
        MajorType = "Video";
        SubType = format.PixelFormat ?? format.Codec ?? "Native";
        FrameSizeWidth = format.Width;
        FrameSizeHeight = format.Height;
        FrameRateNumerator = format.FrameRateNumerator;
        FrameRateDenominator = format.FrameRateDenominator;
        // Integer property is retained for compatibility only. URL and label use the exact ratio.
        FrameRate = FrameRateNumerator / FrameRateDenominator;
        Url = CaptureInputUrl.Build(device, format);
    }

    public override string ToString() => $"{SubType}, {FrameSizeWidth}x{FrameSizeHeight}, {FrameRateNumerator}/{FrameRateDenominator} FPS";

    public static IList<VideoDeviceStream> GetVideoFormatsForVideoDevice(string friendlyName, string symbolicLink)
        => [];
}
