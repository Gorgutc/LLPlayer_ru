using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class DubbingWindowTests
{
    [AvaloniaFact]
    public void Settings_and_cue_picker_show_known_voices_and_close_without_saving()
    {
        int saves = 0;
        var settings = new DubbingViewModel(new(), _ => { saves++; return Task.CompletedTask; });
        var window = new DubbingWindow(settings);
        window.Show();
        var picker = window.FindControl<ComboBox>("DubbingVoicePicker")!;
        Assert.Equal(VoiceBankResolver.BuiltIn[0].Id, Assert.IsType<TtsVoice>(picker.SelectedItem).Id);
        Assert.Equal("15", window.FindControl<TextBox>("DuckingBox")!.Text);
        window.Close();
        var cueModel = new VoicePickerViewModel(VoiceBankResolver.BuiltIn, "custom-existing", _ =>
        { saves++; return Task.CompletedTask; });
        var cueWindow = new VoicePickerWindow(cueModel);
        cueWindow.Show();
        Assert.Equal("custom-existing", Assert.IsType<TtsVoice>(cueWindow.FindControl<ComboBox>("CueVoicePicker")!.SelectedItem).Id);
        cueWindow.Close();
        Assert.Equal(0, saves);
    }
}
