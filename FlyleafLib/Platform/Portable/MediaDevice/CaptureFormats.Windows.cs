#if PORTABLE_WINDOWS
#nullable enable
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.MediaFoundation;

namespace FlyleafLib.MediaFramework.MediaDevice;

/// <summary>Focused MediaFoundation query for the selected DirectShow native identity.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsCaptureFormats
{
    internal static CaptureVideoFormatSnapshot Query(CaptureDeviceInfo device, long epoch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int com = CoInitializeEx(0, 0); // MTA; an existing STA is also an initialized COM apartment.
        if (com < 0 && com != unchecked((int)0x80010106))
            Marshal.ThrowExceptionForHR(com);
        bool started = false;
        try
        {
            MediaFactory.MFStartup().CheckError();
            started = true;
            string? symbolicLink = FindSymbolicLink(device, token);
            if (symbolicLink == null)
                return CaptureFormats.Empty(device, epoch, CaptureAvailability.Unavailable,
                    "This DirectShow input has no unique matching MediaFoundation symbolic link. Use Default format.");
            token.ThrowIfCancellationRequested();
            using var attributes = MediaFactory.MFCreateAttributes(2);
            attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
            attributes.Set(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink, symbolicLink);
            using var source = MediaFactory.MFCreateDeviceSource(attributes);
            try
            {
                using var presentation = source.CreatePresentationDescriptor();
                List<CaptureVideoFormatInfo> formats = [];
                HashSet<string> ids = new(StringComparer.Ordinal);
                bool unknown = false;
                for (int streamIndex = 0; streamIndex < presentation.StreamDescriptorCount; streamIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    presentation.GetStreamDescriptorByIndex(streamIndex, out _, out var stream);
                    using (stream)
                    using (var handler = stream.MediaTypeHandler)
                    {
                        if (handler.MajorType != MediaTypeGuids.Video)
                            continue;
                        for (int typeIndex = 0; typeIndex < handler.MediaTypeCount; typeIndex++)
                        {
                            token.ThrowIfCancellationRequested();
                            using var mediaType = handler.GetMediaTypeByIndex(typeIndex);
                            Guid subtype = mediaType.GetGUID(MediaTypeAttributeKeys.Subtype);
                            if (!TryMapSubtype(subtype, out string? pixel, out string? codec))
                            { unknown = true; continue; }
                            MediaFactory.MFGetAttributeSize(mediaType, MediaTypeAttributeKeys.FrameSize, out uint width, out uint height).CheckError();
                            MediaFactory.MFGetAttributeRatio(mediaType, MediaTypeAttributeKeys.FrameRate, out uint numerator, out uint denominator).CheckError();
                            if (width == 0 || height == 0 || numerator == 0 || denominator == 0 ||
                                width > int.MaxValue || height > int.MaxValue || numerator > int.MaxValue || denominator > int.MaxValue)
                            { unknown = true; continue; }
                            var format = CaptureFormats.Create(device, (int)width, (int)height, (int)numerator, (int)denominator, pixel, codec);
                            if (ids.Add(format.Id)) formats.Add(format);
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                string? reason = unknown ? "Some MediaFoundation media types have no verified DirectShow/FFmpeg option mapping. Use Default format for them." : null;
                if (formats.Count == 0 && reason == null)
                    reason = "The selected device returned no supported discrete formats. Use Default format.";
                return new(epoch, device.Id, CaptureAvailability.Available, Array.AsReadOnly(formats.ToArray()), reason);
            }
            finally { source.Shutdown(); }
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            return CaptureFormats.Empty(device, epoch, CaptureAvailability.Unavailable,
                "Camera access was denied. Check Windows camera privacy and device permissions. " + ex.Message);
        }
        finally
        {
            if (started) MediaFactory.MFShutdown();
            if (com >= 0) CoUninitialize();
        }
    }

    private static string? FindSymbolicLink(CaptureDeviceInfo device, CancellationToken token)
    {
        const string videoPrefix = "video=";
        if (!device.Input.StartsWith(videoPrefix, StringComparison.Ordinal))
            return null;
        string expected = CaptureFormats.NormalizeDshowIdentity(device.Input[videoPrefix.Length..]);
        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
        IntPtr array = 0;
        uint count = 0;
        string? match = null;
        int matches = 0;
        try
        {
            // Vortice 3.7.6's public collection disposes its COM references but not the CoTaskMem array.
            // Its native-array overload is internal; retain explicit ownership through the same native API.
            Marshal.ThrowExceptionForHR(MFEnumDeviceSources(attributes.NativePointer, out array, out count));
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                int offset = checked(i * IntPtr.Size);
                using var candidate = new IMFActivate(Marshal.ReadIntPtr(array, offset));
                Marshal.WriteIntPtr(array, offset, 0); // this reference is now owned by the using scope
                string link = candidate.SymbolicLink;
                if (string.Equals(CaptureFormats.NormalizeDshowIdentity(link), expected, StringComparison.OrdinalIgnoreCase))
                {
                    match = link;
                    matches++;
                }
            }
        }
        finally
        {
            if (array != 0)
            {
                // Also drain references not yet inspected when cancellation or an attribute error occurred.
                for (int i = 0; i < count; i++)
                {
                    IntPtr remaining = Marshal.ReadIntPtr(array, checked(i * IntPtr.Size));
                    if (remaining != 0) Marshal.Release(remaining);
                }
                Marshal.FreeCoTaskMem(array);
            }
        }
        return matches == 1 ? match : null;
    }

    internal static bool TryMapSubtype(Guid subtype, out string? pixelFormat, out string? codec)
    {
        pixelFormat = null;
        codec = null;
        // Standard MF video FOURCC subtype GUID. Do not guess a pixel format from an unknown GUID.
        string key = subtype.ToString("D");
        if (!key.EndsWith("-0000-0010-8000-00aa00389b71", StringComparison.Ordinal))
            return false;
        switch (key[..8])
        {
            case "32595559": pixelFormat = "yuyv422"; break; // YUY2
            case "59565955": pixelFormat = "uyvy422"; break; // UYVY
            case "3231564e": pixelFormat = "nv12"; break;
            case "30323449": pixelFormat = "yuv420p"; break; // I420
            case "56555949": pixelFormat = "yuv420p"; break; // IYUV
            case "47504a4d": codec = "mjpeg"; break;
            case "34363248": codec = "h264"; break;
            case "43564548": codec = "hevc"; break;
        }
        return pixelFormat != null || codec != null;
    }

    [DllImport("mf.dll", ExactSpelling = true)]
    private static extern int MFEnumDeviceSources(nint attributes, out nint sourceActivates, out uint count);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint apartment);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
#endif
