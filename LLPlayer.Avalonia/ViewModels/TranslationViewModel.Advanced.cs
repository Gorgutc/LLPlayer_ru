using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using System.Globalization;

namespace LLPlayer.Avalonia.ViewModels;

public partial class TranslationViewModel
{
    [ObservableProperty] string temperatureText = "";
    [ObservableProperty] string topPText = "";
    [ObservableProperty] string frequencyPenaltyText = "";
    [ObservableProperty] string presencePenaltyText = "";
    [ObservableProperty] string maxTokensText = "";
    [ObservableProperty] string maxCompletionTokensText = "";
    [ObservableProperty] bool temperatureManual;
    [ObservableProperty] bool topPManual;
    [ObservableProperty] bool frequencyPenaltyManual;
    [ObservableProperty] bool presencePenaltyManual;
    [ObservableProperty] string chineseRegion = "";
    [ObservableProperty] string frenchRegion = "";
    [ObservableProperty] string portugueseRegion = "";

    public bool HasRegionMap => selected is GoogleV1TranslateSettings or MicrosoftTranslateSettings;
    public string ModelRequirement => selected is OpenAIBaseTranslateSettings { ModelRequired: true } ? "Model (required for model requests)" : "Model (optional)";
    public string ProviderWiki => Enum.TryParse<TranslateServiceType>(EditingProvider, out var type) && Enum.IsDefined(type)
        ? $"https://github.com/umlx5h/LLPlayer/wiki/Translation-Engine#{(type switch { TranslateServiceType.GoogleV1 => "google-v1", TranslateServiceType.LMStudio => "lm-studio", TranslateServiceType.OpenAILike => "openai-like", _ => type.ToString().ToLowerInvariant() })}" : "https://github.com/umlx5h/LLPlayer/wiki/Translation-Engine";
    public string TargetAdvisory => Enum.TryParse<TranslateServiceType>(EditingProvider, out var type) && Enum.IsDefined(type)
        && !TargetLanguage.SupportedServiceType().HasFlag(type == TranslateServiceType.DeepLX ? TranslateServiceType.DeepL : type) ? "This provider does not list support for the selected target language." : "";
    public string RegionHelp => selected is GoogleV1TranslateSettings
        ? "Chinese: zh-CN / zh-TW; French: fr-FR / fr-CA; Portuguese: pt-BR / pt-PT. Custom codes are retained."
        : "Chinese: zh-Hans / zh-Hant; French: fr / fr-ca; Portuguese: pt / pt-pt. Custom codes are retained.";

    void LoadAdvanced()
    {
        var llm = selected as OpenAIBaseTranslateSettings;
        TemperatureText = Format(llm?.Temperature); TopPText = Format(llm?.TopP);
        FrequencyPenaltyText = Format(llm?.FrequencyPenalty); PresencePenaltyText = Format(llm?.PresencePenalty);
        TemperatureManual = llm?.TemperatureManual ?? false; TopPManual = llm?.TopPManual ?? false;
        FrequencyPenaltyManual = llm?.FrequencyPenaltyManual ?? false; PresencePenaltyManual = llm?.PresencePenaltyManual ?? false;
        MaxTokensText = llm?.MaxTokens?.ToString(CultureInfo.InvariantCulture) ?? "";
        MaxCompletionTokensText = llm?.MaxCompletionTokens?.ToString(CultureInfo.InvariantCulture) ?? "";
        var regions = RegionMap();
        var defaults = selected switch { GoogleV1TranslateSettings => new GoogleV1TranslateSettings().Regions, MicrosoftTranslateSettings => new BingTranslateSettings().Regions, _ => null };
        ChineseRegion = regions?.GetValueOrDefault("zh") ?? defaults?.GetValueOrDefault("zh") ?? "";
        FrenchRegion = regions?.GetValueOrDefault("fr") ?? defaults?.GetValueOrDefault("fr") ?? "";
        PortugueseRegion = regions?.GetValueOrDefault("pt") ?? defaults?.GetValueOrDefault("pt") ?? "";
        AvailableModels.Clear(); SelectedAvailableModel = null;
        foreach (string name in new[] { nameof(HasRegionMap), nameof(ModelRequirement), nameof(ProviderWiki), nameof(TargetAdvisory), nameof(RegionHelp) }) OnPropertyChanged(name);
    }

    static string Format(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
    Dictionary<string, string>? RegionMap() => selected switch
    {
        GoogleV1TranslateSettings s => s.Regions,
        MicrosoftTranslateSettings s => s.Regions,
        _ => null
    };

    void CaptureAdvanced()
    {
        if (selected is OpenAIBaseTranslateSettings s)
        {
            // Parse all raw values before any assignment: engine setters deliberately ignore/coerce invalid values.
            double temperature = ParseSampling(TemperatureText, "Temperature", 0, 2), topP = ParseSampling(TopPText, "top_p", 0, 1);
            double frequency = ParseSampling(FrequencyPenaltyText, "Frequency penalty", -2, 2), presence = ParseSampling(PresencePenaltyText, "Presence penalty", -2, 2);
            int? max = ParseTokenCap(MaxTokensText, "max_tokens"), completion = ParseTokenCap(MaxCompletionTokensText, "max_completion_tokens");
            s.Temperature = temperature; s.TopP = topP; s.FrequencyPenalty = frequency; s.PresencePenalty = presence;
            s.TemperatureManual = TemperatureManual; s.TopPManual = TopPManual;
            s.FrequencyPenaltyManual = FrequencyPenaltyManual; s.PresencePenaltyManual = PresencePenaltyManual;
            s.MaxTokens = max; s.MaxCompletionTokens = completion;
        }
        if (RegionMap() is { } map)
        {
            string chinese = ParseRegion(ChineseRegion), french = ParseRegion(FrenchRegion), portuguese = ParseRegion(PortugueseRegion);
            map["zh"] = chinese; map["fr"] = french; map["pt"] = portuguese;
        }
    }
    static double ParseSampling(string raw, string label, double min, double max) =>
        double.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value >= min && value <= max
            ? value : throw new ArgumentException($"{label}: enter a decimal from {min} to {max} using a dot.");
    static int? ParseTokenCap(string raw, string label) => string.IsNullOrWhiteSpace(raw) ? null :
        int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value : throw new ArgumentException($"{label}: enter a positive whole number up to 2147483647, or leave blank.");
    static string ParseRegion(string raw) => !string.IsNullOrWhiteSpace(raw) && raw.Length <= 100 && raw.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        ? raw : throw new ArgumentException("Region mappings require a nonempty language code containing letters, digits, hyphens or underscores.");

    [RelayCommand]
    void ResetEndpoint()
    {
        if (Busy || !Enum.TryParse<TranslateServiceType>(EditingProvider, out var type) || !Enum.IsDefined(type)) return;
        Endpoint = type.DefaultSettings() switch
        {
            GoogleV1TranslateSettings s => s.Endpoint, MicrosoftTranslateSettings s => s.Endpoint,
            DeepLXTranslateSettings s => s.Endpoint, OpenAIBaseTranslateSettings s => s.Endpoint, _ => Endpoint
        };
    }
    [RelayCommand] void ResetChatPath() { if (!Busy && HasChatPath) ChatPath = new OpenAILikeTranslateSettings().ChatPath; }
}
