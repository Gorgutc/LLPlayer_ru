using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace LLPlayer.Avalonia.ViewModels;

public sealed record PlaybackVideoAdapterChoice(string Id, string Name, string DeviceSelector,
    PortableVideoDecodeMode Mode, bool IsAvailable, string? UnavailableReason);

public sealed partial class PlaybackSettingsViewModel
{
    Func<PortableVideoDecodeStatus>? readDecodeStatus;
    Func<PortableVideoPresentationStatus>? readPresentationStatus;
    Func<Task<IReadOnlyList<PortableVideoAdapterInfo>>>? refreshAdapters;
    IReadOnlyList<PortableVideoAdapterInfo> adapterSnapshot = [];
    PortableVideoDecodeStatus? decodeSnapshot;
    PortableVideoPresentationStatus? presentationSnapshot;
    bool initializingVideoBackends;
    bool updatingAdapterChoices;

    public IReadOnlyList<PortableVideoDecodeMode> HardwareDecodeModes { get; } =
        [PortableVideoDecodeMode.Automatic, PortableVideoDecodeMode.D3D11VA, PortableVideoDecodeMode.VAAPI];
    public IReadOnlyList<PortableVideoPresentationMode> PresentationModes { get; } = Enum.GetValues<PortableVideoPresentationMode>();
    public ObservableCollection<PlaybackVideoAdapterChoice> VideoAdapterChoices { get; } = [];
    [ObservableProperty] bool videoHardwareAccelerationEnabled;
    [ObservableProperty] PortableVideoDecodeMode videoHardwareDecodeMode;
    [ObservableProperty] PortableVideoPresentationMode videoPresentationMode;
    [ObservableProperty] PlaybackVideoAdapterChoice? selectedVideoAdapter;
    [ObservableProperty] string videoAdapterAvailability = "";
    [ObservableProperty] string videoDecodeStatus = "Active decoder: no runtime snapshot is available.";
    [ObservableProperty] string videoPresentationStatus = "Active presentation: no runtime snapshot is available.";
    [ObservableProperty] string videoDecodePendingStatus = "";

    public string VideoDecodeRequest => baseline.VideoHardwareAccelerationEnabled
        ? $"Saved request: {baseline.VideoHardwareDecodeMode}; device: {DeviceName(baseline.VideoHardwareDevice)}."
        : $"Saved request: Software. Remembered hardware: {baseline.VideoHardwareDecodeMode}; device: {DeviceName(baseline.VideoHardwareDevice)}.";
    public string VideoPresentationRequest => $"Saved presentation request: {baseline.VideoPresentationMode}.";

    void InitializeVideoBackends(Func<PortableVideoDecodeStatus>? decodeStatus,
        Func<PortableVideoPresentationStatus>? presentationStatus,
        Func<IReadOnlyList<PortableVideoAdapterInfo>>? videoAdapters,
        Func<Task<IReadOnlyList<PortableVideoAdapterInfo>>>? refreshVideoAdapters)
    {
        readDecodeStatus = decodeStatus; readPresentationStatus = presentationStatus; refreshAdapters = refreshVideoAdapters;
        initializingVideoBackends = true;
        try
        {
            VideoHardwareAccelerationEnabled = Draft.VideoHardwareAccelerationEnabled;
            VideoHardwareDecodeMode = Draft.VideoHardwareDecodeMode;
            VideoPresentationMode = Draft.VideoPresentationMode;
        }
        finally { initializingVideoBackends = false; }
        try { adapterSnapshot = videoAdapters?.Invoke().ToArray() ?? []; }
        catch (Exception) { Warning = "The cached video adapter catalog is unavailable. Your saved device selection was preserved."; }
        RebuildVideoAdapterChoices();
        UpdateVideoBackendStatus();
    }

    static string DeviceName(string identity) => identity.Length == 0 ? "default" : identity;

    partial void OnVideoHardwareAccelerationEnabledChanged(bool value)
    {
        if (initializingVideoBackends) return;
        Draft.VideoHardwareAccelerationEnabled = value;
        UpdateDecodePendingStatus();
    }
    partial void OnVideoHardwareDecodeModeChanged(PortableVideoDecodeMode value)
    {
        if (initializingVideoBackends) return;
        Draft.VideoHardwareDecodeMode = value;
        RebuildVideoAdapterChoices();
        UpdateDecodePendingStatus();
    }
    partial void OnVideoPresentationModeChanged(PortableVideoPresentationMode value)
    {
        if (initializingVideoBackends) return;
        Draft.VideoPresentationMode = value;
    }
    partial void OnSelectedVideoAdapterChanged(PlaybackVideoAdapterChoice? value)
    {
        if (updatingAdapterChoices || value == null) return;
        // Persist the catalog identity; the decoder policy resolves its current native selector on open.
        Draft.VideoHardwareDevice = value.Id;
        VideoAdapterAvailability = value.UnavailableReason ?? (value.IsAvailable
            ? value.Id.Length == 0 ? "The decoder chooses the default native device on its next open." : "Listed by the native adapter catalog. Decoder support is checked when media opens."
            : "This saved device is unavailable on the current system.");
        UpdateDecodePendingStatus();
    }

    void RebuildVideoAdapterChoices()
    {
        updatingAdapterChoices = true;
        try
        {
            VideoAdapterChoices.Clear();
            VideoAdapterChoices.Add(new("", "Default native device", "", VideoHardwareDecodeMode, false,
                "The default native device is resolved on the next decoder open; a software fallback may be used."));
            foreach (PortableVideoAdapterInfo adapter in adapterSnapshot)
                if ((adapter.Mode is PortableVideoDecodeMode.D3D11VA or PortableVideoDecodeMode.VAAPI)
                    && (VideoHardwareDecodeMode == PortableVideoDecodeMode.Automatic || adapter.Mode == VideoHardwareDecodeMode))
                    VideoAdapterChoices.Add(new(adapter.Id, adapter.Name, adapter.DeviceSelector, adapter.Mode,
                        adapter.IsAvailable, adapter.UnavailableReason));
            PlaybackVideoAdapterChoice? selected = VideoAdapterChoices.FirstOrDefault(choice => choice.Id == Draft.VideoHardwareDevice);
            if (selected == null)
            {
                selected = new(Draft.VideoHardwareDevice, $"Saved device: {Draft.VideoHardwareDevice}", "",
                    VideoHardwareDecodeMode, false, "The saved device is not in this adapter catalog. Keep it for another system or explicitly choose a listed device.");
                VideoAdapterChoices.Add(selected);
            }
            SelectedVideoAdapter = selected;
        }
        finally { updatingAdapterChoices = false; }
        OnSelectedVideoAdapterChanged(SelectedVideoAdapter);
    }

    void UpdateDecodePendingStatus()
    {
        bool edited = Draft.VideoHardwareAccelerationEnabled != baseline.VideoHardwareAccelerationEnabled
            || Draft.VideoHardwareDecodeMode != baseline.VideoHardwareDecodeMode || Draft.VideoHardwareDevice != baseline.VideoHardwareDevice;
        VideoDecodePendingStatus = edited ? "Unsaved decode changes. Save applies them on the next decoder open."
            : decodeSnapshot?.IsPending == true ? "Pending: the saved decode request applies on the next decoder open." : "";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    void UpdateVideoBackendStatus()
    {
        try
        {
            decodeSnapshot = readDecodeStatus?.Invoke();
            presentationSnapshot = readPresentationStatus?.Invoke();
            VideoDecodeStatus = decodeSnapshot == null ? "Active decoder: no runtime snapshot is available."
                : !decodeSnapshot.IsInitialized ? "Active decoder: no decoder is open."
                : $"Active decoder: {decodeSnapshot.EffectiveMode}; device: {DeviceName(decodeSnapshot.EffectiveDevice ?? "")}."
                    + (decodeSnapshot.HardwareActive ? " Hardware decoding is active." : " Software decoding is active.")
                    + (decodeSnapshot.FallbackReason is { Length: > 0 } reason ? " " + reason : "");
            VideoPresentationStatus = presentationSnapshot == null ? "Active presentation: no runtime snapshot is available."
                : !presentationSnapshot.IsInitialized ? "Active presentation: no video surface is initialized."
                : $"Active presentation: {presentationSnapshot.EffectiveMode}."
                    + (presentationSnapshot.UsesPlanarShader ? " Planar video shader is active." : "")
                    + (presentationSnapshot.FallbackReason is { Length: > 0 } fallback ? " " + fallback : "");
            UpdateDecodePendingStatus();
        }
        catch (Exception) { Error = "Video runtime status could not be read. No device probe was started."; }
    }

    bool CanRefreshVideoAdapters() => !Busy && refreshAdapters != null;
    [RelayCommand(CanExecute = nameof(CanRefreshVideoAdapters))]
    async Task RefreshVideoAdaptersAsync()
    {
        if (!CanRefreshVideoAdapters()) return;
        Busy = true; Error = null;
        try
        {
            adapterSnapshot = (await refreshAdapters!()).ToArray();
            RebuildVideoAdapterChoices(); UpdateVideoBackendStatus();
        }
        catch (Exception) { Error = "Video adapters could not be refreshed. The selected device was preserved."; }
        finally { Busy = false; }
    }
}
