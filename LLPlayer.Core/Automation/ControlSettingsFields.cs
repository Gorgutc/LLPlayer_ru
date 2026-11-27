using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;

namespace LLPlayer.Core.Automation;

public sealed partial class ControlSettingsService
{
    static SettingsFields<RecognitionSettings> RecognitionFields()
    {
        var fields = new SettingsFields<RecognitionSettings>();
        fields.Add("asrEngine", s => s.ASREngine, (s, value) => s.ASREngine = value);
        fields.Add("asrChunkSizeMB", s => s.ASRChunkSizeMB, (s, value) => s.ASRChunkSizeMB = value);
        fields.Add("asrChunkSeconds", s => s.ASRChunkSeconds, (s, value) => s.ASRChunkSeconds = value);
        fields.Add("asrSpeechSeparation", s => s.ASRSpeechSeparation, (s, value) => s.ASRSpeechSeparation = value);
        fields.Add("ocrPrimaryEngine", s => s.OcrPrimaryEngine, (s, value) => s.OcrPrimaryEngine = value);
        fields.Add("ocrSecondaryEngine", s => s.OcrSecondaryEngine, (s, value) => s.OcrSecondaryEngine = value);
        fields.Add("tesseractOcrRegions", s => s.TesseractOcrRegions, (s, value) => s.TesseractOcrRegions = value);
        fields.Add("msOcrRegions", s => s.MsOcrRegions, (s, value) => s.MsOcrRegions = value);
        fields.Add("whisper.language", s => s.Whisper.Language, (s, value) => s.Whisper.Language = value);
        fields.Add("whisper.languageDetection", s => s.Whisper.LanguageDetection, (s, value) => s.Whisper.LanguageDetection = value);
        fields.Add("whisper.translate", s => s.Whisper.Translate, (s, value) => s.Whisper.Translate = value);
        fields.Add("whisperCpp.model", s => s.WhisperCpp.Model, (s, value) => s.WhisperCpp.Model = value, allowsNull: true);
        fields.Add("whisperCpp.modelDirectory", s => s.WhisperCpp.ModelDirectory, (s, value) => s.WhisperCpp.ModelDirectory = value, allowsNull: true);
        fields.Add("whisperCpp.runtimeLibraries", s => s.WhisperCpp.RuntimeLibraries, (s, value) => s.WhisperCpp.RuntimeLibraries = value);
        fields.Add("whisperCpp.gpuDevice", s => s.WhisperCpp.GpuDevice, (s, value) => s.WhisperCpp.GpuDevice = value, allowsNull: true);
        fields.Add("whisperCpp.threads", s => s.WhisperCpp.Threads, (s, value) => s.WhisperCpp.Threads = value, allowsNull: true);
        fields.Add("whisperCpp.maxSegmentLength", s => s.WhisperCpp.MaxSegmentLength, (s, value) => s.WhisperCpp.MaxSegmentLength = value, allowsNull: true);
        fields.Add("whisperCpp.maxTokensPerSegment", s => s.WhisperCpp.MaxTokensPerSegment, (s, value) => s.WhisperCpp.MaxTokensPerSegment = value, allowsNull: true);
        fields.Add("whisperCpp.splitOnWord", s => s.WhisperCpp.SplitOnWord, (s, value) => s.WhisperCpp.SplitOnWord = value);
        fields.Add("whisperCpp.noSpeechThreshold", s => s.WhisperCpp.NoSpeechThreshold, (s, value) => s.WhisperCpp.NoSpeechThreshold = value, allowsNull: true);
        fields.Add("whisperCpp.temperature", s => s.WhisperCpp.Temperature, (s, value) => s.WhisperCpp.Temperature = value, allowsNull: true);
        fields.Add("whisperCpp.temperatureInc", s => s.WhisperCpp.TemperatureInc, (s, value) => s.WhisperCpp.TemperatureInc = value, allowsNull: true);
        fields.Add("whisperCpp.entropyThreshold", s => s.WhisperCpp.EntropyThreshold, (s, value) => s.WhisperCpp.EntropyThreshold = value, allowsNull: true);
        fields.Add("whisperCpp.logProbThreshold", s => s.WhisperCpp.LogProbThreshold, (s, value) => s.WhisperCpp.LogProbThreshold = value, allowsNull: true);
        fields.Add("whisperCpp.noContext", s => s.WhisperCpp.NoContext, (s, value) => s.WhisperCpp.NoContext = value);
        fields.Add("whisperCpp.audioContextSize", s => s.WhisperCpp.AudioContextSize, (s, value) => s.WhisperCpp.AudioContextSize = value, allowsNull: true);
        fields.Add("whisperCpp.prompt", s => s.WhisperCpp.Prompt, (s, value) => s.WhisperCpp.Prompt = value);
        fields.Add("fasterWhisper.useManualEngine", s => s.FasterWhisper.UseManualEngine, (s, value) => s.FasterWhisper.UseManualEngine = value);
        fields.Add("fasterWhisper.manualEnginePath", s => s.FasterWhisper.ManualEnginePath, (s, value) => s.FasterWhisper.ManualEnginePath = value, allowsNull: true);
        fields.Add("fasterWhisper.useManualModel", s => s.FasterWhisper.UseManualModel, (s, value) => s.FasterWhisper.UseManualModel = value);
        fields.Add("fasterWhisper.manualModelDir", s => s.FasterWhisper.ManualModelDir, (s, value) => s.FasterWhisper.ManualModelDir = value, allowsNull: true);
        fields.Add("fasterWhisper.model", s => s.FasterWhisper.Model, (s, value) => s.FasterWhisper.Model = value);
        fields.Add("fasterWhisper.extraArguments", s => s.FasterWhisper.ExtraArguments, (s, value) => s.FasterWhisper.ExtraArguments = value);
        fields.Add("fasterWhisper.prompt", s => s.FasterWhisper.Prompt, (s, value) => s.FasterWhisper.Prompt = value);
        fields.Add("fasterWhisper.antiHallucination", s => s.FasterWhisper.AntiHallucination, (s, value) => s.FasterWhisper.AntiHallucination = value);
        fields.Add("fasterWhisper.processPriority", s => s.FasterWhisper.ProcessPriority, (s, value) => s.FasterWhisper.ProcessPriority = value);
        return fields;
    }

    static SettingsFields<TranslationSettings> TranslationFields()
    {
        var fields = new SettingsFields<TranslationSettings>();
        fields.Add("dictionaryMode", s => s.DictionaryMode, (s, value) => s.DictionaryMode = value);
        fields.Add("sentenceProvider", s => s.SentenceProvider, (s, value) => s.SentenceProvider = value);
        fields.Add("wordProvider", s => s.WordProvider, (s, value) => s.WordProvider = value);
        fields.Add("targetLanguage", s => s.TargetLanguage, (s, value) => s.TargetLanguage = value);
        fields.Add("translateCountBackward", s => s.TranslateCountBackward, (s, value) => s.TranslateCountBackward = value);
        fields.Add("translateCountForward", s => s.TranslateCountForward, (s, value) => s.TranslateCountForward = value);
        fields.Add("translateMaxConcurrency", s => s.TranslateMaxConcurrency, (s, value) => s.TranslateMaxConcurrency = value);
        fields.Add("chat.promptOneByOne", s => s.Chat.PromptOneByOne, (s, value) => s.Chat.PromptOneByOne = value);
        fields.Add("chat.promptKeepContext", s => s.Chat.PromptKeepContext, (s, value) => s.Chat.PromptKeepContext = value);
        fields.Add("chat.promptContextWindow", s => s.Chat.PromptContextWindow, (s, value) => s.Chat.PromptContextWindow = value);
        fields.Add("chat.promptGrammarCheck", s => s.Chat.PromptGrammarCheck, (s, value) => s.Chat.PromptGrammarCheck = value);
        fields.Add("chat.translateMethod", s => s.Chat.TranslateMethod, (s, value) => s.Chat.TranslateMethod = value);
        fields.Add("chat.subtitleContextCount", s => s.Chat.SubtitleContextCount, (s, value) => s.Chat.SubtitleContextCount = value);
        fields.Add("chat.contextRetainPolicy", s => s.Chat.ContextRetainPolicy, (s, value) => s.Chat.ContextRetainPolicy = value);
        fields.Add("chat.contextWindowBefore", s => s.Chat.ContextWindowBefore, (s, value) => s.Chat.ContextWindowBefore = value);
        fields.Add("chat.contextWindowAfter", s => s.Chat.ContextWindowAfter, (s, value) => s.Chat.ContextWindowAfter = value);
        fields.Add("chat.grammarCheckEnabled", s => s.Chat.GrammarCheckEnabled, (s, value) => s.Chat.GrammarCheckEnabled = value);
        fields.Add("chat.includeTargetLangRegion", s => s.Chat.IncludeTargetLangRegion, (s, value) => s.Chat.IncludeTargetLangRegion = value);
        AddProvider<GoogleV1TranslateSettings>(fields, TranslateServiceType.GoogleV1, (add, _) =>
        {
            add.Endpoint(p => p.Endpoint, (p, value) => p.Endpoint = value);
            add.Field("timeoutMs", p => p.TimeoutMs, (p, value) => p.TimeoutMs = value);
            add.Field("regions", p => p.Regions, (p, value) => p.Regions = value);
        });

        foreach (var provider in new[] { TranslateServiceType.Bing, TranslateServiceType.Azure })
            AddProvider<MicrosoftTranslateSettings>(fields, provider, (add, type) =>
            {
                add.Endpoint(p => p.Endpoint, (p, value) => p.Endpoint = value);
                add.Field("timeoutMs", p => p.TimeoutMs, (p, value) => p.TimeoutMs = value);
                add.Field("regions", p => p.Regions, (p, value) => p.Regions = value);
                if (type == TranslateServiceType.Azure)
                {
                    add.Field("region", p => ((AzureTranslateSettings)p).Region, (p, value) => ((AzureTranslateSettings)p).Region = value!, allowsNull: true);
                    add.Credential("apiKey", p => ((AzureTranslateSettings)p).ApiKey, (p, value) => ((AzureTranslateSettings)p).ApiKey = value);
                }
            });

        AddProvider<DeepLTranslateSettings>(fields, TranslateServiceType.DeepL, (add, _) =>
        {
            add.Field("timeoutMs", p => p.TimeoutMs, (p, value) => p.TimeoutMs = value);
            add.Credential("apiKey", p => p.ApiKey, (p, value) => p.ApiKey = value);
        });
        AddProvider<DeepLXTranslateSettings>(fields, TranslateServiceType.DeepLX, (add, _) =>
        {
            add.Endpoint(p => p.Endpoint, (p, value) => p.Endpoint = value);
            add.Field("timeoutMs", p => p.TimeoutMs, (p, value) => p.TimeoutMs = value);
        });

        foreach (var type in Enum.GetValues<TranslateServiceType>().Where(type => type.DefaultSettings() is OpenAIBaseTranslateSettings))
            AddProvider<OpenAIBaseTranslateSettings>(fields, type, AddLlmProvider);
        return fields;
    }

    static void AddLlmProvider(ProviderFields<OpenAIBaseTranslateSettings> add, TranslateServiceType type)
    {
        add.Endpoint(p => p.Endpoint, (p, value) => p.Endpoint = value);
        add.Field("model", p => p.Model, (p, value) => p.Model = value!, allowsNull: true);
        add.Field("timeoutMs", p => p.TimeoutMs, (p, value) => p.TimeoutMs = value);
        add.Field("timeoutHealthMs", p => p.TimeoutHealthMs, (p, value) => p.TimeoutHealthMs = value);
        add.Field("temperature", p => p.Temperature, (p, value) => p.Temperature = Range(value, 0, 2));
        add.Field("temperatureManual", p => p.TemperatureManual, (p, value) => p.TemperatureManual = value);
        add.Field("topP", p => p.TopP, (p, value) => p.TopP = Range(value, 0, 1));
        add.Field("topPManual", p => p.TopPManual, (p, value) => p.TopPManual = value);
        add.Field("maxTokens", p => p.MaxTokens, (p, value) => p.MaxTokens = PositiveOrNull(value), allowsNull: true);
        add.Field("maxCompletionTokens", p => p.MaxCompletionTokens, (p, value) => p.MaxCompletionTokens = PositiveOrNull(value), allowsNull: true);
        add.Field("frequencyPenalty", p => p.FrequencyPenalty, (p, value) => p.FrequencyPenalty = Range(value, -2, 2));
        add.Field("frequencyPenaltyManual", p => p.FrequencyPenaltyManual, (p, value) => p.FrequencyPenaltyManual = value);
        add.Field("presencePenalty", p => p.PresencePenalty, (p, value) => p.PresencePenalty = Range(value, -2, 2));
        add.Field("presencePenaltyManual", p => p.PresencePenaltyManual, (p, value) => p.PresencePenaltyManual = value);

        switch (type)
        {
            case TranslateServiceType.OpenAI:
                add.Credential("apiKey", p => ((OpenAITranslateSettings)p).ApiKey, (p, value) => ((OpenAITranslateSettings)p).ApiKey = value);
                break;
            case TranslateServiceType.OpenAILike:
                add.Field("chatPath", p => PublicChatPath(p.ChatPath), (p, value) => p.ChatPath = ChatPath(value),
                    readPrivate: p => p.ChatPath, writePrivate: (p, value) => p.ChatPath = value);
                add.Credential("apiKey", p => ((OpenAILikeTranslateSettings)p).ApiKey, (p, value) => ((OpenAILikeTranslateSettings)p).ApiKey = value);
                break;
            case TranslateServiceType.Claude:
                add.Credential("apiKey", p => ((ClaudeTranslateSettings)p).ApiKey, (p, value) => ((ClaudeTranslateSettings)p).ApiKey = value);
                break;
        }
    }

    static void AddProvider<T>(SettingsFields<TranslationSettings> fields, TranslateServiceType type,
        Action<ProviderFields<T>, TranslateServiceType> add) where T : class, ITranslateSettings
        => add(new(fields, type), type);

    sealed class ProviderFields<T>(SettingsFields<TranslationSettings> fields, TranslateServiceType type) where T : class, ITranslateSettings
    {
        readonly string prefix = "providers." + System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(type.ToString()) + ".";

        public void Field<TValue>(string name, Func<T, TValue> read, Action<T, TValue> write, bool allowsNull = false,
            Func<T, TValue>? readPrivate = null, Action<T, TValue>? writePrivate = null)
            => fields.Add(prefix + name, s => read(Get(s, create: false)), (s, value) => write(Get(s, create: true), value), allowsNull,
                readPrivate: readPrivate == null ? null : s => readPrivate(Get(s, create: false)),
                writePrivate: writePrivate == null ? null : (s, value) => writePrivate(Get(s, create: true), value));

        public void Endpoint(Func<T, string> read, Action<T, string> write)
            => Field("endpoint", provider => PublicEndpoint(read(provider)), (provider, value) => write(provider, ControlSettingsService.Endpoint(value)),
                readPrivate: read, writePrivate: write);

        public void Credential(string name, Func<T, string?> read, Action<T, string?> write)
            => fields.AddCredential(prefix + name, s => read(Get(s, create: false)), (s, value) => write(Get(s, create: true), value));

        T Get(TranslationSettings settings, bool create)
        {
            if (settings.Providers.TryGetValue(type, out var provider)) return (T)provider;
            var defaults = (T)type.DefaultSettings();
            if (create) settings.Providers.Add(type, defaults);
            return defaults;
        }
    }

    static string Endpoint(string value)
    {
        if (value.Length == 0) return value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Endpoints must not contain credentials, query strings, or fragments.");
        return value;
    }

    static string PublicEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "";
        if (uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0) return value;
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri;
    }

    static string ChatPath(string value)
    {
        if (value.Contains('?') || value.Contains('#')) throw new ArgumentException("Chat paths must not contain queries or fragments.");
        return value;
    }

    static string PublicChatPath(string value)
    {
        int delimiter = value.IndexOfAny(['?', '#']);
        return delimiter < 0 ? value : value[..delimiter];
    }

    static double Range(double value, double min, double max) => value >= min && value <= max ? value : throw new ArgumentException();
    static int? PositiveOrNull(int? value) => value is null or > 0 ? value : throw new ArgumentException();

    static SettingsFields<BatchSettings> BatchFields()
    {
        var fields = new SettingsFields<BatchSettings>();
        fields.Add("lastFolder", s => s.LastFolder, (s, value) => s.LastFolder = value);
        fields.Add("recursive", s => s.Recursive, (s, value) => s.Recursive = value);
        fields.Add("overwriteExisting", s => s.OverwriteExisting, (s, value) => s.OverwriteExisting = value);
        fields.Add("serializeAsrAndTranslate", s => s.SerializeAsrAndTranslate, (s, value) => s.SerializeAsrAndTranslate = value);
        fields.Add("runOnCpuWhenActive", s => s.RunOnCpuWhenActive, (s, value) => s.RunOnCpuWhenActive = value);
        fields.Add("activeIdleThresholdSeconds", s => s.ActiveIdleThresholdSeconds, (s, value) => s.ActiveIdleThresholdSeconds = value);
        fields.Add("preferRussianAudio", s => s.PreferRussianAudio, (s, value) => s.PreferRussianAudio = value);
        fields.Add("generateDubbing", s => s.GenerateDubbing, (s, value) => s.GenerateDubbing = value);
        fields.Add("watchFolder", s => s.WatchFolder, (s, value) => s.WatchFolder = value);
        return fields;
    }

    static SettingsFields<DubbingSettings> DubbingFields()
    {
        var fields = new SettingsFields<DubbingSettings>();
        fields.Add("persistPerLineVoices", s => s.PersistPerLineVoices, (s, value) => s.PersistPerLineVoices = value);
        fields.Add("engine.ttsServiceType", s => s.Engine.TtsServiceType, (s, value) => s.Engine.TtsServiceType = value);
        fields.Add("engine.useManualEngine", s => s.Engine.UseManualEngine, (s, value) => s.Engine.UseManualEngine = value);
        fields.Add("engine.manualVenvPython", s => s.Engine.ManualVenvPython, (s, value) => s.Engine.ManualVenvPython = value, allowsNull: true);
        fields.Add("engine.model", s => s.Engine.Model, (s, value) => s.Engine.Model = value);
        fields.Add("engine.defaultVoiceId", s => s.Engine.DefaultVoiceId, (s, value) => s.Engine.DefaultVoiceId = value);
        fields.Add("engine.customVoiceIds", s => s.Engine.CustomVoiceIds, (s, value) => s.Engine.CustomVoiceIds = value);
        fields.Add("engine.duckingPercent", s => s.Engine.DuckingPercent, (s, value) => s.Engine.DuckingPercent = value);
        fields.Add("engine.atempoMin", s => s.Engine.AtempoMin, (s, value) => s.Engine.AtempoMin = value);
        fields.Add("engine.atempoMax", s => s.Engine.AtempoMax, (s, value) => s.Engine.AtempoMax = value);
        fields.Add("engine.stressNormalization", s => s.Engine.StressNormalization, (s, value) => s.Engine.StressNormalization = value);
        fields.Add("engine.outputFormat", s => s.Engine.OutputFormat, (s, value) => s.Engine.OutputFormat = value);
        return fields;
    }
}
