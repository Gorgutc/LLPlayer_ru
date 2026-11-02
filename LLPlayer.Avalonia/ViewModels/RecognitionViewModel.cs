using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib;
using LLPlayer.Core.Services;
using LLPlayer.Core.Services.Models;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using FlyleafLib.MediaPlayer;
using Whisper.net.LibraryLoader;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>A detached recognition draft. Dismissal never applies or saves it.</summary>
public partial class RecognitionViewModel : ObservableObject
{
    readonly RecognitionActions actions;
    readonly RecognitionSettingsStore store;
    readonly Func<RecognitionSettings, Task>? save;
    readonly Action<string>? copyText;
    readonly Func<Task>? manageModels;
    readonly Func<ModelKind, Task>? manageResource;
    readonly Action<string>? openFolder;
    readonly Func<IReadOnlyList<string>>? microsoftLanguages;
    readonly string tesseractDirectory;
    readonly Func<IReadOnlyList<string>>? tesseractDirectories;
    public bool CanEditFallbackLanguages { get; }
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
    [ObservableProperty] string noSpeechThresholdText;
    [ObservableProperty] string audioContextSizeText;
    [ObservableProperty] string temperatureText;
    [ObservableProperty] string temperatureIncText;
    [ObservableProperty] string entropyThresholdText;
    [ObservableProperty] string logProbThresholdText;
    [ObservableProperty] string gpuDeviceText;
    [ObservableProperty] string asrChunkSizeText;
    [ObservableProperty] string asrChunkSecondsText;
    [ObservableProperty] RuntimeLibrary? selectedRuntime;
    [ObservableProperty] RuntimeLibrary? availableRuntime;
    [ObservableProperty] string? selectedPreset;
    [ObservableProperty] string? selectedPromptPreset;
    [ObservableProperty] SubASREngineType selectedAsrEngine;
    [ObservableProperty] WhisperLanguage? selectedLanguage;
    [ObservableProperty] string commandPreview = "";
    [ObservableProperty] string ocrStatus = "";
    [ObservableProperty] IReadOnlyList<OcrInstalledLanguage> tesseractLanguages = [];
    [ObservableProperty] IReadOnlyList<string> microsoftLanguageTags = [];
    [ObservableProperty] IReadOnlyList<OcrRegionPreference> tesseractRegions = [];
    [ObservableProperty] IReadOnlyList<OcrRegionPreference> microsoftRegions = [];
    public IReadOnlyList<SubASREngineType> AsrEngines { get; } = Enum.GetValues<SubASREngineType>();
    public IReadOnlyList<SubOCREngineType> OcrEngines { get; } = Enum.GetValues<SubOCREngineType>();
    public IReadOnlyList<ProcessPriorityClass> ProcessPriorities { get; } = Enum.GetValues<ProcessPriorityClass>();
    public IReadOnlyList<string> FasterModels => FasterWhisperConfig.ModelOptions;
    public IReadOnlyList<WhisperLanguage> Languages { get; } = WhisperLanguage.GetWhisperLanguages();
    public ObservableCollection<RuntimeLibrary> RuntimeLibraries { get; } = [];
    public IReadOnlyList<RuntimeLibrary> AvailableRuntimes { get; } = Enum.GetValues<RuntimeLibrary>().Where(runtime => runtime != RuntimeLibrary.CoreML).ToArray();
    public bool CanManageModels => manageModels != null || manageResource != null;
    public bool IsWhisperCppSelected => SelectedAsrEngine == SubASREngineType.WhisperCpp;
    public bool IsFasterWhisperSelected => SelectedAsrEngine == SubASREngineType.FasterWhisper;
    public string LoadedRuntime => Draft.WhisperCpp.LoadedLibrary?.ToString() ?? "Not loaded";
    public string RuntimeAvailability => "This package includes the CPU runtime. CPU-no-AVX requires a separately installed compatible runtime. GPU runtimes/devices are not supplied by this build; saved preferences are retained. Loaded library cannot change until process restart.";
    public string FasterInstalledStatus => File.Exists(Draft.FasterWhisper.UseManualEngine ? Draft.FasterWhisper.ManualEnginePath : FasterWhisperConfig.DefaultEnginePath)
        ? "Executable present (compatibility is checked when recognition starts)." : "Executable missing. Install an engine or select a manual path before starting faster-whisper.";
    public string EnglishOnlyWarning => "English-only models force English and ignore automatic language / translation options.";
    public IReadOnlyDictionary<string, string> ExtraArgumentsPresets { get; } = new Dictionary<string, string>
    {
        ["Anti-repetition (CPU)"] = "--device cpu --condition_on_previous_text False --no_repeat_ngram_size 3",
        ["Use CPU device"] = "--device cpu",
        ["CUDA RTX 50XX — float16 (external engine required)"] = "--device cuda --compute_type float16",
        ["CUDA RTX 50XX — float32 (external engine required)"] = "--device cuda --compute_type float32",
        ["Use CUDA device (external engine required)"] = "--device cuda",
        ["Use CUDA second device (external engine required)"] = "--device cuda:1",
        ["Chinese Simplified, with punctuation"] = "--initial_prompt \"以下是普通话的句子。\"",
        ["Chinese Traditional, with punctuation"] = "--initial_prompt \"以下是普通話的句子。\""
    };
    public IEnumerable<string> PresetNames => ExtraArgumentsPresets.Keys;
    public IReadOnlyDictionary<string, string> PromptPresets { get; } = new Dictionary<string, string>
    {
        ["Chinese Simplified, with punctuation"] = "以下是普通话的句子。",
        ["Chinese Traditional, with punctuation"] = "以下是普通話的句子。"
    };
    public IEnumerable<string> PromptPresetNames => PromptPresets.Keys;

    public RecognitionViewModel(RecognitionSettings settings, RecognitionActions actions, RecognitionSettingsStore store,
        string? warning = null, Func<RecognitionSettings, Task>? save = null, bool fallbackLanguagesEditable = true,
        Action<string>? copyText = null, Func<Task>? manageModels = null,
        Func<IReadOnlyList<string>>? microsoftLanguages = null, string? tesseractDirectory = null,
        Func<ModelKind, Task>? manageResource = null, Action<string>? openFolder = null,
        Func<IReadOnlyList<string>>? tesseractDirectories = null)
    {
        Draft = settings.Clone();
        selectedAsrEngine = Draft.ASREngine;
        this.actions = actions;
        this.store = store;
        this.save = save;
        this.copyText = copyText;
        this.manageModels = manageModels;
        this.manageResource = manageResource;
        this.openFolder = openFolder;
        this.tesseractDirectories = tesseractDirectories;
        this.microsoftLanguages = microsoftLanguages;
        this.tesseractDirectory = tesseractDirectory ?? Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ?? TesseractModel.ModelsDirectory;
        CanEditFallbackLanguages = fallbackLanguagesEditable;
        modelDirectory = Draft.WhisperCpp.ModelDirectory ?? WhisperConfig.ModelsDirectory;
        selectedModel = Draft.WhisperCpp.Model;
        threadsText = DisplayInteger(Draft.WhisperCpp.Threads);
        maxSegmentLengthText = DisplayInteger(Draft.WhisperCpp.MaxSegmentLength);
        maxTokensPerSegmentText = DisplayInteger(Draft.WhisperCpp.MaxTokensPerSegment);
        noSpeechThresholdText = DisplayFloat(Draft.WhisperCpp.NoSpeechThreshold);
        audioContextSizeText = DisplayInteger(Draft.WhisperCpp.AudioContextSize);
        temperatureText = DisplayFloat(Draft.WhisperCpp.Temperature);
        temperatureIncText = DisplayFloat(Draft.WhisperCpp.TemperatureInc);
        entropyThresholdText = DisplayFloat(Draft.WhisperCpp.EntropyThreshold);
        logProbThresholdText = DisplayFloat(Draft.WhisperCpp.LogProbThreshold);
        gpuDeviceText = Draft.WhisperCpp.GpuDevice?.ToString(CultureInfo.InvariantCulture) ?? "";
        asrChunkSizeText = Draft.ASRChunkSizeMB.ToString(CultureInfo.InvariantCulture);
        asrChunkSecondsText = Draft.ASRChunkSeconds.ToString(CultureInfo.InvariantCulture);
        foreach (var runtime in Draft.WhisperCpp.RuntimeLibraries) RuntimeLibraries.Add(runtime);
        selectedLanguage = Languages.FirstOrDefault(language => language.Code == Draft.Whisper.Language);
        Warning = warning;
        message = "Choose existing whisper.cpp CPU weights. Models are never downloaded automatically.";
        Draft.FasterWhisper.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FasterInstalledStatus));
        RefreshModels();
        RefreshOcrLanguages();
    }
    static string DisplayInteger(int? value) => value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
    static string DisplayFloat(float? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
    partial void OnSelectedLanguageChanged(WhisperLanguage? value) { if (value != null) Draft.Whisper.Language = value.Code; }
    partial void OnSelectedAsrEngineChanged(SubASREngineType value)
    {
        Draft.ASREngine = value;
        OnPropertyChanged(nameof(IsWhisperCppSelected)); OnPropertyChanged(nameof(IsFasterWhisperSelected));
    }
    partial void OnSelectedPresetChanged(string? value)
    {
        if (value != null && ExtraArgumentsPresets.TryGetValue(value, out string? arguments)) Draft.FasterWhisper.ExtraArguments = arguments;
    }
    partial void OnSelectedPromptPresetChanged(string? value)
    {
        if (value != null && PromptPresets.TryGetValue(value, out string? prompt)) Draft.WhisperCpp.Prompt = prompt;
    }
    static float? ParseFloat(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new ArgumentException($"{label}: enter a finite decimal number, or leave empty for the library default.");
        return value;
    }
    static int? ParseDevice(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
            throw new ArgumentException("GPU device: enter a nonnegative device index, or leave empty for the default. GPU execution is unavailable in this CPU package.");
        return value;
    }
    [RelayCommand] void AddRuntime()
    {
        if (AvailableRuntime is { } value && !RuntimeLibraries.Contains(value)) RuntimeLibraries.Add(value);
    }
    [RelayCommand] void RemoveRuntime() { if (SelectedRuntime is { } value) RuntimeLibraries.Remove(value); }
    [RelayCommand] void MoveRuntimeUp() => MoveRuntime(-1);
    [RelayCommand] void MoveRuntimeDown() => MoveRuntime(1);
    void MoveRuntime(int delta)
    {
        if (SelectedRuntime is not { } value) return;
        int index = RuntimeLibraries.IndexOf(value), next = index + delta;
        if (index >= 0 && next >= 0 && next < RuntimeLibraries.Count) RuntimeLibraries.Move(index, next);
        SelectedRuntime = value;
    }
    [RelayCommand] void CopyHelp()
    {
        try
        {
            var command = FasterWhisperASRService.BuildCommand(Draft.FasterWhisper, Draft.Whisper).WithArguments("--help");
            CommandPreview = command.CommandToText(); copyText?.Invoke(CommandPreview);
        }
        catch (Exception e) { Message = e.Message; }
    }
    [RelayCommand] void CopyDebug()
    {
        try
        {
            var command = FasterWhisperASRService.BuildCommand(Draft.FasterWhisper, Draft.Whisper);
            CommandPreview = command.WithArguments(command.Arguments + " \"audio.wav\"").CommandToText(); copyText?.Invoke(CommandPreview);
        }
        catch (Exception e) { Message = e.Message; }
    }
    [RelayCommand] async Task ManageModelsAsync(ModelKind kind)
    {
        if (Busy || !CanManageModels) return;
        Busy = true;
        try
        {
            if (manageResource != null) await manageResource(kind);
            else if (manageModels != null) await manageModels();
            RefreshModels(); RefreshOcrLanguages(); OnPropertyChanged(nameof(FasterInstalledStatus));
        }
        catch (Exception e) { Message = e.Message; }
        finally { Busy = false; }
    }
    [RelayCommand] void OpenModelFolder()
    {
        try
        {
            string? directory = Draft.FasterWhisper.UseManualModel ? Draft.FasterWhisper.ManualModelDir : ModelDirectory;
            if (directory == null || !Directory.Exists(directory)) throw new DirectoryNotFoundException("The model directory does not exist.");
            if (openFolder == null) throw new InvalidOperationException("Opening a folder is unavailable in this host.");
            openFolder(directory);
        }
        catch (Exception e) { Message = e.Message; }
    }
    [RelayCommand] void RefreshOcrLanguages()
    {
        try
        {
            // Read only. Discovery never creates model storage or initializes a native OCR engine.
            var directories = tesseractDirectories?.Invoke() ?? [tesseractDirectory];
            TesseractLanguages = directories.Where(Directory.Exists).SelectMany(directory => TesseractModel.TesseractLangToISO6391.Keys
                    .Select(language => new TesseractModel { Lang = language })
                    .Select(model => new { Model = model, Path = Path.Combine(directory, model.ModelFileName) }))
                .Where(item => File.Exists(item.Path) && new FileInfo(item.Path).Length > 0)
                .DistinctBy(item => item.Model.LangCode, StringComparer.Ordinal)
                .Select(item => new OcrInstalledLanguage(item.Model.ISO6391, item.Model.LangCode, new FileInfo(item.Path).Length)).ToArray();
            var tessPreferences = new Dictionary<string, string>(Draft.TesseractOcrRegions);
            foreach (var region in TesseractRegions)
                if (region.Selected != null) tessPreferences[region.LanguageCode] = region.Selected;
                else tessPreferences.Remove(region.LanguageCode);
            TesseractRegions = BuildRegions(TesseractLanguages.Select(language => (language.LanguageCode, language.Code)), tessPreferences);
            MicrosoftLanguageTags = microsoftLanguages?.Invoke() ?? [];
            var microsoftPreferences = new Dictionary<string, string>(Draft.MsOcrRegions);
            foreach (var region in MicrosoftRegions)
                if (region.Selected != null) microsoftPreferences[region.LanguageCode] = region.Selected;
                else microsoftPreferences.Remove(region.LanguageCode);
            MicrosoftRegions = BuildRegions(MicrosoftLanguageTags.Select(tag => (tag.Split('-')[0], tag)), microsoftPreferences);
            OcrStatus = !OperatingSystem.IsWindows() ? "Microsoft OCR is unavailable on Linux. Tesseract uses installed language data."
                : MicrosoftLanguageTags.Count == 0 ? "No Microsoft OCR language packs are available. Install the Windows OCR language feature; this desktop environment may require package identity." : "Microsoft OCR uses installed Windows language packs.";
        }
        catch (Exception e) { OcrStatus = $"OCR language discovery failed: {e.Message}"; }
    }
    static IReadOnlyList<OcrRegionPreference> BuildRegions(IEnumerable<(string Language, string Code)> source, Dictionary<string, string> preferences)
        => source.GroupBy(item => item.Language, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new OcrRegionPreference(group.Key, group.Select(item => item.Code).Distinct(StringComparer.Ordinal).ToArray(),
                preferences.TryGetValue(group.Key, out string? selected) ? selected : null)).ToArray();

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
            var previous = SelectedModel ?? Draft.WhisperCpp.Model;
            SelectedModel = Models.FirstOrDefault(model => model.Model == previous?.Model && model.Quantization == previous?.Quantization);
            if (Models.Count == 0) Message = "No non-empty whisper.cpp model files were found in this directory.";
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { Models = []; Message = e.Message; }
    }
    [RelayCommand]
    async Task SaveAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            // Validate all three before changing the detached numeric draft or attempting persistence.
            int? threads = ParseInteger(ThreadsText, "Threads");
            int? segmentLength = ParseInteger(MaxSegmentLengthText, "MaxSegmentLength (characters)");
            int? tokens = ParseInteger(MaxTokensPerSegmentText, "MaxTokensPerSegment (subword pieces)");
            RecognitionSettings candidate = Draft.Clone();
            candidate.WhisperCpp.Threads = threads;
            candidate.WhisperCpp.MaxSegmentLength = segmentLength;
            candidate.WhisperCpp.MaxTokensPerSegment = tokens;
            candidate.WhisperCpp.NoSpeechThreshold = ParseFloat(NoSpeechThresholdText, "NoSpeechThreshold");
            candidate.WhisperCpp.AudioContextSize = ParseInteger(AudioContextSizeText, "AudioContextSize");
            candidate.WhisperCpp.Temperature = ParseFloat(TemperatureText, "Temperature");
            candidate.WhisperCpp.TemperatureInc = ParseFloat(TemperatureIncText, "TemperatureInc");
            candidate.WhisperCpp.EntropyThreshold = ParseFloat(EntropyThresholdText, "EntropyThreshold");
            candidate.WhisperCpp.LogProbThreshold = ParseFloat(LogProbThresholdText, "LogProbThreshold");
            candidate.WhisperCpp.GpuDevice = ParseDevice(GpuDeviceText);
            candidate.WhisperCpp.RuntimeLibraries = [.. RuntimeLibraries];
            candidate.ASRChunkSizeMB = ParseInteger(AsrChunkSizeText, "Chunk size MB") ?? throw new ArgumentException("Chunk size MB is required.");
            candidate.ASRChunkSeconds = ParseInteger(AsrChunkSecondsText, "Chunk seconds") ?? throw new ArgumentException("Chunk seconds is required.");
            candidate.WhisperCpp.ModelDirectory = ModelDirectory == WhisperConfig.ModelsDirectory ? null : ModelDirectory;
            candidate.WhisperCpp.Model = SelectedModel;
            foreach (var region in TesseractRegions)
                if (region.Selected != null) candidate.TesseractOcrRegions[region.LanguageCode] = region.Selected;
                else candidate.TesseractOcrRegions.Remove(region.LanguageCode);
            foreach (var region in MicrosoftRegions)
                if (region.Selected != null) candidate.MsOcrRegions[region.LanguageCode] = region.Selected;
                else candidate.MsOcrRegions.Remove(region.LanguageCode);
            candidate.Validate();
            if (save != null) await save(candidate);
            else await actions.SaveAndApplyAsync(candidate, store);
            Busy = false;
            Saved?.Invoke();
        }
        catch (Exception e) { Message = e.Message; }
        finally { Busy = false; }
    }
}

public sealed record OcrInstalledLanguage(string LanguageCode, string Code, long Size)
{
    public string Display => $"{Code} — {Size:N0} bytes";
}
public partial class OcrRegionPreference : ObservableObject
{
    public string LanguageCode { get; }
    public IReadOnlyList<string> Choices { get; }
    [ObservableProperty] string? selected;
    public OcrRegionPreference(string languageCode, IReadOnlyList<string> choices, string? selected)
    {
        LanguageCode = languageCode;
        Choices = selected != null && !choices.Contains(selected, StringComparer.Ordinal) ? [.. choices, selected] : choices;
        this.selected = selected;
    }
}
