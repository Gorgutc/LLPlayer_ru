using FlyleafLib;
using FlyleafLib.MediaFramework.MediaDevice;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    sealed record CaptureFormatsLease(CaptureDeviceInfo Device, CaptureVideoFormatSnapshot Snapshot);
    sealed record CaptureOpenChoice(string Url);
    sealed class ActiveCaptureOpen { public long InterruptRevision { get; set; } }

    readonly object captureFormatsGate = new();
    readonly Dictionary<string, CaptureFormatsLease> captureFormats = new(StringComparer.Ordinal);
    IReadOnlyList<PortableVideoAdapterInfo> videoAdapters = Array.Empty<PortableVideoAdapterInfo>();
    Func<PortableVideoPresentationStatus>? getPresentationStatus;
    Action<PortableVideoPresentationMode>? applyPresentationMode;
    ActiveCaptureOpen? activeCaptureOpen;
    int presentationPublication;

    public bool IsRecording => player.IsRecording;

    // Keyboard, palette and menu execution share the persisted host owner for this one legacy engine action.
    public Action? ResolveEngineAction(KeyBindingAction action) => action == KeyBindingAction.ToggleVideoAcceleration
        ? () => Observe(ToggleVideoAccelerationAsync()) : player.Config.Player.KeyBindings.GetKeyBindingAction(action);

    public Task ToggleVideoAccelerationAsync() => Recognition.MutateAsync(token =>
    {
        CheckPlaybackSettingsAdmission(token);
        // Read after queue admission: each toggle sees the prior successfully persisted request.
        PlaybackSettings candidate = GetPlaybackSettings();
        candidate.VideoHardwareAccelerationEnabled = !candidate.VideoHardwareAccelerationEnabled;
        PersistPlaybackSettings(candidate);
        player.Config.Video.PortableDecodeMode = candidate.VideoHardwareAccelerationEnabled
            ? candidate.VideoHardwareDecodeMode : PortableVideoDecodeMode.Software;
        player.Config.Video.PortableHardwareDevice = candidate.VideoHardwareDevice;
        if (candidate.VideoHardwareAccelerationEnabled) player.Config.Video.VideoAcceleration = true;
        PublishVideoBackendPreferences(candidate);
    });

    public PortableVideoDecodeStatus GetVideoDecodeStatus() => player.Renderer.DecodeStatus;

    public PortableVideoPresentationStatus GetVideoPresentationStatus() => getPresentationStatus?.Invoke()
        ?? new(savedPlaybackSettings?.VideoPresentationMode ?? PortableVideoPresentationMode.Software,
            PortableVideoPresentationMode.Software, false, false, null);

    public void ConfigureVideoPresentation(Func<PortableVideoPresentationStatus> getStatus, Action<PortableVideoPresentationMode> applyMode)
    {
        ArgumentNullException.ThrowIfNull(getStatus);
        ArgumentNullException.ThrowIfNull(applyMode);
        getPresentationStatus = getStatus;
        applyPresentationMode = applyMode;
        PublishVideoBackendPreferences(savedPlaybackSettings ?? GetPlaybackSettings());
    }

    void PublishVideoBackendPreferences(PlaybackSettings settings)
    {
        // Request/pending state only. Decoder Setup owns native hardware allocation.
        player.Renderer.UpdateDecodeRequest();
        PortableVideoPresentationMode mode = settings.VideoPresentationMode;
        int publication = Interlocked.Increment(ref presentationPublication);
        Post(() =>
        {
            if (disposed || publication != Volatile.Read(ref presentationPublication)) return;
            try { applyPresentationMode?.Invoke(mode); }
            catch (Exception error) { ErrorOccurred?.Invoke(error.Message); }
            Raise("VideoBackendPreferences");
        });
    }

    public IReadOnlyList<PortableVideoAdapterInfo> GetVideoAdapters() => Volatile.Read(ref videoAdapters);

    public async Task<IReadOnlyList<PortableVideoAdapterInfo>> RefreshVideoAdaptersAsync()
    {
        IReadOnlyList<PortableVideoAdapterInfo>? result = null;
        await Recognition.MutateAsync(token =>
        {
            CheckAudioAdmission(token);
            var adapters = Array.AsReadOnly(PortableVideoHardwareCatalog.EnumerateAdapters().ToArray());
            CheckAudioAdmission(token);
            Volatile.Write(ref videoAdapters, adapters);
            result = adapters;
            Raise("VideoAdapters");
        }).ConfigureAwait(false);
        return result!;
    }

    public CaptureDeviceSnapshot GetCaptureDevices(CaptureDeviceKind kind) => kind switch
    {
        CaptureDeviceKind.Video => Engine.Video.CaptureSnapshot,
        CaptureDeviceKind.Audio => Engine.Audio.CaptureSnapshot,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public async Task<CaptureDeviceSnapshot> RefreshCaptureDevicesAsync(CaptureDeviceKind kind, CancellationToken cancellationToken = default)
    {
        _ = GetCaptureDevices(kind);
        CaptureDeviceSnapshot? result = null;
        cancellationToken.ThrowIfCancellationRequested();
        await Recognition.MutateAsync(queueToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(queueToken, cancellationToken);
            CheckAudioAdmission(linked.Token);
            if (kind == CaptureDeviceKind.Video) Engine.Video.RefreshCapDevices(linked.Token);
            else Engine.Audio.RefreshCapDevices(linked.Token);
            CheckAudioAdmission(linked.Token);
            lock (captureFormatsGate)
                if (kind == CaptureDeviceKind.Video) captureFormats.Clear();
            result = GetCaptureDevices(kind);
            Raise("CaptureDevices");
        }).ConfigureAwait(false);
        return result!;
    }

    public async Task<CaptureVideoFormatSnapshot> GetCaptureFormatsAsync(CaptureDeviceInfo device, long catalogEpoch,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaptureDeviceInfo canonical = ValidateCaptureDevice(device, catalogEpoch);
        if (canonical.Kind != CaptureDeviceKind.Video)
            throw new InvalidOperationException("Only video inputs have selectable video formats.");
        CaptureVideoFormatSnapshot? result = null;
        await Recognition.MutateAsync(queueToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(queueToken, cancellationToken);
            CheckAudioAdmission(linked.Token);
            canonical = ValidateCaptureDevice(device, catalogEpoch);
            CaptureVideoFormatSnapshot queried = Engine.Video.GetCaptureFormats(canonical, linked.Token);
            CheckAudioAdmission(linked.Token);
            _ = ValidateCaptureDevice(canonical, catalogEpoch);
            if (queried.CatalogEpoch != catalogEpoch || queried.DeviceId != canonical.Id)
                throw new InvalidOperationException("The capture catalog changed. Refresh devices and load formats again.");
            result = queried with { Formats = Array.AsReadOnly(queried.Formats.ToArray()) };
            lock (captureFormatsGate) captureFormats[canonical.Id] = new(canonical, result);
        }).ConfigureAwait(false);
        return result!;
    }

    CaptureDeviceInfo ValidateCaptureDevice(CaptureDeviceInfo device, long catalogEpoch)
    {
        ArgumentNullException.ThrowIfNull(device);
        ObjectDisposedException.ThrowIf(disposed || player.IsDisposed, this);
        CaptureDeviceSnapshot current = GetCaptureDevices(device.Kind);
        if (current.Epoch != catalogEpoch || current.Availability != CaptureAvailability.Available)
            throw new InvalidOperationException("The capture catalog changed or is unavailable. Refresh devices and select a current input.");
        // Full record equality rejects retained IDs with replaced native input/options.
        return current.Devices.FirstOrDefault(candidate => candidate == device)
            ?? throw new InvalidOperationException("This capture input is stale. Refresh devices and select a current input.");
    }

    CaptureOpenChoice ValidateCaptureOpen(CaptureDeviceInfo device, long catalogEpoch, CaptureVideoFormatInfo? format)
    {
        CaptureDeviceInfo canonical = ValidateCaptureDevice(device, catalogEpoch);
        CaptureVideoFormatInfo? canonicalFormat = null;
        if (format != null)
        {
            lock (captureFormatsGate)
            {
                if (canonical.Kind != CaptureDeviceKind.Video || !captureFormats.TryGetValue(canonical.Id, out var formats)
                    || formats.Device != canonical || formats.Snapshot.CatalogEpoch != catalogEpoch
                    || formats.Snapshot.Availability != CaptureAvailability.Available)
                    throw new InvalidOperationException("Load the current input's formats before choosing a specific format.");
                canonicalFormat = formats.Snapshot.Formats.FirstOrDefault(candidate => candidate == format)
                    ?? throw new InvalidOperationException("This format is stale. Load the current input's formats again.");
            }
        }
        // Canonical DTOs and every URL field are validated before translation/media state changes.
        return new(CaptureInputUrl.Build(canonical, canonicalFormat));
    }

    public async Task OpenCaptureDeviceAsync(CaptureDeviceInfo device, long catalogEpoch, CaptureVideoFormatInfo? format = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = ValidateCaptureOpen(device, catalogEpoch, format);
        // A stale queued choice cannot invalidate recognition, suspend translators, or advance media identity.
        // Valid capture waits for existing queue work rather than canceling its own admission.
        await Recognition.MutateAsync(queueToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(queueToken, cancellationToken);
            CancellationToken token = linked.Token;
            CheckAudioAdmission(token);
            CaptureOpenChoice choice = ValidateCaptureOpen(device, catalogEpoch, format);
            token.ThrowIfCancellationRequested();
            SuspendTranslations();
            int epoch = 0, previous = appMediaOpenEpoch.Value;
            ActiveCaptureOpen active = new();
            CancellationTokenRegistration interruption = default;
            try
            {
                lock (mediaOpenInterruption)
                {
                    token.ThrowIfCancellationRequested();
                    cancelableAppEpoch = interruptibleAppEpoch = epoch = Interlocked.Increment(ref mediaOpenEpoch);
                    activeCaptureOpen = active;
                }
                appMediaOpenEpoch.Value = epoch;
                Raise(nameof(CanCancelOpen));
                QueuePlaybackChoices();
                int openingEpoch = epoch;
                Post(() => { if (!disposed && openingEpoch == Volatile.Read(ref mediaOpenEpoch)) ExternalMediaOpening?.Invoke(); });
                interruption = token.Register(() =>
                {
                    lock (mediaOpenInterruption)
                        if (ReferenceEquals(activeCaptureOpen, active) && interruptibleAppEpoch == openingEpoch && !player.IsDisposed)
                            active.InterruptRevision = player.decoder.RequestOwnedInterrupt();
                });
                token.ThrowIfCancellationRequested();
                string? error = player.Open(choice.Url).Error;
                token.ThrowIfCancellationRequested();
                if (error != null) throw new InvalidOperationException(error);
            }
            finally
            {
                lock (mediaOpenInterruption)
                {
                    if (ReferenceEquals(activeCaptureOpen, active))
                    {
                        if (!player.IsDisposed) player.decoder.ReleaseOwnedInterrupt(active.InterruptRevision);
                        activeCaptureOpen = null;
                    }
                    if (interruptibleAppEpoch == epoch) interruptibleAppEpoch = 0;
                    if (cancelableAppEpoch == epoch) cancelableAppEpoch = 0;
                }
                // Drain a racing cancellation callback before another queued mutation owns the decoder.
                interruption.Dispose();
                appMediaOpenEpoch.Value = previous;
                for (int slot = 0; slot < 2; slot++) { player.SubtitlesManager[slot].Translator.Resume(); RefreshOverlay(slot); }
                Raise(nameof(CanCancelOpen));
                QueuePlaybackChoices();
            }
        }).ConfigureAwait(false);
    }

    public PlayerDebugSnapshot GetDebugSnapshot()
    {
        PortableVideoDecodeStatus decode = GetVideoDecodeStatus();
        PortableVideoPresentationStatus presentation = GetVideoPresentationStatus();
        var video = player.Video;
        var audio = player.Audio;
        // Debug runs on the UI thread: use the last completed cue-watch publication, without projecting
        // a transcript on every 500 ms refresh. Match GetCues' suppression of disabled loaded-media slots.
        int primaryCount = player.CanPlay && !player.Subtitles[0].Enabled ? 0 : observedCues[0].Count;
        int secondaryCount = player.CanPlay && !player.Subtitles[1].Enabled ? 0 : observedCues[1].Count;
        return new(player.Status.ToString(), PlayerDebugSnapshot.SafeMediaTitle(Title), PlayerDebugSnapshot.SafeMediaSource(Url),
            TimeSpan.FromTicks(Math.Max(0, CurTime)).ToString(), TimeSpan.FromTicks(Math.Max(0, Duration)).ToString(), IsRecording,
            $"{video.Codec ?? "No video"}; {video.Width}x{video.Height}; {video.FPS:0.###} fps; {video.PixelFormat}; displayed {video.FramesDisplayed}, dropped {video.FramesDropped}",
            $"{audio.Codec ?? "No audio"}; {audio.SampleRate} Hz; {audio.Channels} channels; {AudioEngine.Backend.Name}; volume {Volume}; mute {Mute}",
            $"Primary: {GetSubtitleLanguage(0) ?? "unknown"}, {primaryCount} cues; Secondary: {GetSubtitleLanguage(1) ?? "unknown"}, {secondaryCount} cues",
            $"Requested: {presentation.RequestedMode}; active: {presentation.EffectiveMode}; initialized: {presentation.IsInitialized}; planar shader: {presentation.UsesPlanarShader}. {presentation.FallbackReason} D3D-specific metrics are unavailable in the portable renderer.",
            $"Requested: {decode.RequestedMode} ({decode.RequestedDevice}); active: {decode.EffectiveMode}; initialized: {decode.IsInitialized}; hardware active: {decode.HardwareActive}. "
                + (decode.IsPending ? "Pending next decoder open. " : "") + decode.FallbackReason);
    }

    public void ZoomVideoAt(bool zoomIn, double deviceX, double deviceY)
    {
        if (!double.IsFinite(deviceX) || !double.IsFinite(deviceY) || disposed) return;
        System.Windows.Point point = new(deviceX, deviceY);
        if (zoomIn) player.Config.Video.ZoomIn(point); else player.Config.Video.ZoomOut(point);
    }

    public (double X, double Y) GetVideoPan() => (player.Config.Video.PanXOffset, player.Config.Video.PanYOffset);

    public void SetVideoPan(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || disposed) return;
        player.Config.Video.PanXOffset = Math.Clamp(x, -10, 10);
        player.Config.Video.PanYOffset = Math.Clamp(y, -10, 10);
    }
}
