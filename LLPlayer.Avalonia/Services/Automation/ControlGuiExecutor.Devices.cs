using FlyleafLib.MediaFramework.MediaDevice;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services.Automation;

internal sealed partial class ControlGuiExecutor
{
    ControlAudioDeviceSnapshot AudioDevices()
    {
        AudioRuntimeSnapshot audio = player.AudioSettings;
        return new(audio.Options.Enabled, audio.SelectedDeviceId, audio.DefaultDeviceId, audio.OutputDeviceId, audio.Recovering,
            audio.Devices.Select(device => new ControlAudioDevice(device.Id, device.Name)).ToArray());
    }

    ControlVideoBackendSnapshot VideoBackends()
    {
        var decode = player.GetVideoDecodeStatus();
        var presentation = player.GetVideoPresentationStatus();
        return new(decode.RequestedMode.ToString(), decode.EffectiveMode.ToString(), decode.RequestedDevice,
            decode.IsPending, decode.FallbackReason, presentation.RequestedMode.ToString(), presentation.EffectiveMode.ToString(),
            !presentation.IsInitialized || presentation.RequestedMode != presentation.EffectiveMode, presentation.FallbackReason);
    }

    async Task<ControlCaptureDevicesSnapshot> CaptureDevicesAsync(ControlCaptureDevices request, CancellationToken token)
    {
        CaptureDeviceKind kind = CaptureKind(request.Kind);
        CaptureDeviceSnapshot snapshot = request.Refresh
            ? await player.RefreshCaptureDevicesAsync(kind, token) : player.GetCaptureDevices(kind);
        return new(snapshot.Epoch, snapshot.Availability.ToString(), snapshot.Reason,
            snapshot.Devices.Select(device => new ControlCaptureDevice(device.Id, device.Label, device.Backend)).ToArray());
    }

    async Task<ControlCaptureFormatsSnapshot> CaptureFormatsAsync(ControlCaptureFormats request, CancellationToken token)
    {
        CaptureDeviceInfo device = GetCaptureDevice(CaptureDeviceKind.Video, request.DeviceId, request.Epoch);
        CaptureVideoFormatSnapshot formats = await player.GetCaptureFormatsAsync(device, request.Epoch, token);
        return new(formats.CatalogEpoch, formats.DeviceId, formats.Availability.ToString(), formats.Reason, formats.Formats.Select(format =>
            new ControlCaptureFormat(format.Id, format.Width, format.Height,
                $"{format.FrameRateNumerator}/{format.FrameRateDenominator} fps · {format.Codec} {format.PixelFormat}".TrimEnd())).ToArray());
    }

    async Task OpenCaptureAsync(ControlCaptureOpen request, CancellationToken token)
    {
        CaptureDeviceKind kind = CaptureKind(request.Kind);
        CaptureDeviceInfo device = GetCaptureDevice(kind, request.DeviceId, request.Epoch);
        CaptureVideoFormatInfo? format = null;
        if (request.FormatId is { } formatId)
        {
            if (kind != CaptureDeviceKind.Video) throw Invalid("Audio capture does not take a video format.");
            CaptureVideoFormatSnapshot formats = await player.GetCaptureFormatsAsync(device, request.Epoch, token);
            format = formats.Formats.FirstOrDefault(candidate => candidate.Id == formatId)
                ?? throw new ControlException(new("stale_revision", "The selected capture format is no longer available."));
        }
        await player.OpenCaptureDeviceAsync(device, request.Epoch, format, token);
    }

    CaptureDeviceInfo GetCaptureDevice(CaptureDeviceKind kind, string id, long epoch)
    {
        CaptureDeviceSnapshot snapshot = player.GetCaptureDevices(kind);
        return snapshot.Epoch == epoch ? snapshot.Devices.FirstOrDefault(device => device.Id == id)
            ?? throw new ControlException(new("stale_revision", "Refresh the capture catalog and select a current device."))
            : throw new ControlException(new("stale_revision", "Refresh the capture catalog before using a device ID."));
    }

    static CaptureDeviceKind CaptureKind(ControlCaptureKind kind) => kind switch
    {
        ControlCaptureKind.Audio => CaptureDeviceKind.Audio,
        ControlCaptureKind.Video => CaptureDeviceKind.Video,
        _ => throw Invalid("The capture kind is unknown.")
    };
}
