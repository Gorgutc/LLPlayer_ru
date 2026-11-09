#nullable enable
namespace FlyleafLib.MediaFramework.MediaRenderer;

internal unsafe delegate int PortableHardwareDeviceFactory(AVHWDeviceType type, string? selector, AVBufferRef** device);

internal sealed record PortableHardwareRequest(PortableVideoDecodeMode Mode, string Device, bool Allowance);

public unsafe partial class Renderer
{
    internal AVBufferRef* ffDevice;
    internal AVBufferRef* ffFrames;
    internal HWFramesInfo ffFramesInfo = new();
    internal HWTextureDesc ffTextureDesc = new();
    internal struct HWFramesInfo { public AVCodecID CodecId; }
    internal struct HWTextureDesc { public uint Width, Height; }

    internal AVPixelFormat HardwarePixelFormat { get; private set; } = AVPixelFormat.None;
    internal AVHWDeviceType HardwareDeviceType { get; private set; } = AVHWDeviceType.None;
    internal Func<IReadOnlyList<PortableVideoAdapterInfo>> HardwareAdapterCatalog = PortableVideoHardwareCatalog.EnumerateAdapters;
    internal PortableHardwareDeviceFactory HardwareDeviceFactory = CreateHardwareDevice;

    PortableVideoDecodeMode preparedHardwareMode;
    string? preparedHardwareDevice;
    string? hardwareFailureReason;
    PortableHardwareRequest? hardwareRequestAtOpen;
    PortableVideoDecodeStatus decodeStatus = new(PortableVideoDecodeMode.Software, "",
        PortableVideoDecodeMode.Software, null, false, false, null);

    /// <summary>Immutable cached state. This getter never enumerates adapters or invokes a native API.</summary>
    public PortableVideoDecodeStatus DecodeStatus => Volatile.Read(ref decodeStatus);

    /// <summary>Publishes saved preferences without allocating, closing or reopening a native decoder.</summary>
    public void UpdateDecodeRequest()
    {
        var request = CurrentHardwareRequest();
        var opened = Volatile.Read(ref hardwareRequestAtOpen);
        UpdateDecodeStatus(state => state with
        {
            RequestedMode = request.Mode, RequestedDevice = request.Device,
            IsPending = opened is null ? request.Mode != PortableVideoDecodeMode.Software : request != opened
        });
    }

    PortableHardwareRequest CurrentHardwareRequest()
        => new(Config.Video.PortableDecodeMode, Config.Video.PortableHardwareDevice ?? "", Config.Video.VideoAcceleration);

    void UpdateDecodeStatus(Func<PortableVideoDecodeStatus, PortableVideoDecodeStatus> update)
    {
        PortableVideoDecodeStatus previous, next;
        do
        {
            previous = Volatile.Read(ref decodeStatus);
            next = update(previous);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref decodeStatus, next, previous), previous));
    }

    // Actual decoder Setup owns this call. Renderer construction and settings application never call it.
    internal void PrepareHardwareDecoding()
    {
        var request = CurrentHardwareRequest();
        Volatile.Write(ref hardwareRequestAtOpen, request);
        ReleasePortableHardwareResources();
        hardwareFailureReason = null;
        UpdateDecodeStatus(_ => new(request.Mode, request.Device, PortableVideoDecodeMode.Software,
            null, false, false, null) { IsPending = false });

        AVBufferRef* created = null;
        try
        {
            var adapters = request.Mode != PortableVideoDecodeMode.Software && request.Allowance
                ? HardwareAdapterCatalog() : Array.Empty<PortableVideoAdapterInfo>();
            var selection = PortableHardwareDecodePolicy.Resolve(request.Mode, request.Device, request.Allowance,
                OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), adapters);
            if (selection.Mode == PortableVideoDecodeMode.Software)
            {
                if (selection.FallbackReason is not null)
                    MarkHardwareFallback(selection.FallbackReason);
                return;
            }

            HardwareDeviceType = selection.Mode == PortableVideoDecodeMode.D3D11VA ? AVHWDeviceType.D3d11va : AVHWDeviceType.Vaapi;
            HardwarePixelFormat = selection.Mode == PortableVideoDecodeMode.D3D11VA ? AVPixelFormat.D3d11 : AVPixelFormat.Vaapi;
            int result = HardwareDeviceFactory(HardwareDeviceType, selection.DeviceSelector, &created);
            if (result < 0 || created == null)
            {
                if (created != null) av_buffer_unref(&created);
                MarkHardwareFallback($"Hardware device creation failed ({result}).");
                HardwareDeviceType = AVHWDeviceType.None;
                HardwarePixelFormat = AVPixelFormat.None;
                return;
            }
            ffDevice = created;
            created = null;
            preparedHardwareMode = selection.Mode;
            preparedHardwareDevice = selection.EffectiveDevice;
        }
        catch (Exception error)
        {
            ReleasePortableHardwareResources();
            MarkHardwareFallback($"Hardware device initialization failed: {error.Message}");
        }
        finally
        {
            if (created != null) av_buffer_unref(&created);
            UpdateDecodeRequest();
        }
    }

    static int CreateHardwareDevice(AVHWDeviceType type, string? selector, AVBufferRef** device)
        => av_hwdevice_ctx_create(device, type, selector, null, 0);

    internal bool ConfigHWFrames(AVCodecContext* context)
    {
        AVBufferRef* frames = null;
        try
        {
            int result = avcodec_get_hw_frames_parameters(context, ffDevice, HardwarePixelFormat, &frames);
            if (result < 0 || frames == null)
            {
                MarkHardwareFallback($"Hardware frame parameters are unavailable ({result}).");
                return false;
            }
            // FFmpeg's helper already includes extra_hw_frames and active frame-thread requirements.
            result = av_hwframe_ctx_init(frames);
            if (result < 0)
            {
                MarkHardwareFallback($"Hardware frame pool initialization failed ({result}).");
                return false;
            }
            var previous = ffFrames;
            ffFrames = frames;
            frames = null;
            if (previous != null) av_buffer_unref(&previous);
            ffFramesInfo.CodecId = context->codec_id;
            return true;
        }
        catch (Exception error)
        {
            MarkHardwareFallback($"Hardware frame pool initialization failed: {error.Message}");
            return false;
        }
        finally { if (frames != null) av_buffer_unref(&frames); }
    }

    internal void MarkHardwareFallback(string reason)
    {
        hardwareFailureReason = reason;
        UpdateDecodeStatus(state => state with
        {
            EffectiveMode = PortableVideoDecodeMode.Software, EffectiveDevice = null,
            HardwareActive = false, FallbackReason = reason
        });
    }

    internal void UseSoftwareFallback()
    {
        ReleasePortableHardwareResources();
        MarkHardwareFallback(hardwareFailureReason ?? "Hardware decoding failed; using the software decoder.");
    }

    internal void HardwareCodecOpened(bool accelerated)
    {
        if (!accelerated && hardwareRequestAtOpen?.Mode != PortableVideoDecodeMode.Software && hardwareFailureReason is null)
            MarkHardwareFallback("The selected codec has no compatible hardware configuration.");
        UpdateDecodeStatus(state => state with { IsInitialized = true });
        UpdateDecodeRequest();
    }

    internal void HardwareDecoderClosing()
        => UpdateDecodeStatus(state => state with
        {
            IsInitialized = false, HardwareActive = false,
            EffectiveMode = PortableVideoDecodeMode.Software, EffectiveDevice = null
        });

    internal bool TransferHardwareFrame(AVFrame* frame)
    {
        var descriptor = av_pix_fmt_desc_get((AVPixelFormat)frame->format);
        if (descriptor == null || (descriptor->flags & PixFmtFlags.Hwaccel) == 0)
            return true;
        if (!VideoDecoder.VideoAccelerated || (AVPixelFormat)frame->format != HardwarePixelFormat || frame->hw_frames_ctx == null)
        {
            av_frame_unref(frame);
            VideoDecoder.RequestSoftwareFallback("The decoder produced an unsupported hardware frame.");
            return false;
        }
        try
        {
            if (!PortableHardwareFrameTransfer.Download(frame, DownloadFrame, out int result))
            {
                av_frame_unref(frame);
                VideoDecoder.RequestSoftwareFallback($"Hardware frame download failed ({result}).");
                return false;
            }
            ffTextureDesc.Width = (uint)frame->width;
            ffTextureDesc.Height = (uint)frame->height;
            UpdateDecodeStatus(state => state.HardwareActive ? state : state with
            {
                EffectiveMode = preparedHardwareMode, EffectiveDevice = preparedHardwareDevice,
                IsInitialized = true, HardwareActive = true, FallbackReason = null
            });
            return true;
        }
        catch (Exception error)
        {
            av_frame_unref(frame);
            VideoDecoder.RequestSoftwareFallback($"Hardware frame download failed: {error.Message}");
            return false;
        }
    }

    static int DownloadFrame(AVFrame* destination, AVFrame* source)
        => av_hwframe_transfer_data(destination, source, 0);

    void ReleasePortableHardwareResources()
    {
        // Codec references have been drained/freed by the existing decoder owner before teardown or the next Setup.
        var frames = ffFrames;
        ffFrames = null;
        if (frames != null) av_buffer_unref(&frames);
        var device = ffDevice;
        ffDevice = null;
        if (device != null) av_buffer_unref(&device);
        HardwareDeviceType = AVHWDeviceType.None;
        HardwarePixelFormat = AVPixelFormat.None;
        preparedHardwareMode = PortableVideoDecodeMode.Software;
        preparedHardwareDevice = null;
        ffFramesInfo = new();
        ffTextureDesc = new();
    }
}
