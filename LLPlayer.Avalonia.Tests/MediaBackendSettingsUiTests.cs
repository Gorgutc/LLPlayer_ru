using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using System.Reflection;
using FlyleafLib.MediaFramework.MediaRenderer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public sealed class MediaBackendSettingsUiTests
{
    [AvaloniaTheory]
    [InlineData(PortableVideoDecodeMode.D3D11VA, "dxgi:luid:1234", "3", true)]
    [InlineData(PortableVideoDecodeMode.VAAPI, "pci:0000:01:00.0", "/dev/dri/renderD130", false)]
    public async Task Selecting_an_adapter_saves_the_identity_that_the_actual_decode_policy_resolves(
        PortableVideoDecodeMode mode, string id, string selector, bool windows)
    {
        PortableVideoAdapterInfo[] adapters = [new(id, "Selected native adapter", mode, selector, true, null)];
        PlaybackSettings? saved = null;
        var model = new PlaybackSettingsViewModel(new(), new(), (candidate, _, _, _) =>
        { saved = candidate; return Task.CompletedTask; }, videoAdapters: () => adapters);
        var window = new PlaybackSettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            window.FindControl<TabControl>("PlaybackTabs")!.SelectedIndex = 1;
            window.FindControl<CheckBox>("HardwareAccelerationEditor")!.IsChecked = true;
            window.FindControl<ComboBox>("HardwareDecodeModeEditor")!.SelectedItem = mode;
            Dispatcher.UIThread.RunJobs();
            window.FindControl<ComboBox>("VideoAdapterEditor")!.SelectedItem =
                model.VideoAdapterChoices.Single(choice => choice.Id == id);
            Dispatcher.UIThread.RunJobs();

            await model.SaveCommand.ExecuteAsync(null);

            Assert.NotNull(saved);
            var resolved = ResolveSavedRequest(saved, windows, adapters);
            Assert.Equal(mode, resolved.Mode);
            Assert.Equal(id, resolved.EffectiveDevice);
            Assert.Equal(selector, resolved.DeviceSelector);
            Assert.Null(resolved.FallbackReason);
            Assert.Equal(id, saved.VideoHardwareDevice);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [Theory]
    [InlineData(PortableVideoDecodeMode.D3D11VA, "dxgi:luid:1234", "3", "4")]
    [InlineData(PortableVideoDecodeMode.VAAPI, "pci:0000:01:00.0", "/dev/dri/renderD130", "/dev/dri/renderD131")]
    public async Task Refresh_matches_the_saved_identity_when_the_native_selector_changes(
        PortableVideoDecodeMode mode, string id, string selector, string refreshedSelector)
    {
        PlaybackSettings? saved = null;
        var current = new PlaybackSettings { VideoHardwareDecodeMode = mode, VideoHardwareDevice = id };
        var model = new PlaybackSettingsViewModel(current, new(), (candidate, _, _, _) =>
        { saved = candidate; return Task.CompletedTask; }, videoAdapters: () =>
            [new(id, "Native adapter", mode, selector, true, null)], refreshVideoAdapters: () =>
                Task.FromResult<IReadOnlyList<PortableVideoAdapterInfo>>(
                    [new(id, "Native adapter", mode, refreshedSelector, true, null)]));

        Assert.Equal(id, model.SelectedVideoAdapter!.Id);
        Assert.True(model.SelectedVideoAdapter.IsAvailable);
        Assert.Equal(id, model.Draft.VideoHardwareDevice);
        await model.RefreshVideoAdaptersCommand.ExecuteAsync(null);
        Assert.Equal(id, model.SelectedVideoAdapter!.Id);
        Assert.Equal(refreshedSelector, model.SelectedVideoAdapter.DeviceSelector);
        Assert.True(model.SelectedVideoAdapter.IsAvailable);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved);
        Assert.Equal(id, saved.VideoHardwareDevice);
    }

    [Fact]
    public async Task Cached_adapter_choices_do_not_probe_and_keep_a_foreign_saved_identity_visible()
    {
        int refreshes = 0; PlaybackSettings? saved = null;
        var original = new PlaybackSettings
        {
            VideoHardwareDecodeMode = PortableVideoDecodeMode.VAAPI,
            VideoHardwareDevice = "/dev/dri/renderD130"
        };
        var model = new PlaybackSettingsViewModel(original, new(), (candidate, _, _, _) =>
        { saved = candidate; return Task.CompletedTask; }, videoAdapters: () =>
            [new("dxgi-adapter", "Native Windows adapter", PortableVideoDecodeMode.D3D11VA, "2", true, null)],
            refreshVideoAdapters: () =>
            {
                refreshes++; return Task.FromResult<IReadOnlyList<PortableVideoAdapterInfo>>([]);
            });

        Assert.Equal(0, refreshes);
        Assert.Equal("/dev/dri/renderD130", model.SelectedVideoAdapter!.Id);
        Assert.Equal("", model.SelectedVideoAdapter.DeviceSelector);
        Assert.False(model.SelectedVideoAdapter.IsAvailable);
        Assert.Contains("not in this adapter catalog", model.VideoAdapterAvailability);
        Assert.DoesNotContain(model.VideoAdapterChoices, choice => choice.Id == "dxgi-adapter");
        await model.RefreshVideoAdaptersCommand.ExecuteAsync(null);
        Assert.Equal(1, refreshes);
        Assert.Equal("/dev/dri/renderD130", model.SelectedVideoAdapter!.Id);
        Assert.Equal("", model.SelectedVideoAdapter.DeviceSelector);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved);
        Assert.Equal("/dev/dri/renderD130", saved.VideoHardwareDevice);
        Assert.Equal(PortableVideoDecodeMode.VAAPI, saved.VideoHardwareDecodeMode);
        Assert.False(saved.VideoHardwareAccelerationEnabled);
    }

    [Fact]
    public void Runtime_fallback_and_pending_are_separate_from_the_saved_enabled_request()
    {
        var current = new PlaybackSettings { VideoHardwareAccelerationEnabled = true,
            VideoHardwareDecodeMode = PortableVideoDecodeMode.VAAPI, VideoPresentationMode = PortableVideoPresentationMode.OpenGL };
        var model = new PlaybackSettingsViewModel(current, new(), (_, _, _, _) => Task.CompletedTask,
            decodeStatus: () => new(PortableVideoDecodeMode.VAAPI, "", PortableVideoDecodeMode.Software,
                null, true, false, "Driver initialization failed.") { IsPending = true },
            presentationStatus: () => new(PortableVideoPresentationMode.OpenGL, PortableVideoPresentationMode.Software,
                true, false, "The GL context is unavailable."));

        Assert.True(model.VideoHardwareAccelerationEnabled);
        Assert.Contains("Saved request: VAAPI", model.VideoDecodeRequest);
        Assert.Contains("Active decoder: Software", model.VideoDecodeStatus);
        Assert.Contains("Driver initialization failed", model.VideoDecodeStatus);
        Assert.Contains("Pending:", model.VideoDecodePendingStatus);
        Assert.Contains("Active presentation: Software", model.VideoPresentationStatus);
        Assert.Contains("GL context is unavailable", model.VideoPresentationStatus);
        model.VideoHardwareAccelerationEnabled = false;
        Assert.Contains("Unsaved", model.VideoDecodePendingStatus);
        Assert.True(current.VideoHardwareAccelerationEnabled);
    }

    [AvaloniaFact]
    public async Task Real_backend_controls_save_the_stable_identity_and_presentation_without_changing_the_input()
    {
        PlaybackSettings? saved = null; var current = new PlaybackSettings();
        var model = new PlaybackSettingsViewModel(current, new(), (candidate, _, _, _) =>
        { saved = candidate; return Task.CompletedTask; }, videoAdapters: () =>
            [new("native-id-is-not-the-selector", "Adapter 3", PortableVideoDecodeMode.D3D11VA, "3", true, null)]);
        var window = new PlaybackSettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            window.FindControl<TabControl>("PlaybackTabs")!.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            window.FindControl<CheckBox>("HardwareAccelerationEditor")!.IsChecked = true;
            window.FindControl<ComboBox>("HardwareDecodeModeEditor")!.SelectedItem = PortableVideoDecodeMode.D3D11VA;
            Dispatcher.UIThread.RunJobs();
            window.FindControl<ComboBox>("VideoAdapterEditor")!.SelectedItem = model.VideoAdapterChoices.Single(choice => choice.DeviceSelector == "3");
            window.FindControl<ComboBox>("VideoPresentationModeEditor")!.SelectedItem = PortableVideoPresentationMode.OpenGL;
            Dispatcher.UIThread.RunJobs();
            await model.SaveCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(saved);
            Assert.True(saved.VideoHardwareAccelerationEnabled);
            Assert.Equal(PortableVideoDecodeMode.D3D11VA, saved.VideoHardwareDecodeMode);
            Assert.Equal("native-id-is-not-the-selector", saved.VideoHardwareDevice);
            Assert.Equal(PortableVideoPresentationMode.OpenGL, saved.VideoPresentationMode);
            Assert.False(current.VideoHardwareAccelerationEnabled);
            Assert.Equal("", current.VideoHardwareDevice);
            Assert.False(window.IsVisible);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [Fact]
    public async Task Failed_backend_save_keeps_the_request_draft_and_window_open_for_retry()
    {
        var current = new PlaybackSettings(); int closes = 0;
        var model = new PlaybackSettingsViewModel(current, new(), (_, _, _, _) => throw new IOException("test failure"));
        model.CloseRequested += () => closes++;
        model.VideoHardwareAccelerationEnabled = true;
        model.VideoPresentationMode = PortableVideoPresentationMode.OpenGL;
        await model.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(model.Error); Assert.Equal(0, closes); Assert.False(model.Busy);
        Assert.True(model.VideoHardwareAccelerationEnabled);
        Assert.Equal(PortableVideoPresentationMode.OpenGL, model.VideoPresentationMode);
        Assert.False(current.VideoHardwareAccelerationEnabled);
        Assert.Equal(PortableVideoPresentationMode.Software, current.VideoPresentationMode);
    }

    static (PortableVideoDecodeMode Mode, string? DeviceSelector, string? EffectiveDevice, string? FallbackReason)
        ResolveSavedRequest(PlaybackSettings settings, bool windows, IReadOnlyList<PortableVideoAdapterInfo> adapters)
    {
        // Exercise the real internal policy without expanding the product's public API for a cross-project test.
        Type policy = typeof(PortableVideoAdapterInfo).Assembly.GetType(
            "FlyleafLib.MediaFramework.MediaRenderer.PortableHardwareDecodePolicy", throwOnError: true)!;
        object result = policy.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [settings.VideoHardwareDecodeMode, settings.VideoHardwareDevice, true, windows, !windows, adapters])!;
        Type resultType = result.GetType();
        return ((PortableVideoDecodeMode)resultType.GetProperty("Mode")!.GetValue(result)!,
            (string?)resultType.GetProperty("DeviceSelector")!.GetValue(result),
            (string?)resultType.GetProperty("EffectiveDevice")!.GetValue(result),
            (string?)resultType.GetProperty("FallbackReason")!.GetValue(result));
    }
}
