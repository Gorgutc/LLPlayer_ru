using System.Text.Json;
using System.Text.RegularExpressions;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using Whisper.net.LibraryLoader;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public static class BatchSubtitleConfigSnapshot
{
    public static Config Create(Config source)
    {
        Config snapshot = new(true)
        {
            Audio = CloneAudioConfig(source.Audio),
            Video = source.Video.Clone(),
            Subtitles = CreateSubtitlesConfig(source.Subtitles),
            Demuxer = source.Demuxer.Clone(),
            Decoder = source.Decoder.Clone(),
            Player = source.Player.Clone(),
            Data = source.Data.Clone(),
            Plugins = ClonePlugins(source.Plugins)
        };

        snapshot.Player.config = snapshot;
        snapshot.Demuxer.config = snapshot;

        return snapshot;
    }

    private static Config.AudioConfig CloneAudioConfig(Config.AudioConfig source)
    {
        Config.AudioConfig audio = source.Clone();
        try
        {
            audio.Languages = new List<Language>(source.Languages);
        }
        catch (NullReferenceException)
        {
            audio.Languages = [Language.English];
        }

        return audio;
    }

    public static Config.SubtitlesConfig CreateSubtitlesConfig(Config.SubtitlesConfig source)
    {
        Config.SubtitlesConfig snapshot = new()
        {
            Enabled = source.Enabled,
            DelayOffset = source.DelayOffset,
            DelayOffset2 = source.DelayOffset2,
            Max = source.Max,
            EnabledCached = source.EnabledCached,
            OpenAutomaticSubs = source.OpenAutomaticSubs,
            SearchLocal = source.SearchLocal,
            SearchLocalPaths = source.SearchLocalPaths,
            SearchLocalOnInputType = new List<InputType>(source.SearchLocalOnInputType),
            SearchOnline = source.SearchOnline,
            SearchOnlineOnInputType = new List<InputType>(source.SearchOnlineOnInputType),
            ASREngine = source.ASREngine,
            ASRChunkSizeMB = source.ASRChunkSizeMB,
            ASRChunkSeconds = source.ASRChunkSeconds,
            ResegmentSubtitles = source.ResegmentSubtitles,
            SubtitleMaxCharsPerLine = source.SubtitleMaxCharsPerLine,
            SubtitleMaxLinesPerCue = source.SubtitleMaxLinesPerCue,
            SubtitleMaxCjkCharsPerLine = source.SubtitleMaxCjkCharsPerLine,
            SubtitleMaxCueDurationSec = source.SubtitleMaxCueDurationSec,
            SubtitleMinCueDurationSec = source.SubtitleMinCueDurationSec,
            FixAllCaps = source.FixAllCaps,
            TesseractOcrRegions = new Dictionary<string, string>(source.TesseractOcrRegions),
            MsOcrRegions = new Dictionary<string, string>(source.MsOcrRegions),
            TranslateServiceType = source.TranslateServiceType,
            TranslateWordServiceType = source.TranslateWordServiceType,
            TranslateCountBackward = source.TranslateCountBackward,
            TranslateCountForward = source.TranslateCountForward,
            TranslateMaxConcurrency = source.TranslateMaxConcurrency
        };

        snapshot.WhisperConfig = CloneWhisperConfig(source.WhisperConfig);
        snapshot.WhisperConfig.Translate = false;
        snapshot.WhisperCppConfig = CloneWhisperCppConfig(source.WhisperCppConfig);
        snapshot.FasterWhisperConfig = CloneFasterWhisperConfig(source.FasterWhisperConfig);
        snapshot.TranslateChatConfig = CloneTranslateChatConfig(source.TranslateChatConfig);
        snapshot.TranslateServiceSettings = CloneTranslateServiceSettings(source.TranslateServiceSettings);
        snapshot.TranslateTargetLanguage = TargetLanguage.Russian;

        return snapshot;
    }

    private static Dictionary<string, Utils.ObservableDictionary<string, string>> ClonePlugins(
        Dictionary<string, Utils.ObservableDictionary<string, string>> source)
    {
        Dictionary<string, Utils.ObservableDictionary<string, string>> snapshot = [];

        foreach ((string pluginName, Utils.ObservableDictionary<string, string> pluginOptions) in source)
        {
            Utils.ObservableDictionary<string, string> options = [];
            foreach ((string key, string value) in pluginOptions)
                options[key] = value;

            snapshot[pluginName] = options;
        }

        return snapshot;
    }

    private static WhisperConfig CloneWhisperConfig(WhisperConfig source)
    {
        return new WhisperConfig
        {
            Language = source.Language,
            LanguageDetection = source.LanguageDetection,
            Translate = source.Translate
        };
    }

    private static WhisperCppConfig CloneWhisperCppConfig(WhisperCppConfig source)
    {
        return new WhisperCppConfig
        {
            Model = source.Model == null
                ? null
                : new WhisperCppModel
                {
                    Model = source.Model.Model,
                    Size = source.Model.Size
                },
            RuntimeLibraries = new List<RuntimeLibrary>(source.RuntimeLibraries),
            GpuDevice = source.GpuDevice,
            Threads = source.Threads,
            MaxSegmentLength = source.MaxSegmentLength,
            MaxTokensPerSegment = source.MaxTokensPerSegment,
            SplitOnWord = source.SplitOnWord,
            NoSpeechThreshold = source.NoSpeechThreshold,
            // Anti-repetition / anti-hallucination decoding guards. These were dropped from the batch snapshot,
            // so a UI-set guard was silently ignored during batch transcription — carry them over.
            Temperature = source.Temperature,
            TemperatureInc = source.TemperatureInc,
            EntropyThreshold = source.EntropyThreshold,
            LogProbThreshold = source.LogProbThreshold,
            NoContext = source.NoContext,
            AudioContextSize = source.AudioContextSize,
            Prompt = source.Prompt
        };
    }

    private static FasterWhisperConfig CloneFasterWhisperConfig(FasterWhisperConfig source)
    {
        return new FasterWhisperConfig
        {
            UseManualEngine = source.UseManualEngine,
            ManualEnginePath = source.ManualEnginePath,
            UseManualModel = source.UseManualModel,
            ManualModelDir = source.ManualModelDir,
            Model = source.Model,
            ExtraArguments = RemoveFasterWhisperTaskArgument(source.ExtraArguments),
            ProcessPriority = source.ProcessPriority,
            AntiHallucination = source.AntiHallucination,
            Prompt = source.Prompt
        };
    }

    private static string RemoveFasterWhisperTaskArgument(string extraArguments)
    {
        if (string.IsNullOrWhiteSpace(extraArguments))
            return string.Empty;

        string sanitized = Regex.Replace(
            extraArguments,
            @"(?i)(^|\s)--task(?:=|\s+)(?:""[^""]*""|'[^']*'|\S+)",
            " ");

        return Regex.Replace(sanitized, @"\s+", " ").Trim();
    }

    private static TranslateChatConfig CloneTranslateChatConfig(TranslateChatConfig source)
    {
        return new TranslateChatConfig
        {
            PromptOneByOne = source.PromptOneByOne,
            PromptKeepContext = source.PromptKeepContext,
            PromptContextWindow = source.PromptContextWindow,
            PromptGrammarCheck = source.PromptGrammarCheck,
            TranslateMethod = source.TranslateMethod,
            SubtitleContextCount = source.SubtitleContextCount,
            ContextRetainPolicy = source.ContextRetainPolicy,
            ContextWindowBefore = source.ContextWindowBefore,
            ContextWindowAfter = source.ContextWindowAfter,
            GrammarCheckEnabled = source.GrammarCheckEnabled,
            IncludeTargetLangRegion = source.IncludeTargetLangRegion
        };
    }

    private static Dictionary<TranslateServiceType, ITranslateSettings> CloneTranslateServiceSettings(
        Dictionary<TranslateServiceType, ITranslateSettings> source)
    {
        Dictionary<TranslateServiceType, ITranslateSettings> snapshot = new();

        foreach ((TranslateServiceType serviceType, ITranslateSettings settings) in source)
        {
            snapshot[serviceType] = CloneTranslateSettings(settings);
        }

        return snapshot;
    }

    private static ITranslateSettings CloneTranslateSettings(ITranslateSettings source)
    {
        Type sourceType = source.GetType();
        string json = JsonSerializer.Serialize(source, sourceType);

        return (ITranslateSettings)JsonSerializer.Deserialize(json, sourceType)!;
    }
}
