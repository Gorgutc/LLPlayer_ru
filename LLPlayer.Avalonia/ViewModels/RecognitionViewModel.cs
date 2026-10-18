using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib;
using LLPlayer.Core.Services;
using System.Globalization;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>A detached recognition draft. Dismissal never applies or saves it.</summary>
public partial class RecognitionViewModel : ObservableObject
{
    readonly RecognitionActions actions;
    readonly RecognitionSettingsStore store;
    public RecognitionSettings Draft { get; }
    public event Action? Saved;
    public string? Warning { get; }
    [ObservableProperty] string message = "";
    [ObservableProperty] IReadOnlyList<WhisperCppModel> models = [];
    [ObservableProperty] WhisperCppModel? selectedModel;
    [ObservableProperty] string modelDirectory;
    [ObservableProperty] bool busy;
    // Keep raw editing text: invalid input must never fall back to an earlier numeric value.
    [ObservableProperty] string threadsText;
    [ObservableProperty] string maxSegmentLengthText;
    [ObservableProperty] string maxTokensPerSegmentText;

    public RecognitionViewModel(RecognitionSettings settings, RecognitionActions actions, RecognitionSettingsStore store, string? warning = null)
    {
        Draft = settings.Clone();
        this.actions = actions;
        this.store = store;
        modelDirectory = Draft.WhisperCpp.ModelDirectory ?? WhisperConfig.ModelsDirectory;
        selectedModel = Draft.WhisperCpp.Model;
        threadsText = DisplayInteger(Draft.WhisperCpp.Threads);
        maxSegmentLengthText = DisplayInteger(Draft.WhisperCpp.MaxSegmentLength);
        maxTokensPerSegmentText = DisplayInteger(Draft.WhisperCpp.MaxTokensPerSegment);
        Warning = warning;
        message = "Choose existing whisper.cpp CPU weights. Models are never downloaded automatically.";
        RefreshModels();
    }
    static string DisplayInteger(int? value) => value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : "";

    static int? ParseInteger(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
            throw new ArgumentException($"{label}: enter a whole number from 1 to 2147483647, or leave empty for the library default.");
        return value;
    }

    [RelayCommand]
    void ResetAdvanced()
    {
        WhisperCppConfig defaults = new();
        Draft.WhisperCpp.Threads = defaults.Threads;
        Draft.WhisperCpp.MaxSegmentLength = defaults.MaxSegmentLength;
        Draft.WhisperCpp.MaxTokensPerSegment = defaults.MaxTokensPerSegment;
        ThreadsText = DisplayInteger(defaults.Threads);
        MaxSegmentLengthText = DisplayInteger(defaults.MaxSegmentLength);
        MaxTokensPerSegmentText = DisplayInteger(defaults.MaxTokensPerSegment);
        Draft.WhisperCpp.SplitOnWord = defaults.SplitOnWord;
        Draft.WhisperCpp.NoContext = defaults.NoContext;
        Draft.WhisperCpp.Prompt = defaults.Prompt;
    }
    [RelayCommand]
    void RefreshModels()
    {
        try
        {
            Models = WhisperCppModelLoader.DiscoverDownloadedModels(ModelDirectory);
            SelectedModel = Models.FirstOrDefault(model => model.Model == Draft.WhisperCpp.Model?.Model && model.Quantization == Draft.WhisperCpp.Model?.Quantization);
            if (Models.Count == 0) Message = "No non-empty whisper.cpp model files were found in this directory.";
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { Models = []; Message = e.Message; }
    }
    [RelayCommand]
    async Task SaveAsync()
    {
        Busy = true;
        try
        {
            // Validate all three before changing the detached numeric draft or attempting persistence.
            int? threads = ParseInteger(ThreadsText, "Threads");
            int? segmentLength = ParseInteger(MaxSegmentLengthText, "MaxSegmentLength (characters)");
            int? tokens = ParseInteger(MaxTokensPerSegmentText, "MaxTokensPerSegment (subword pieces)");
            Draft.WhisperCpp.Threads = threads;
            Draft.WhisperCpp.MaxSegmentLength = segmentLength;
            Draft.WhisperCpp.MaxTokensPerSegment = tokens;
            Draft.WhisperCpp.ModelDirectory = ModelDirectory == WhisperConfig.ModelsDirectory ? null : ModelDirectory;
            Draft.WhisperCpp.Model = SelectedModel;
            await actions.SaveAndApplyAsync(Draft, store);
            Busy = false;
            Saved?.Invoke();
        }
        catch (Exception e) { Message = e.Message; }
        finally { Busy = false; }
    }
}
