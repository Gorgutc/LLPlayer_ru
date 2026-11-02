using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class VoicePickerViewModelTests
{
    [Fact]
    public async Task Unknown_current_assignment_remains_selectable_and_default_clears_override()
    {
        List<string?> applied = [];
        var model = new VoicePickerViewModel(VoiceBankResolver.BuiltIn, "custom-current", value =>
        { applied.Add(value); return Task.CompletedTask; });
        Assert.Equal("custom-current", model.SelectedVoice?.Id);
        await model.SaveCommand.ExecuteAsync(null);
        await model.UseDefaultCommand.ExecuteAsync(null);
        Assert.Equal(new string?[] { "custom-current", null }, applied);
    }

    [Fact]
    public async Task Foreign_selection_is_rejected_and_failed_save_keeps_window_open()
    {
        int calls = 0, closed = 0;
        var model = new VoicePickerViewModel(VoiceBankResolver.BuiltIn, null, _ =>
        { calls++; throw new InvalidOperationException("The subtitle source changed."); });
        model.Saved += () => closed++;
        model.SelectedVoice = VoiceBankResolver.CustomVoice("not-in-this-bank");
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, calls);
        model.SelectedVoice = model.Voices[0];
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
        Assert.Equal(0, closed);
        Assert.False(model.Busy);
        Assert.Contains("source changed", model.Message);
    }
}
