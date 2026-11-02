using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer.Dubbing;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>GPU-free voice choice for one opaque, host-validated subtitle cue.</summary>
public sealed partial class VoicePickerViewModel : ObservableObject
{
    readonly Func<string?, Task> save;
    public IReadOnlyList<TtsVoice> Voices { get; }
    [ObservableProperty] TtsVoice? selectedVoice;
    [ObservableProperty] string message = "Choose an existing voice for this subtitle line, or inherit the default voice.";
    [ObservableProperty] bool busy;
    public event Action? Saved;

    public VoicePickerViewModel(IReadOnlyList<TtsVoice> voices, string? currentVoiceId, Func<string?, Task> save)
    {
        this.save = save;
        var merged = new List<TtsVoice>(voices);
        string? current = string.IsNullOrWhiteSpace(currentVoiceId) ? null : currentVoiceId.Trim();
        if (current != null && !VoiceBankResolver.ContainsVoiceId(merged, current))
            merged.Add(VoiceBankResolver.CustomVoice(current));
        Voices = merged.AsReadOnly();
        selectedVoice = Voices.FirstOrDefault(voice => string.Equals(voice.Id, current, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    Task SaveAsync() => SaveChoiceAsync(SelectedVoice?.Id, requireSelection: true);

    [RelayCommand]
    Task UseDefaultAsync() => SaveChoiceAsync(null, requireSelection: false);

    async Task SaveChoiceAsync(string? voiceId, bool requireSelection)
    {
        if (Busy) return;
        if (requireSelection && !VoiceBankResolver.ContainsVoiceId(Voices, voiceId))
        { Message = "Select a voice, or choose Use default voice."; return; }
        Busy = true;
        try
        {
            await save(voiceId);
            Busy = false;
            Saved?.Invoke();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { Message = e.Message; }
        finally { Busy = false; }
    }
}
