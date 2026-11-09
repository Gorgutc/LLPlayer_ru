using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaFramework.MediaDevice;

namespace LLPlayer.Avalonia.ViewModels;

public sealed record CaptureFormatChoice(string Label, CaptureVideoFormatInfo? Format);

/// <summary>Explicit capture discovery. Native work and canonical choice validation stay with the controller.</summary>
public sealed partial class CaptureDevicesViewModel : ObservableObject
{
    readonly Func<CaptureDeviceSnapshot> getSnapshot;
    readonly Func<CancellationToken, Task> refresh;
    readonly Func<CaptureDeviceInfo, long, CancellationToken, Task<CaptureVideoFormatSnapshot>> getFormats;
    readonly Func<CaptureDeviceInfo, long, CaptureVideoFormatInfo?, CancellationToken, Task> open;
    CaptureDeviceSnapshot snapshot = CaptureDeviceSnapshot.Initial;
    CancellationTokenSource? operationCancellation;
    bool closeAfterOperation;
    bool updatingDevices;

    public CaptureDeviceKind Kind { get; }
    public bool IsVideo => Kind == CaptureDeviceKind.Video;
    public string Heading => IsVideo ? "Video capture devices" : "Audio capture devices";
    public string Title => Heading + " — LLPlayer";
    public long CatalogEpoch => snapshot.Epoch;
    public ObservableCollection<CaptureDeviceInfo> Devices { get; } = [];
    public ObservableCollection<CaptureFormatChoice> Formats { get; } = [];
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(QueryFormatsCommand)), NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    CaptureDeviceInfo? selectedDevice;
    [ObservableProperty] CaptureFormatChoice? selectedFormat;
    [ObservableProperty] string status = "";
    [ObservableProperty] string formatStatus = "Choose Device default, or load the selected device's reported formats.";
    [ObservableProperty] string selectedDeviceDetails = "";
    [ObservableProperty] string? error;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RefreshCommand)), NotifyCanExecuteChangedFor(nameof(QueryFormatsCommand)),
        NotifyCanExecuteChangedFor(nameof(OpenCommand)), NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    bool busy;
    public event Action? CloseRequested;

    public CaptureDevicesViewModel(CaptureDeviceKind kind, Func<CaptureDeviceSnapshot> getSnapshot,
        Func<CancellationToken, Task> refresh,
        Func<CaptureDeviceInfo, long, CancellationToken, Task<CaptureVideoFormatSnapshot>> getFormats,
        Func<CaptureDeviceInfo, long, CaptureVideoFormatInfo?, CancellationToken, Task> open)
    {
        Kind = kind; this.getSnapshot = getSnapshot; this.refresh = refresh; this.getFormats = getFormats; this.open = open;
        // Reading the already-published catalog never enumerates a camera or queries its formats.
        UpdateSnapshot(getSnapshot());
    }

    static bool SameDevice(CaptureDeviceInfo left, CaptureDeviceInfo right) => left.Id == right.Id
        && left.Backend == right.Backend && left.Input == right.Input && left.Kind == right.Kind;

    void UpdateSnapshot(CaptureDeviceSnapshot current)
    {
        CaptureDeviceInfo? previous = SelectedDevice;
        snapshot = current with { Devices = current.Devices.ToArray() };
        updatingDevices = true;
        try
        {
            Devices.Clear();
            foreach (CaptureDeviceInfo device in snapshot.Devices.Where(device => device.Kind == Kind)) Devices.Add(device);
            SelectedDevice = previous == null ? Devices.FirstOrDefault() : Devices.FirstOrDefault(device => SameDevice(device, previous));
        }
        finally { updatingDevices = false; }
        ResetFormats(); UpdateDeviceDetails();
        Status = snapshot.Availability switch
        {
            CaptureAvailability.NotRefreshed => "Devices have not been refreshed. Use Refresh devices to discover them.",
            CaptureAvailability.Available when Devices.Count == 0 => "Discovery completed: no capture devices were found.",
            CaptureAvailability.Available => $"Discovery completed: {Devices.Count} capture device(s).",
            CaptureAvailability.Unavailable => "Capture is unavailable with the current runtime.",
            _ => "Capture discovery failed."
        };
        if (!string.IsNullOrWhiteSpace(snapshot.Reason)) Status += " " + snapshot.Reason;
        if (previous != null && SelectedDevice == null) Status += " The previously selected device is no longer in this catalog. Choose a current device.";
        OnPropertyChanged(nameof(CatalogEpoch));
        QueryFormatsCommand.NotifyCanExecuteChanged(); OpenCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDeviceChanged(CaptureDeviceInfo? value)
    {
        if (updatingDevices) return;
        ResetFormats(); UpdateDeviceDetails();
    }
    void UpdateDeviceDetails() => SelectedDeviceDetails = SelectedDevice is { } device
        ? $"Backend: {device.Backend}. Native ID: {device.Id}" : "No capture device is selected.";
    void ResetFormats()
    {
        Formats.Clear(); Formats.Add(new("Device default", null)); SelectedFormat = Formats[0];
        FormatStatus = "Choose Device default, or load the selected device's reported formats.";
    }

    bool CanRefresh() => !Busy;
    bool CanOpen() => !Busy && snapshot.Availability == CaptureAvailability.Available && SelectedDevice is { } device
        && Devices.Any(current => SameDevice(current, device));
    bool CanQueryFormats() => IsVideo && CanOpen();
    bool CanCancel() => Busy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    Task RefreshAsync() => RunOperation(async token =>
    {
        try
        {
            await refresh(token);
            token.ThrowIfCancellationRequested();
        }
        finally { UpdateSnapshot(getSnapshot()); }
    });

    [RelayCommand(CanExecute = nameof(CanQueryFormats))]
    Task QueryFormatsAsync()
    {
        if (!CanQueryFormats() || SelectedDevice is not { } device) return Task.CompletedTask;
        long epoch = snapshot.Epoch;
        return RunOperation(async token =>
        {
            CaptureVideoFormatSnapshot result = await getFormats(device, epoch, token);
            token.ThrowIfCancellationRequested();
            ResetFormats();
            if (result.CatalogEpoch != snapshot.Epoch || result.DeviceId != device.Id
                || SelectedDevice is not { } selected || !SameDevice(selected, device))
                throw new InvalidOperationException("The capture catalog changed. Refresh devices and load the current device's formats again.");
            if (result.Availability == CaptureAvailability.Available)
                foreach (CaptureVideoFormatInfo format in result.Formats)
                    if (format.DeviceId == device.Id) Formats.Add(new(FormatLabel(format), format));
            FormatStatus = result.Availability == CaptureAvailability.Available
                ? result.Formats.Count == 0 ? "No specific formats were reported. Device default remains available."
                    : "These formats were reported by the selected device. Device default remains available."
                : "Specific formats are unavailable. Device default remains available.";
            if (!string.IsNullOrWhiteSpace(result.Reason)) FormatStatus += " " + result.Reason;
        });
    }

    static string FormatLabel(CaptureVideoFormatInfo format)
    {
        string rate = format.FrameRateDenominator > 0
            ? $"{format.FrameRateNumerator}/{format.FrameRateDenominator} fps" : "device frame rate";
        string detail = string.Join("; ", new[] { format.PixelFormat, format.Codec }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return $"{format.Width} × {format.Height}; {rate}" + (detail.Length == 0 ? "" : "; " + detail);
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    Task OpenAsync()
    {
        if (!CanOpen() || SelectedDevice is not { } device) return Task.CompletedTask;
        long epoch = snapshot.Epoch;
        CaptureVideoFormatInfo? format = IsVideo ? SelectedFormat?.Format : null;
        return RunOperation(async token =>
        {
            await open(device, epoch, format, token);
            token.ThrowIfCancellationRequested();
            closeAfterOperation = true;
        });
    }

    async Task RunOperation(Func<CancellationToken, Task> operation)
    {
        if (Busy || closeAfterOperation) return;
        using var cancellation = new CancellationTokenSource(); operationCancellation = cancellation;
        Busy = true; Error = null;
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Status = "Capture operation canceled."; }
        catch (Exception exception)
        {
            Error = exception is ArgumentException or InvalidOperationException ? exception.Message
                : "The capture operation failed. Refresh devices and retry.";
        }
        finally
        {
            operationCancellation = null; Busy = false;
            if (closeAfterOperation) CloseRequested?.Invoke();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    void Cancel() => operationCancellation?.Cancel();
    [RelayCommand] void Close() => RequestClose();

    public void RequestClose()
    {
        closeAfterOperation = true;
        operationCancellation?.Cancel();
        // A native call without an interrupt API must return before its dialog can close.
        if (!Busy) CloseRequested?.Invoke();
    }
}
