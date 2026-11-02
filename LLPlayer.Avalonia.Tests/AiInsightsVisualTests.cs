using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;
using FlyleafLib.MediaPlayer.AI;

namespace LLPlayer.Avalonia.Tests;

public class AiInsightsVisualTests
{
    [AvaloniaFact]
    public async Task Reachable_mode_slot_generate_results_and_live_transcript_bindings()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var cues = new List<string?>();
            await using var model = new AiInsightsViewModel((slot, translated) => new(1, "movie", DateTimeOffset.UtcNow,
                slot, translated, "English", "en", "Russian", "ru", cues),
                () => new("test model", new((_, _, _) => Task.FromResult("A summary."))), new(path));
            var window = new AiInsightsWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            var generate = window.FindControl<Button>("GenerateButton")!;
            Assert.Same(model.GenerateCommand, generate.Command);
            Assert.False(model.CanGenerate); Assert.False(model.GenerateCommand.CanExecute(null));
            // Avalonia's Button combines Command.CanExecute with IsEnabledCore. The locally set IsEnabled
            // property remains true; effective enablement is the actual user-facing state.
            Assert.False(generate.IsEffectivelyEnabled);
            cues.Add("new cue"); model.Refresh(); Dispatcher.UIThread.RunJobs();
            Assert.True(model.CanGenerate); Assert.True(model.GenerateCommand.CanExecute(null)); Assert.True(generate.IsEffectivelyEnabled);
            var mode = window.FindControl<ComboBox>("ModePicker")!; mode.SelectedItem = AiInsightMode.Summary;
            var slot = window.FindControl<ComboBox>("SlotPicker")!; slot.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs(); Assert.Equal(AiInsightMode.Summary, model.SelectedMode); Assert.Equal(1, model.SelectedSubIndex);
            await model.GenerateCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.Equal("A summary.", window.FindControl<SelectableTextBlock>("SummaryResult")!.Text);
            Assert.Contains("Secondary", model.ResultSourceText);
            window.Width = 480; window.Height = 420; Dispatcher.UIThread.RunJobs(); Assert.True(window.IsVisible);
            window.Close(); Dispatcher.UIThread.RunJobs();
        }
        finally { File.Delete(path); }
    }
}
