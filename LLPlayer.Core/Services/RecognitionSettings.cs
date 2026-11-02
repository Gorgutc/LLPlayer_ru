using System.Text.Json;
using System.Text.Json.Serialization;
using FlyleafLib;

namespace LLPlayer.Core.Services;

/// <summary>Recognition-only preferences; never serializes a live player configuration.</summary>
public sealed class RecognitionSettings
{
    public SubASREngineType ASREngine { get; set; } = SubASREngineType.WhisperCpp;
    public WhisperConfig Whisper { get; set; } = new();
    public WhisperCppConfig WhisperCpp { get; set; } = new();
    public FasterWhisperConfig FasterWhisper { get; set; } = new();
    public int ASRChunkSizeMB { get; set; } = 20;
    public int ASRChunkSeconds { get; set; } = 20;
    public SubOCREngineType OcrPrimaryEngine { get; set; } = SubOCREngineType.Tesseract;
    public SubOCREngineType OcrSecondaryEngine { get; set; } = SubOCREngineType.Tesseract;
    public Dictionary<string, string> TesseractOcrRegions { get; set; } = new();
    public Dictionary<string, string> MsOcrRegions { get; set; } = new();
    public string? OcrPrimaryLanguage { get; set; }
    public string? OcrSecondaryLanguage { get; set; }
    public bool OcrSecondarySame { get; set; } = true;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public RecognitionSettings Clone() => JsonSerializer.Deserialize<RecognitionSettings>(
        JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public static RecognitionSettings FromConfig(Config.SubtitlesConfig source) => new()
    {
        ASREngine = source.ASREngine,
        Whisper = new WhisperConfig { Language = source.WhisperConfig.Language,
            LanguageDetection = source.WhisperConfig.LanguageDetection, Translate = source.WhisperConfig.Translate },
        WhisperCpp = JsonSerializer.Deserialize<WhisperCppConfig>(JsonSerializer.Serialize(source.WhisperCppConfig, JsonOptions), JsonOptions)!,
        FasterWhisper = JsonSerializer.Deserialize<FasterWhisperConfig>(JsonSerializer.Serialize(source.FasterWhisperConfig, JsonOptions), JsonOptions)!,
        ASRChunkSizeMB = source.ASRChunkSizeMB,
        ASRChunkSeconds = source.ASRChunkSeconds,
        OcrPrimaryEngine = source[0].OCREngine,
        OcrSecondaryEngine = source[1].OCREngine,
        TesseractOcrRegions = new(source.TesseractOcrRegions),
        MsOcrRegions = new(source.MsOcrRegions),
        OcrPrimaryLanguage = source.LanguageFallbackPrimary?.ISO6391,
        OcrSecondaryLanguage = source.LanguageFallbackSecondary?.ISO6391,
        OcrSecondarySame = source.LanguageFallbackSecondarySame
    };

    public void Validate()
    {
        if (Whisper == null || WhisperCpp == null || FasterWhisper == null || TesseractOcrRegions == null || MsOcrRegions == null)
            throw new ArgumentException("Recognition configuration is incomplete.");
        if (!Enum.IsDefined(ASREngine) || !Enum.IsDefined(OcrPrimaryEngine) || !Enum.IsDefined(OcrSecondaryEngine))
            throw new ArgumentException("Unknown recognition engine.");
        if (ASRChunkSizeMB <= 0 || ASRChunkSeconds <= 0)
            throw new ArgumentException("ASR chunk size and seconds must be positive whole numbers.");
        if (!Enum.IsDefined(FasterWhisper.ProcessPriority))
            throw new ArgumentException("Unknown faster-whisper process priority.");
        if (string.IsNullOrWhiteSpace(FasterWhisper.Model))
            throw new ArgumentException("Select a faster-whisper model.");
        if (FasterWhisper.UseManualEngine && (!Path.IsPathFullyQualified(FasterWhisper.ManualEnginePath ?? "") || !File.Exists(FasterWhisper.ManualEnginePath)))
            throw new ArgumentException("Choose an existing absolute faster-whisper executable path.");
        if (FasterWhisper.UseManualEngine && !OperatingSystem.IsWindows() &&
            (File.GetUnixFileMode(FasterWhisper.ManualEnginePath!) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
            throw new ArgumentException("The faster-whisper executable needs execute permission (chmod +x).");
        if (FasterWhisper.UseManualModel && (!Path.IsPathFullyQualified(FasterWhisper.ManualModelDir ?? "") || !Directory.Exists(FasterWhisper.ManualModelDir)))
            throw new ArgumentException("Choose an existing absolute faster-whisper model directory.");
        CheckFloat(WhisperCpp.NoSpeechThreshold, 0, 1, "NoSpeechThreshold");
        CheckFloat(WhisperCpp.Temperature, 0, float.MaxValue, "Temperature");
        CheckFloat(WhisperCpp.TemperatureInc, 0, float.MaxValue, "TemperatureInc");
        CheckFloat(WhisperCpp.EntropyThreshold, 0, float.MaxValue, "EntropyThreshold");
        CheckFloat(WhisperCpp.LogProbThreshold, -float.MaxValue, 0, "LogProbThreshold");
        if (WhisperCpp.AudioContextSize is <= 0 || WhisperCpp.GpuDevice is < 0)
            throw new ArgumentException("Audio context must be positive; GPU device must be a zero-based nonnegative index.");
        foreach (var pair in TesseractOcrRegions.Concat(MsOcrRegions))
            if (!IsKnownLanguage(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Any(char.IsControl) || pair.Value.IndexOfAny(['/', '\\']) >= 0)
                throw new ArgumentException("OCR regions must map a known language to a language code, without path characters.");
        foreach (var pair in TesseractOcrRegions)
            if (!TesseractModel.TesseractLangToISO6391.Any(model => model.Value == pair.Key && new TesseractModel { Lang = model.Key }.LangCode == pair.Value))
                throw new ArgumentException($"Tesseract region '{pair.Value}' does not belong to language '{pair.Key}'.");
        foreach (var pair in MsOcrRegions)
            if (!string.Equals(pair.Key, pair.Value.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Microsoft OCR region '{pair.Value}' does not belong to language '{pair.Key}'.");
        if (WhisperCpp.ModelDirectory is { } directory &&
            (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)))
            throw new ArgumentException("Choose an existing absolute model directory.");
        if (WhisperCpp.RuntimeLibraries == null || WhisperCpp.RuntimeLibraries.Any(runtime => !Enum.IsDefined(runtime)))
            throw new ArgumentException("Unknown or missing whisper.cpp runtime libraries.");
        if (WhisperCpp.Model is { } model && (!Enum.IsDefined(model.Model) || !Enum.IsDefined(model.Quantization)))
            throw new ArgumentException("Unknown whisper.cpp model or quantization.");
        if (!Whisper.LanguageDetection && !IsKnownLanguage(Whisper.Language))
            throw new ArgumentException("Choose a valid recognition language code, or enable Auto language.");
        foreach (string? language in new[] { OcrPrimaryLanguage, OcrSecondaryLanguage })
            if (language != null && !IsKnownLanguage(language))
                throw new ArgumentException("OCR fallback must be a valid language code (for example en or ru).");
    }

    static void CheckFloat(float? value, float minimum, float maximum, string name)
    {
        if (value.HasValue && (!float.IsFinite(value.Value) || value < minimum || value > maximum))
            throw new ArgumentException($"{name} must be finite and between {minimum} and {maximum}.");
    }

    static bool IsKnownLanguage(string? code) => !string.IsNullOrWhiteSpace(code) &&
        Language.AllLanguages.Any(language => language != Language.Unknown &&
            (string.Equals(language.ISO6391, code, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(language.IdSubLanguage, code, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(language.CultureName, code, StringComparison.OrdinalIgnoreCase)));

    public void Apply(Config.SubtitlesConfig target)
    {
        RecognitionSettings copy = Clone();
        target.ASREngine = copy.ASREngine;
        target.WhisperConfig = copy.Whisper;
        target.WhisperCppConfig = copy.WhisperCpp;
        target.FasterWhisperConfig = copy.FasterWhisper;
        target.ASRChunkSizeMB = copy.ASRChunkSizeMB;
        target.ASRChunkSeconds = copy.ASRChunkSeconds;
        target[0].OCREngine = copy.OcrPrimaryEngine;
        target[1].OCREngine = copy.OcrSecondaryEngine;
        target.TesseractOcrRegions = copy.TesseractOcrRegions;
        target.MsOcrRegions = copy.MsOcrRegions;
        if (copy.OcrPrimaryLanguage != null) target.LanguageFallbackPrimary = Language.Get(copy.OcrPrimaryLanguage);
        if (copy.OcrSecondaryLanguage != null) target.LanguageFallbackSecondary = Language.Get(copy.OcrSecondaryLanguage);
        target.LanguageFallbackSecondarySame = copy.OcrSecondarySame;
    }
}

public sealed class RecognitionSettingsStore(string path)
{
    public string? LoadWarning { get; private set; }
    public RecognitionSettings? Load()
    {
        LoadWarning = null;
        if (!File.Exists(path)) return null;
        try
        {
            RecognitionSettings settings = JsonSerializer.Deserialize<RecognitionSettings>(File.ReadAllText(path), RecognitionSettings.JsonOptions)
                ?? throw new JsonException("Empty recognition settings.");
            settings.Validate();
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            LoadWarning = $"Recognition settings could not be loaded: {e.Message} The original file was preserved.";
            return null;
        }
    }

    /// <summary>Same-directory replacement: failures never apply settings or truncate the original.</summary>
    public void Save(RecognitionSettings settings)
    {
        settings.Validate();
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        AtomicFile.WriteAllText(fullPath, JsonSerializer.Serialize(settings, RecognitionSettings.JsonOptions));
    }
}
