#nullable enable
using System.Linq;

namespace FlyleafLib.MediaFramework.MediaDevice;

/// <summary>Structured FFmpeg discovery. No global logging callback, helper process or format query.</summary>
internal sealed class FFmpegCaptureDeviceBackend : ICaptureDeviceBackend
{
    public unsafe CaptureBackendResult Enumerate(string backend)
    {
        AVDeviceInfoList* native = null;
        try
        {
            avdevice_register_all();
            AVInputFormat* format = av_find_input_format(backend);
            if (format == null)
                return new(backend, CaptureAvailability.Unavailable, [], "The loaded FFmpeg build does not register this capture input driver.");
            int result = avdevice_list_input_sources(format, null, null, &native);
            if (result < 0)
                return new(backend, result is -13 or -1 or -38 ? CaptureAvailability.Unavailable : CaptureAvailability.Failed,
                    [], $"Device discovery returned {result}: {FFmpegEngine.ErrorCodeToMsg(result)}");
            if (native == null || native->nb_devices < 0 || (native->nb_devices != 0 && native->devices == null))
                return new(backend, CaptureAvailability.Failed, [], "The native device driver returned an invalid list.");

            List<CaptureBackendDevice> devices = [];
            bool untyped = false;
            for (int i = 0; i < native->nb_devices; i++)
            {
                AVDeviceInfo* item = native->devices[i];
                if (item == null || item->device_name == null || item->nb_media_types < 0 ||
                    (item->nb_media_types != 0 && item->media_types == null))
                    return new(backend, CaptureAvailability.Failed, [], "The native device driver returned an invalid entry.");
                string id = BytePtrToStringUTF8(item->device_name);
                if (string.IsNullOrEmpty(id))
                    continue;
                List<CaptureDeviceKind> kinds = [];
                for (int j = 0; j < item->nb_media_types; j++)
                {
                    if (item->media_types[j] == AVMediaType.Video && !kinds.Contains(CaptureDeviceKind.Video))
                        kinds.Add(CaptureDeviceKind.Video);
                    if (item->media_types[j] == AVMediaType.Audio && !kinds.Contains(CaptureDeviceKind.Audio))
                        kinds.Add(CaptureDeviceKind.Audio);
                }
                untyped |= kinds.Count == 0;
                devices.Add(new(id, BytePtrToStringUTF8(item->device_description) ?? id, Array.AsReadOnly(kinds.ToArray())));
            }

            string? reason = null;
            var availability = CaptureAvailability.Available;
            if (OperatingSystem.IsLinux() && backend is "v4l2" or "video4linux2")
            {
                reason = LinuxCaptureFormats.CheckDiscoveryAccess(devices.Count);
                if (devices.Count == 0 && reason != null)
                    availability = CaptureAvailability.Unavailable;
            }
            else if (backend == "dshow" && untyped)
            {
                reason = "Some DirectShow inputs expose no usable media type and cannot safely be classified as audio or video.";
                if (devices.All(d => d.Kinds.Count == 0))
                    availability = CaptureAvailability.Unavailable;
            }
            return new(backend, availability, Array.AsReadOnly(devices.ToArray()), reason);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException)
        {
            return new(backend, CaptureAvailability.Unavailable, [], "The loaded FFmpeg avdevice API is unavailable: " + ex.Message);
        }
        finally
        {
            if (native != null)
                avdevice_free_list_devices(&native);
        }
    }
}
