using System.Text.Json;
using System.Text.Json.Serialization;
using FlyleafLib;

namespace LLPlayer.Core.Services;

/// <summary>Recognition-only preferences; never serializes a live player configuration.</summary>
public sealed class RecognitionSettings
{
    public WhisperConfig Whisper { get; set; } = new();
    public WhisperCppConfig WhisperCpp { get; set; } = new();
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
        Whisper = new WhisperConfig { Language = source.WhisperConfig.Language,
            LanguageDetection = source.WhisperConfig.LanguageDetection, Translate = source.WhisperConfig.Translate },
        WhisperCpp = JsonSerializer.Deserialize<WhisperCppConfig>(JsonSerializer.Serialize(source.WhisperCppConfig, JsonOptions), JsonOptions)!,
        OcrPrimaryLanguage = source.LanguageFallbackPrimary?.ISO6391,
        OcrSecondaryLanguage = source.LanguageFallbackSecondary?.ISO6391,
        OcrSecondarySame = source.LanguageFallbackSecondarySame
    };

    public void Validate()
    {
        if (Whisper == null || WhisperCpp == null)
            throw new ArgumentException("Recognition configuration is incomplete.");
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

    static bool IsKnownLanguage(string? code) => !string.IsNullOrWhiteSpace(code) &&
        Language.AllLanguages.Any(language => language != Language.Unknown &&
            (string.Equals(language.ISO6391, code, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(language.IdSubLanguage, code, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(language.CultureName, code, StringComparison.OrdinalIgnoreCase)));

    public void Apply(Config.SubtitlesConfig target)
    {
        RecognitionSettings copy = Clone();
        target.WhisperConfig = copy.Whisper;
        target.WhisperCppConfig = copy.WhisperCpp;
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
