using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib.MediaFramework.MediaDevice;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public sealed class CaptureDevicesViewModelTests
{
    static readonly CaptureDeviceInfo Camera = new("camera:1", "Camera one", "v4l2", "/dev/video0", CaptureDeviceKind.Video);
    static CaptureDeviceSnapshot Catalog(long epoch, CaptureDeviceInfo? device = null)
        => new(epoch, CaptureAvailability.Available, [device ?? Camera], null);
    static CaptureVideoFormatSnapshot NoFormats(CaptureDeviceInfo device, long epoch)
        => new(epoch, device.Id, CaptureAvailability.Available, [], null);

    [Fact]
    public void Opening_the_dialog_reads_only_the_cached_snapshot()
    {
        int refreshes = 0, queries = 0, opens = 0;
        var model = new CaptureDevicesViewModel(CaptureDeviceKind.Video, () => CaptureDeviceSnapshot.Initial,
            _ => { refreshes++; return Task.CompletedTask; }, (device, epoch, _) =>
            { queries++; return Task.FromResult(NoFormats(device, epoch)); }, (_, _, _, _) =>
            { opens++; return Task.CompletedTask; });

        Assert.Equal(0, refreshes); Assert.Equal(0, queries); Assert.Equal(0, opens);
        Assert.Empty(model.Devices); Assert.Contains("not been refreshed", model.Status);
        Assert.False(model.OpenCommand.CanExecute(null));
        Assert.Single(model.Formats); Assert.Null(model.SelectedFormat!.Format);
    }

    [Fact]
    public async Task Exact_rational_format_and_catalog_epoch_reach_the_open_owner_unchanged()
    {
        var exact = new CaptureVideoFormatInfo("format:1", Camera.Id, 1920, 1080, 30000, 1001, "nv12", null);
        CaptureDeviceInfo? opened = null; CaptureVideoFormatInfo? openedFormat = null; long openedEpoch = -1; int closes = 0;
        var model = new CaptureDevicesViewModel(CaptureDeviceKind.Video, () => Catalog(12), _ => Task.CompletedTask,
            (device, epoch, _) =>
            {
                Assert.Same(Camera, device); Assert.Equal(12, epoch);
                return Task.FromResult(new CaptureVideoFormatSnapshot(epoch, device.Id, CaptureAvailability.Available, [exact], null));
            }, (device, epoch, format, _) =>
            { opened = device; openedEpoch = epoch; openedFormat = format; return Task.CompletedTask; });
        model.CloseRequested += () => closes++;

        await model.QueryFormatsCommand.ExecuteAsync(null);
        CaptureFormatChoice choice = model.Formats.Single(choice => choice.Format != null);
        Assert.Contains("30000/1001 fps", choice.Label);
        model.SelectedFormat = choice;
        await model.OpenCommand.ExecuteAsync(null);
        Assert.Same(Camera, opened); Assert.Same(exact, openedFormat); Assert.Equal(12, openedEpoch);
        Assert.Equal(1, closes); Assert.False(model.Busy);
    }

    [Fact]
    public async Task Explicit_refresh_replaces_the_epoch_and_invalidates_previous_formats()
    {
        CaptureDeviceSnapshot snapshot = Catalog(1); int refreshes = 0, queries = 0;
        var exact = new CaptureVideoFormatInfo("format:1", Camera.Id, 640, 480, 30, 1, "yuyv422", null);
        long openedEpoch = 0; CaptureVideoFormatInfo? openedFormat = exact;
        var model = new CaptureDevicesViewModel(CaptureDeviceKind.Video, () => snapshot,
            _ => { refreshes++; snapshot = Catalog(2, Camera with { Label = "Renamed camera" }); return Task.CompletedTask; },
            (device, epoch, _) =>
            { queries++; return Task.FromResult(new CaptureVideoFormatSnapshot(epoch, device.Id, CaptureAvailability.Available, [exact], null)); },
            (_, epoch, format, _) => { openedEpoch = epoch; openedFormat = format; return Task.CompletedTask; });
        Assert.Equal(0, queries); Assert.Equal(0, refreshes);
        await model.QueryFormatsCommand.ExecuteAsync(null);
        model.SelectedFormat = model.Formats.Single(choice => choice.Format != null);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, refreshes); Assert.Equal(1, queries); Assert.Equal(2, model.CatalogEpoch);
        Assert.Equal("Renamed camera", model.SelectedDevice!.Label);
        Assert.Single(model.Formats); Assert.Null(model.SelectedFormat!.Format);
        await model.OpenCommand.ExecuteAsync(null);
        Assert.Equal(2, openedEpoch); Assert.Null(openedFormat);
    }

    [Fact]
    public async Task Unreported_stepwise_formats_keep_default_open_and_the_native_reason()
    {
        CaptureVideoFormatInfo? opened = new("unused", Camera.Id, 1, 1, 1, 1, null, null);
        var model = new CaptureDevicesViewModel(CaptureDeviceKind.Video, () => Catalog(3), _ => Task.CompletedTask,
            (device, epoch, _) => Task.FromResult(new CaptureVideoFormatSnapshot(epoch, device.Id,
                CaptureAvailability.Unavailable, [], "The device reports stepwise ranges; choose Device default.")),
            (_, _, format, _) => { opened = format; return Task.CompletedTask; });
        await model.QueryFormatsCommand.ExecuteAsync(null);
        Assert.Single(model.Formats); Assert.Contains("stepwise ranges", model.FormatStatus);
        Assert.True(model.OpenCommand.CanExecute(null));
        await model.OpenCommand.ExecuteAsync(null); Assert.Null(opened);
    }

    [Fact]
    public async Task Closing_cancels_but_does_not_complete_until_the_native_operation_drains()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken runningToken = default; int closes = 0;
        var model = new CaptureDevicesViewModel(CaptureDeviceKind.Video, () => CaptureDeviceSnapshot.Initial,
            async token => { runningToken = token; await completion.Task; },
            (device, epoch, _) => Task.FromResult(NoFormats(device, epoch)), (_, _, _, _) => Task.CompletedTask);
        model.CloseRequested += () => closes++;
        Task pending = model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.Busy); model.RequestClose();
        Assert.True(runningToken.IsCancellationRequested); Assert.Equal(0, closes); Assert.False(pending.IsCompleted);
        completion.SetResult(); await pending;
        Assert.Equal(1, closes); Assert.False(model.Busy);
    }

    [Fact]
    public void Unavailable_profile_is_distinct_from_a_successful_empty_catalog()
    {
        CaptureDevicesViewModel Create(CaptureDeviceSnapshot snapshot) => new(CaptureDeviceKind.Video, () => snapshot,
            _ => Task.CompletedTask, (device, epoch, _) => Task.FromResult(NoFormats(device, epoch)), (_, _, _, _) => Task.CompletedTask);
        var unavailable = Create(new(4, CaptureAvailability.Unavailable, [], "Restart with FFmpeg All."));
        var empty = Create(new(5, CaptureAvailability.Available, [], null));
        Assert.Contains("unavailable", unavailable.Status); Assert.Contains("Restart with FFmpeg All", unavailable.Status);
        Assert.Contains("no capture devices were found", empty.Status);
        Assert.False(unavailable.OpenCommand.CanExecute(null)); Assert.False(empty.OpenCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Capture_window_keeps_actions_reachable_and_never_refreshes_on_show()
    {
        int refreshes = 0;
        var model = new CaptureDevicesViewModel(CaptureDeviceKind.Video, () => CaptureDeviceSnapshot.Initial,
            _ => { refreshes++; return Task.CompletedTask; }, (device, epoch, _) => Task.FromResult(NoFormats(device, epoch)),
            (_, _, _, _) => Task.CompletedTask);
        var window = new CaptureDevicesWindow(model) { Width = 520, Height = 420 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(0, refreshes);
            Button close = window.FindControl<Button>("CloseCaptureDevicesButton")!;
            Assert.True(close.Bounds.Width > 0 && close.Bounds.Height > 0);
            Assert.NotNull(window.FindControl<ComboBox>("CaptureDeviceEditor"));
            Assert.NotNull(window.FindControl<ComboBox>("CaptureFormatEditor"));
            Assert.False(model.OpenCommand.CanExecute(null));
            Assert.False(window.FindControl<Button>("OpenCaptureDeviceButton")!.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Capture_window_close_waits_for_an_uninterruptible_refresh_to_return()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken runningToken = default;
        var model = new CaptureDevicesViewModel(CaptureDeviceKind.Video, () => CaptureDeviceSnapshot.Initial,
            async token => { runningToken = token; await completion.Task; },
            (device, epoch, _) => Task.FromResult(NoFormats(device, epoch)), (_, _, _, _) => Task.CompletedTask);
        var window = new CaptureDevicesWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        Task pending = model.RefreshCommand.ExecuteAsync(null);
        try
        {
            Assert.True(model.Busy); window.Close();
            Assert.True(window.IsVisible); Assert.True(runningToken.IsCancellationRequested);
            completion.SetResult(); await pending; Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible); Assert.False(model.Busy);
        }
        finally
        {
            completion.TrySetResult(); await pending;
            if (window.IsVisible) window.Close();
        }
    }
}
