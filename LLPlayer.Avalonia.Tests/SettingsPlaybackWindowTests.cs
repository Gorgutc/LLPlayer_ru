using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class SettingsPlaybackWindowTests
{
    [AvaloniaFact]
    public async Task Actual_audio_controls_save_detached_values_and_close_after_the_async_save_finishes()
    {
        PlaybackSettings? saved = null; bool? defaultEdited = null;
        var model = new PlaybackSettingsViewModel(new(), new(),
            (candidate, _, _, dirty) => { saved = candidate; defaultEdited = dirty; return Task.CompletedTask; });
        var window = new PlaybackSettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            window.FindControl<CheckBox>("AudioEnabledEditor")!.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.Draft.AudioEnabled);
            TextBox number = window.GetVisualDescendants().OfType<TextBox>()
                .Single(control => control.DataContext is PlaybackNumberEditor { Key: "AudioVolumeDefault" });
            number.Text = "90"; Dispatcher.UIThread.RunJobs();
            await model.SaveCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(saved); Assert.False(saved.AudioEnabled); Assert.Equal(90, saved.AudioVolumeDefault);
            Assert.True(defaultEdited); Assert.False(window.IsVisible);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [AvaloniaFact]
    public void Minimum_window_keeps_footer_reachable_and_exposes_real_policy_and_hardware_controls()
    {
        var model = new PlaybackSettingsViewModel(new(), new(), (_, _, _, _) => Task.CompletedTask);
        var window = new PlaybackSettingsWindow(model) { Width = 560, Height = 440 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Button save = window.FindControl<Button>("SavePlaybackSettingsButton")!;
            Assert.True(save.Bounds.Width > 0 && save.Bounds.Height > 0);
            var tabs = window.FindControl<TabControl>("PlaybackTabs")!;
            Assert.Equal(5, tabs.ItemCount);
            tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.FindControl<ComboBox>("AspectRatioEditor"));
            CheckBox hardware = window.FindControl<CheckBox>("HardwareAccelerationEditor")!;
            Assert.False(hardware.IsEnabled); Assert.False(hardware.IsChecked);
            tabs.SelectedIndex = 3; Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.FindControl<CheckBox>("SubtitleExportBomEditor"));
            Assert.NotNull(window.FindControl<ComboBox>("PrimaryFallbackEditor"));
            tabs.SelectedIndex = 4; Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.FindControl<ComboBox>("FFmpegLoadProfileEditor"));
            Assert.NotNull(window.FindControl<ComboBox>("EngineLogLevelEditor"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Refresh_is_explicit_preserves_the_draft_selection_and_does_not_save_or_change_delays()
    {
        int refreshes = 0, saves = 0;
        var original = new PlaybackRuntimeSettings
        {
            AudioDevice = "missing", DefaultAudioDevice = "a",
            AudioDevices = [new("a", "Device A")], AudioDelayTicks = 550000,
            SessionIdentity = new object()
        };
        PlaybackRuntimeSettings? savedRuntime = null;
        var model = new PlaybackSettingsViewModel(new(), original, (_, _, runtime, _) =>
        { saves++; savedRuntime = runtime; return Task.CompletedTask; }, refreshDevices: () =>
        {
            refreshes++;
            return Task.FromResult(original with { DefaultAudioDevice = "b", AudioDevices = [new("b", "Device B")] });
        });
        Assert.Equal(0, refreshes); Assert.Equal("missing", model.SelectedAudioDevice!.Id);
        model.FindNumber("AudioDelayMs").Text = "70";
        await model.RefreshDevicesCommand.ExecuteAsync(null);
        Assert.Equal(1, refreshes); Assert.Equal(0, saves); Assert.Equal("missing", model.SelectedAudioDevice!.Id);
        Assert.Equal("Device B", model.DefaultAudioDeviceName); Assert.Equal("70", model.FindNumber("AudioDelayMs").Text);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(700000, savedRuntime!.AudioDelayTicks);
        Assert.Same(original.SessionIdentity, savedRuntime.SessionIdentity);
        Assert.Equal(550000, savedRuntime.Baseline!.AudioDelayTicks);
    }

    [AvaloniaFact]
    public async Task Busy_save_vetoes_close_and_a_failed_save_leaves_window_open_for_retry()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool fail = true;
        var model = new PlaybackSettingsViewModel(new(), new(), async (_, _, _, _) =>
        { await completion.Task; if (fail) throw new IOException("test write failure"); });
        var window = new PlaybackSettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Task pending = model.SaveCommand.ExecuteAsync(null); Assert.True(model.Busy);
            window.Close(); Assert.True(window.IsVisible);
            completion.SetResult(); await pending; Dispatcher.UIThread.RunJobs();
            Assert.NotNull(model.Error); Assert.True(window.IsVisible); Assert.False(model.Busy);
            fail = false; await model.SaveCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.Null(model.Error); Assert.False(window.IsVisible);
        }
        finally { if (window.IsVisible) window.Close(); }
    }
}
