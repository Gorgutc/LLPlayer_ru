using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Core.Services;
using System.Globalization;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>Known provider fields over a detached settings draft; advanced engine settings are retained.</summary>
public partial class TranslationViewModel : ObservableObject
{
    public IReadOnlyList<FlyleafLib.MediaPlayer.AI.WordDefinitionServiceType> DictionaryModes { get; } = Enum.GetValues<FlyleafLib.MediaPlayer.AI.WordDefinitionServiceType>();
    [ObservableProperty] FlyleafLib.MediaPlayer.AI.WordDefinitionServiceType dictionaryMode;
    partial void OnDictionaryModeChanged(FlyleafLib.MediaPlayer.AI.WordDefinitionServiceType value) => Draft.DictionaryMode = value;
    readonly Func<TranslationSettings, Task> save;
    public TranslationSettings Draft { get; }
    public string? Warning { get; }
    public event Action? Saved;
    public IReadOnlyList<string> Providers { get; } = ["Off", .. Enum.GetNames<TranslateServiceType>()];
    public IReadOnlyList<TargetLanguage> TargetLanguages { get; } = Enum.GetValues<TargetLanguage>();
    [ObservableProperty] string provider;
    [ObservableProperty] string sentenceProvider;
    [ObservableProperty] string editingProvider;
    [ObservableProperty] TargetLanguage targetLanguage;
    [ObservableProperty] string endpoint = "";
    [ObservableProperty] string model = "";
    [ObservableProperty] string apiKey = "";
    [ObservableProperty] string region = "";
    [ObservableProperty] string chatPath = "";
    [ObservableProperty] decimal? timeoutMs;
    [ObservableProperty] decimal? healthTimeoutMs;
    [ObservableProperty] bool busy;
    [ObservableProperty] string message = "";
    public IReadOnlyList<ChatTranslateMethod> TranslateMethods { get; } = Enum.GetValues<ChatTranslateMethod>();
    public IReadOnlyList<ChatContextRetainPolicy> ContextRetainPolicies { get; } = Enum.GetValues<ChatContextRetainPolicy>();
    [ObservableProperty] ChatTranslateMethod translateMethod;
    [ObservableProperty] ChatContextRetainPolicy contextRetainPolicy;
    [ObservableProperty] string contextWindowBefore = "";
    [ObservableProperty] string contextWindowAfter = "";
    [ObservableProperty] string subtitleContextCount = "";
    [ObservableProperty] string translateCountBackward = "";
    [ObservableProperty] string translateCountForward = "";
    [ObservableProperty] string translateMaxConcurrency = "";
    [ObservableProperty] bool grammarCheckEnabled;
    [ObservableProperty] bool includeTargetLangRegion;
    [ObservableProperty] string promptContextWindow = "";
    [ObservableProperty] string promptKeepContext = "";
    [ObservableProperty] string promptOneByOne = "";
    [ObservableProperty] string promptGrammarCheck = "";
    public bool IsContextWindow => TranslateMethod == ChatTranslateMethod.ContextWindow;
    public bool IsKeepContext => TranslateMethod == ChatTranslateMethod.KeepContext;
    public bool IsOneByOne => TranslateMethod == ChatTranslateMethod.OneByOne;
    public bool ShowGrammarPrompt => IsContextWindow && GrammarCheckEnabled;
    partial void OnTranslateMethodChanged(ChatTranslateMethod value)
    {
        foreach (var name in new[] { nameof(IsContextWindow), nameof(IsKeepContext), nameof(IsOneByOne), nameof(ShowGrammarPrompt) }) OnPropertyChanged(name);
    }
    partial void OnGrammarCheckEnabledChanged(bool value) => OnPropertyChanged(nameof(ShowGrammarPrompt));
    [RelayCommand] void ResetContextWindowPrompt() => PromptContextWindow = TranslateChatConfig.DefaultPromptContextWindow.ReplaceLineEndings("\n");
    [RelayCommand] void ResetKeepContextPrompt() => PromptKeepContext = TranslateChatConfig.DefaultPromptKeepContext.ReplaceLineEndings("\n");
    [RelayCommand] void ResetOneByOnePrompt() => PromptOneByOne = TranslateChatConfig.DefaultPromptOneByOne.ReplaceLineEndings("\n");
    [RelayCommand] void ResetGrammarCheckPrompt() => PromptGrammarCheck = TranslateChatConfig.DefaultPromptGrammarCheck.ReplaceLineEndings("\n");
    ITranslateSettings? selected;
    bool loading;
    string previousEditingProvider = "Off";

    public TranslationViewModel(TranslationSettings settings, Func<TranslationSettings, Task> save, string? warning = null)
    {
        Draft = settings.Clone(); this.save = save; Warning = warning;
        dictionaryMode = Draft.DictionaryMode; provider = Draft.WordProvider; sentenceProvider = Draft.SentenceProvider; editingProvider = provider; targetLanguage = Draft.TargetLanguage;
        translateMethod = Draft.Chat.TranslateMethod; contextRetainPolicy = Draft.Chat.ContextRetainPolicy;
        contextWindowBefore = Draft.Chat.ContextWindowBefore.ToString(CultureInfo.InvariantCulture);
        contextWindowAfter = Draft.Chat.ContextWindowAfter.ToString(CultureInfo.InvariantCulture);
        subtitleContextCount = Draft.Chat.SubtitleContextCount.ToString(CultureInfo.InvariantCulture);
        translateCountBackward = Draft.TranslateCountBackward.ToString(CultureInfo.InvariantCulture);
        translateCountForward = Draft.TranslateCountForward.ToString(CultureInfo.InvariantCulture);
        translateMaxConcurrency = Draft.TranslateMaxConcurrency.ToString(CultureInfo.InvariantCulture);
        grammarCheckEnabled = Draft.Chat.GrammarCheckEnabled; includeTargetLangRegion = Draft.Chat.IncludeTargetLangRegion;
        promptContextWindow = Draft.Chat.PromptContextWindow; promptKeepContext = Draft.Chat.PromptKeepContext;
        promptOneByOne = Draft.Chat.PromptOneByOne; promptGrammarCheck = Draft.Chat.PromptGrammarCheck;
        SelectProvider();
        previousEditingProvider = EditingProvider;
    }
    public bool HasEndpoint => selected is GoogleV1TranslateSettings or MicrosoftTranslateSettings or DeepLXTranslateSettings or OpenAIBaseTranslateSettings;
    public bool HasModel => selected is OpenAIBaseTranslateSettings;
    public bool HasApiKey => selected is AzureTranslateSettings or DeepLTranslateSettings or OpenAITranslateSettings or OpenAILikeTranslateSettings or ClaudeTranslateSettings;
    public bool HasRegion => selected is AzureTranslateSettings;
    public bool HasChatPath => selected is OpenAILikeTranslateSettings;
    public bool HasTimeout => selected != null;
    partial void OnProviderChanged(string value)
    {
        if (Busy) return;
        try { CaptureFields(); }
        catch (ArgumentException e)
        {
            Message = e.Message;
            provider = Draft.WordProvider;
            OnPropertyChanged(nameof(Provider));
            return;
        }
        Draft.WordProvider = value;
        EditingProvider = value;
    }
    partial void OnSentenceProviderChanged(string value) => Draft.SentenceProvider = value;
    partial void OnEditingProviderChanged(string value)
    {
        if (loading) return;
        try { CaptureFields(); SelectProvider(); previousEditingProvider = value; }
        catch (ArgumentException e)
        {
            Message = e.Message;
            editingProvider = previousEditingProvider;
            OnPropertyChanged(nameof(EditingProvider));
        }
    }
    void SelectProvider()
    {
        loading = true;
        selected = null;
        if (Enum.TryParse<TranslateServiceType>(EditingProvider, out var type) && Enum.IsDefined(type))
        {
            if (!Draft.Providers.TryGetValue(type, out selected)) Draft.Providers[type] = selected = type.DefaultSettings();
        }
        Endpoint = selected switch { GoogleV1TranslateSettings s => s.Endpoint, MicrosoftTranslateSettings s => s.Endpoint,
            DeepLXTranslateSettings s => s.Endpoint, OpenAIBaseTranslateSettings s => s.Endpoint, _ => "" };
        Model = (selected as OpenAIBaseTranslateSettings)?.Model ?? "";
        ApiKey = selected switch { AzureTranslateSettings s => s.ApiKey, DeepLTranslateSettings s => s.ApiKey,
            OpenAITranslateSettings s => s.ApiKey, OpenAILikeTranslateSettings s => s.ApiKey, ClaudeTranslateSettings s => s.ApiKey, _ => "" } ?? "";
        Region = (selected as AzureTranslateSettings)?.Region ?? "";
        ChatPath = (selected as OpenAILikeTranslateSettings)?.ChatPath ?? "";
        TimeoutMs = selected switch { GoogleV1TranslateSettings s => s.TimeoutMs, MicrosoftTranslateSettings s => s.TimeoutMs,
            DeepLTranslateSettings s => s.TimeoutMs, DeepLXTranslateSettings s => s.TimeoutMs, OpenAIBaseTranslateSettings s => s.TimeoutMs, _ => null };
        HealthTimeoutMs = (selected as OpenAIBaseTranslateSettings)?.TimeoutHealthMs;
        foreach (string name in new[] { nameof(HasEndpoint), nameof(HasModel), nameof(HasApiKey), nameof(HasRegion), nameof(HasChatPath), nameof(HasTimeout) }) OnPropertyChanged(name);
        loading = false;
    }
    void CaptureFields()
    {
        if (loading || selected == null) return;
        int timeout = ToTimeout(TimeoutMs);
        switch (selected)
        {
            case GoogleV1TranslateSettings s: s.Endpoint = Endpoint; s.TimeoutMs = timeout; break;
            case MicrosoftTranslateSettings s: s.Endpoint = Endpoint; s.TimeoutMs = timeout; break;
            case DeepLTranslateSettings s: s.TimeoutMs = timeout; break;
            case DeepLXTranslateSettings s: s.Endpoint = Endpoint; s.TimeoutMs = timeout; break;
            case OpenAIBaseTranslateSettings s: s.Endpoint = Endpoint; s.Model = Model; s.TimeoutMs = timeout; s.TimeoutHealthMs = ToTimeout(HealthTimeoutMs); break;
        }
        switch (selected)
        {
            case AzureTranslateSettings s: s.ApiKey = ApiKey; s.Region = Region; break;
            case DeepLTranslateSettings s: s.ApiKey = ApiKey; break;
            case OpenAITranslateSettings s: s.ApiKey = ApiKey; break;
            case OpenAILikeTranslateSettings s: s.ApiKey = ApiKey; s.ChatPath = ChatPath; break;
            case ClaudeTranslateSettings s: s.ApiKey = ApiKey; break;
        }
    }
    static int ToTimeout(decimal? value) => value is > 0 and <= int.MaxValue && value == decimal.Truncate(value.Value)
        ? (int)value : throw new ArgumentException("Enter a positive whole number of milliseconds for each timeout.");
    static int ToContextCount(string value, string label) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 0
        ? count : throw new ArgumentException($"{label}: enter a whole number from 0 to 2147483647.");
    static int ToBoundedCount(string value, string label, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= min && count <= max
            ? count : throw new ArgumentException($"{label}: enter a whole number from {min} to {max}.");
    void CaptureChat()
    {
        // Validate every mode, including inactive drafts, before updating the detached record.
        int before = ToContextCount(ContextWindowBefore, "ContextWindow lines before"), after = ToContextCount(ContextWindowAfter, "ContextWindow lines after"), count = ToContextCount(SubtitleContextCount, "KeepContext subtitle count");
        Draft.Chat.TranslateMethod = TranslateMethod; Draft.Chat.ContextRetainPolicy = ContextRetainPolicy;
        Draft.Chat.ContextWindowBefore = before; Draft.Chat.ContextWindowAfter = after; Draft.Chat.SubtitleContextCount = count;
        Draft.Chat.GrammarCheckEnabled = GrammarCheckEnabled; Draft.Chat.IncludeTargetLangRegion = IncludeTargetLangRegion;
        Draft.Chat.PromptContextWindow = PromptContextWindow; Draft.Chat.PromptKeepContext = PromptKeepContext;
        Draft.Chat.PromptOneByOne = PromptOneByOne; Draft.Chat.PromptGrammarCheck = PromptGrammarCheck;
        Draft.TranslateCountBackward = ToBoundedCount(TranslateCountBackward, "Max translate count backward", 0, 1000);
        Draft.TranslateCountForward = ToBoundedCount(TranslateCountForward, "Max translate count forward", 0, 1000);
        Draft.TranslateMaxConcurrency = ToBoundedCount(TranslateMaxConcurrency, "Max translate concurrency", 1, 64);
    }
    [RelayCommand]
    async Task SaveAsync()
    {
        Busy = true;
        try
        {
            CaptureChat(); CaptureFields(); Draft.WordProvider = Provider; Draft.SentenceProvider = SentenceProvider; Draft.TargetLanguage = TargetLanguage;
            Draft.Validate();
            await save(Draft.Clone());
            Busy = false;
            Saved?.Invoke();
        }
        catch (ArgumentException e) { Message = e.Message; }
        catch (Exception) { Message = "Translation settings could not be saved. Live settings have not changed; check access to the configuration directory."; }
        finally { Busy = false; }
    }
}
