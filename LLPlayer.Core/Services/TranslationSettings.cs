using System.Text.Json;
using System.Text.Json.Serialization;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Extensions;

namespace LLPlayer.Core.Services;

/// <summary>Detached word-translation preferences, including advanced engine parameters not exposed by the UI.</summary>
public sealed class TranslationSettings
{
    public FlyleafLib.MediaPlayer.AI.WordDefinitionServiceType DictionaryMode { get; set; } = FlyleafLib.MediaPlayer.AI.WordDefinitionServiceType.Off;
    public string SentenceProvider { get; set; } = "Off";
    public string WordProvider { get; set; } = nameof(TranslateServiceType.GoogleV1);
    public TargetLanguage TargetLanguage { get; set; } = new Config(true).Subtitles.TranslateTargetLanguage;
    public Dictionary<TranslateServiceType, ITranslateSettings> Providers { get; set; } = [];
    public TranslateChatConfig Chat { get; set; } = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new JsonInterfaceConcreteConverter<ITranslateSettings>(
            Enum.GetValues<TranslateServiceType>().Select(type => type.DefaultSettings().GetType())
                .ToDictionary(type => type.Name, type => type)) }
    };
    public TranslationSettings Clone() => JsonSerializer.Deserialize<TranslationSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
    public void Validate()
    {
        if (WordProvider != "Off" && (!Enum.TryParse<TranslateServiceType>(WordProvider, out var selectedType) || !Enum.IsDefined(selectedType)))
            throw new ArgumentException("Choose a known word translation provider or Off.");
        if (SentenceProvider != "Off" && (!Enum.TryParse<TranslateServiceType>(SentenceProvider, out var sentenceType) || !Enum.IsDefined(sentenceType)))
            throw new ArgumentException("Choose a known sentence translation provider or Off.");
        if (!Enum.IsDefined(DictionaryMode) || !Enum.IsDefined(TargetLanguage) || Providers == null || Chat == null)
            throw new ArgumentException("Translation settings are incomplete.");
        if (!Enum.IsDefined(Chat.TranslateMethod) || !Enum.IsDefined(Chat.ContextRetainPolicy))
            throw new ArgumentException("Choose a known translation chat mode.");
        if (Chat.SubtitleContextCount < 0 || Chat.ContextWindowBefore < 0 || Chat.ContextWindowAfter < 0)
            throw new ArgumentException("Subtitle context counts must be nonnegative whole numbers.");
        foreach (var (type, value) in Providers)
        {
            if (!Enum.IsDefined(type) || value == null || value.GetType() != type.DefaultSettings().GetType())
                throw new ArgumentException("A provider configuration has an incompatible type.");
            if (value is GoogleV1TranslateSettings { Regions: null } or MicrosoftTranslateSettings { Regions: null })
                throw new ArgumentException("Provider region mappings are incomplete.");
            string? endpoint = value switch
            {
                GoogleV1TranslateSettings s => s.Endpoint,
                MicrosoftTranslateSettings s => s.Endpoint,
                DeepLXTranslateSettings s => s.Endpoint,
                OpenAIBaseTranslateSettings s => s.Endpoint,
                _ => null
            };
            if (!string.IsNullOrEmpty(endpoint) && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)))
                throw new ArgumentException("A provider endpoint must be an absolute HTTP or HTTPS address.");
            int timeout = value switch
            {
                GoogleV1TranslateSettings s => s.TimeoutMs,
                MicrosoftTranslateSettings s => s.TimeoutMs,
                DeepLTranslateSettings s => s.TimeoutMs,
                DeepLXTranslateSettings s => s.TimeoutMs,
                OpenAIBaseTranslateSettings s => s.TimeoutMs,
                _ => 0
            };
            if (timeout <= 0 || value is OpenAIBaseTranslateSettings { TimeoutHealthMs: <= 0 })
                throw new ArgumentException("Request timeouts must be positive milliseconds.");
        }
    }
    public void Apply(Config.SubtitlesConfig target)
    {
        Validate();
        var copy = Clone();
        target.WordDefinitionServiceType = copy.DictionaryMode;
        target.TranslateTargetLanguage = copy.TargetLanguage;
        target.TranslateServiceSettings = copy.Providers;
        target.TranslateChatConfig = copy.Chat;
        if (Enum.TryParse<TranslateServiceType>(copy.SentenceProvider, out var sentenceProvider)) target.TranslateServiceType = sentenceProvider;
        if (Enum.TryParse<TranslateServiceType>(copy.WordProvider, out var provider)) target.TranslateWordServiceType = provider;
    }
}

public sealed class TranslationSettingsStore(string path)
{
    public string? LoadWarning { get; private set; }
    public TranslationSettings Load(string legacyProvider, string? legacyTarget)
    {
        LoadWarning = null;
        try
        {
            // GetAttributes distinguishes an absent file from denied access; File.Exists would hide read errors.
            try { File.GetAttributes(path); }
            catch (FileNotFoundException) { return Seed(legacyProvider, legacyTarget); }
            catch (DirectoryNotFoundException) { return Seed(legacyProvider, legacyTarget); }
            var settings = JsonSerializer.Deserialize<TranslationSettings>(File.ReadAllText(path), TranslationSettings.JsonOptions)
                ?? throw new JsonException();
            settings.Validate();
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Never include JSON values or exception text: the input may contain credentials.
            LoadWarning = "Translation settings could not be read. Defaults are in use; the original file was preserved. Save explicitly to replace it.";
            return new();
        }
    }
    static TranslationSettings Seed(string provider, string? target)
    {
        var settings = new TranslationSettings();
        if (string.Equals(provider, "Off", StringComparison.OrdinalIgnoreCase)) settings.WordProvider = "Off";
        else if (Enum.TryParse<TranslateServiceType>(provider, true, out var type) && Enum.IsDefined(type)) settings.WordProvider = type.ToString();
        if (Enum.TryParse<TargetLanguage>(target, true, out var language) && Enum.IsDefined(language)) settings.TargetLanguage = language;
        return settings;
    }
    public void Save(TranslationSettings settings)
    {
        settings.Validate();
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        AtomicFile.WriteAllText(full, JsonSerializer.Serialize(settings, TranslationSettings.JsonOptions));
    }
}
