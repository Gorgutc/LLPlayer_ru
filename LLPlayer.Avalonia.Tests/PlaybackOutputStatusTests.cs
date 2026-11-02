using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class PlaybackOutputStatusTests
{
    [AvaloniaTheory]
    [InlineData("OpenAL", "speakers", false, true, false, "disabled")]
    [InlineData("null", null, true, true, false, "silent")]
    [InlineData("OpenAL", null, true, true, true, "recover")]
    [InlineData("OpenAL", null, true, false, false, "no audio stream")]
    [InlineData("OpenAL", "speakers", true, true, false, "System speakers")]
    public void Output_status_reports_engine_truth_and_keeps_the_missing_preferred_device_selected(
        string backend, string? effective, bool enabled, bool streamOpened, bool recovering, string expectedStatus)
    {
        PlaybackRuntimeSettings runtime = new()
        {
            AudioDevice = "preferred-usb", DefaultAudioDevice = "speakers", AudioRecovering = recovering,
            AudioDevices = [new AudioDeviceInfo("speakers", "System speakers")],
        };
        // This additive runtime projection must compile before its new output-state fields are introduced.
        Set(runtime, "AudioBackendName", backend); Set(runtime, "AudioOutputDevice", effective);
        Set(runtime, "AudioEnabled", enabled); Set(runtime, "AudioStreamOpened", streamOpened);
        var model = new PlaybackSettingsViewModel(new(), runtime, (_, _, _, _) => Task.CompletedTask);
        var window = new PlaybackSettingsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal("preferred-usb", model.SelectedAudioDevice!.Id);
            Assert.Contains(expectedStatus, model.AudioRecoveryStatus, StringComparison.OrdinalIgnoreCase);
            if (enabled && streamOpened && !recovering && effective != null)
            {
                Assert.Contains("OpenAL", model.AudioRecoveryStatus, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("fallback", model.AudioRecoveryStatus, StringComparison.OrdinalIgnoreCase);
            }
            TextBlock status = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == model.AudioRecoveryStatus);
            status.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Assert.True(status.IsEffectivelyVisible); Assert.True(status.Bounds.Width > 0 && status.Bounds.Height > 0);
        }
        finally { window.Close(); }
    }

    static void Set(PlaybackRuntimeSettings runtime, string name, object? value)
    {
        var property = typeof(PlaybackRuntimeSettings).GetProperty(name);
        Assert.NotNull(property);
        property.SetValue(runtime, value);
    }
}
