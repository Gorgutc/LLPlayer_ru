using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class DubbingViewModelTests
{
    [Fact]
    public async Task Custom_voices_and_numeric_settings_save_as_detached_snapshot()
    {
        DubbingSettings original = new();
        DubbingSettings? saved = null;
        var model = new DubbingViewModel(original, settings => { saved = settings; return Task.CompletedTask; });
        model.NewVoiceId = " custom-local ";
        model.AddVoiceCommand.Execute(null);
        Assert.Contains(model.Voices, voice => voice.Id == "custom-local");
        Assert.Empty(original.Engine.CustomVoiceIds);
        model.SelectedVoiceId = "custom-local";
        model.DuckingText = "0";
        model.AtempoMinText = "0.8";
        model.AtempoMaxText = "1.3";
        model.Draft.PersistPerLineVoices = true;
        await model.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved);
        Assert.Equal("custom-local", saved.Engine.DefaultVoiceId);
        Assert.Equal(0, saved.Engine.DuckingPercent);
        Assert.Equal(0.8, saved.Engine.AtempoMin);
        Assert.True(saved.PersistPerLineVoices);
        Assert.False(original.PersistPerLineVoices);
    }

    [Theory]
    [InlineData("abc", "0.9", "1.15")]
    [InlineData("101", "0.9", "1.15")]
    [InlineData("15", "NaN", "1.15")]
    [InlineData("15", "0", "1.15")]
    public async Task Invalid_numeric_input_does_not_save(string ducking, string min, string max)
    {
        int saves = 0;
        var model = new DubbingViewModel(new(), _ => { saves++; return Task.CompletedTask; });
        model.DuckingText = ducking;
        model.AtempoMinText = min;
        model.AtempoMaxText = max;
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, saves);
        Assert.NotEmpty(model.Message);
    }

    [Fact]
    public async Task Save_failure_keeps_editor_open_and_draft_available()
    {
        var model = new DubbingViewModel(new(), _ => throw new IOException("Read only"));
        int saved = 0;
        model.Saved += () => saved++;
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, saved);
        Assert.False(model.Busy);
        Assert.Contains("Read only", model.Message);
    }
}
