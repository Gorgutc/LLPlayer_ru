using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>Detached existing-engine settings. Explicit Save persists before the host applies live state.</summary>
public sealed partial class DubbingViewModel : ObservableObject
{
    readonly Func<DubbingSettings, Task> save;
    public DubbingSettings Draft { get; }
    public string? Warning { get; }
    public event Action? Saved;
    public ObservableCollection<string> CustomVoiceIds { get; }
    [ObservableProperty] IReadOnlyList<TtsVoice> voices;
    [ObservableProperty] string selectedVoiceId;
    [ObservableProperty] string? selectedCustomVoiceId;
    [ObservableProperty] string newVoiceId = "";
    [ObservableProperty] string duckingText;
    [ObservableProperty] string atempoMinText;
    [ObservableProperty] string atempoMaxText;
    [ObservableProperty] bool busy;
    [ObservableProperty] string message = "Dubbing runs explicitly from Batch subtitles. Use an existing local CosyVoice environment and model; nothing is installed or downloaded here.";
    public TtsVoice? SelectedVoice
    {
        get => Voices.FirstOrDefault(voice => string.Equals(voice.Id, SelectedVoiceId, StringComparison.OrdinalIgnoreCase));
        set { if (value != null) SelectedVoiceId = value.Id; }
    }
    partial void OnSelectedVoiceIdChanged(string value) => OnPropertyChanged(nameof(SelectedVoice));
    public string DefaultPythonPath => Path.Combine(FlyleafLib.DubbingConfig.DefaultEngineDir,
        OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python");

    public DubbingViewModel(DubbingSettings settings, Func<DubbingSettings, Task> save, string? warning = null)
    {
        Draft = settings.Clone();
        this.save = save;
        Warning = warning;
        CustomVoiceIds = new(Draft.Engine.CustomVoiceIds);
        selectedVoiceId = Draft.Engine.DefaultVoiceId;
        voices = VoiceBankResolver.ForConfig(selectedVoiceId, CustomVoiceIds);
        duckingText = Draft.Engine.DuckingPercent.ToString(CultureInfo.InvariantCulture);
        atempoMinText = Draft.Engine.AtempoMin.ToString(CultureInfo.InvariantCulture);
        atempoMaxText = Draft.Engine.AtempoMax.ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    void AddVoice()
    {
        string value = NewVoiceId.Trim();
        if (value.Length == 0) return;
        if (!CustomVoiceIds.Contains(value, StringComparer.OrdinalIgnoreCase) && VoiceBankResolver.Resolve(value) == null)
            CustomVoiceIds.Add(value);
        NewVoiceId = "";
        RefreshVoices();
    }

    [RelayCommand]
    void UseLocalEngine() => Draft.Engine.TtsServiceType = TtsServiceType.LocalCosyVoice;

    [RelayCommand]
    void RemoveVoice()
    {
        if (SelectedCustomVoiceId != null) CustomVoiceIds.Remove(SelectedCustomVoiceId);
        SelectedCustomVoiceId = null;
        RefreshVoices(); // A selected custom voice remains selectable until explicitly replaced.
    }

    void RefreshVoices()
    {
        Voices = VoiceBankResolver.ForConfig(SelectedVoiceId, CustomVoiceIds);
        OnPropertyChanged(nameof(SelectedVoice));
    }

    [RelayCommand]
    async Task SaveAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            if (!int.TryParse(DuckingText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ducking) || ducking is < 0 or > 100)
                throw new ArgumentException("Ducking must be a whole percentage from 0 to 100.");
            double min = ParseTempo(AtempoMinText), max = ParseTempo(AtempoMaxText);
            DubbingSettings candidate = Draft.Clone();
            candidate.Engine.CustomVoiceIds = [.. CustomVoiceIds];
            candidate.Engine.DefaultVoiceId = SelectedVoiceId;
            candidate.Engine.DuckingPercent = ducking;
            candidate.Engine.AtempoMin = min;
            candidate.Engine.AtempoMax = max;
            candidate.Validate();
            await save(candidate);
            Busy = false;
            Saved?.Invoke();
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        { Message = e.Message; }
        finally { Busy = false; }
    }

    static double ParseTempo(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value) || value is < 0.5 or > 2)
            throw new ArgumentException("Time-stretch limits must be numbers from 0.5 to 2.0.");
        return value;
    }
}
