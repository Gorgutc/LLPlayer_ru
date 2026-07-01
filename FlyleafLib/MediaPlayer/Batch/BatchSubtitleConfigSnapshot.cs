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
            // T-09 silence-split applies to batch transcription too (same producer); fold-back (ASRFoldBack) is a
            // structural no-op for batch because BatchAsrTranscriber calls ReadAll(TimeSpan.Zero, ...) so the
            // seek/curTime>30s path never fires — copied only for snapshot/reflection-guard parity.
            ASRSplitOnSilence = source.ASRSplitOnSilence,
            ASRSilenceSoftFraction = source.ASRSilenceSoftFraction,
            ASRSilenceRmsThreshold = source.ASRSilenceRmsThreshold,
            ASRFoldBack = source.ASRFoldBack,
            // F-02 denoise applies to batch transcription too (same AudioReader producer / ResampleTo seam).
            ASRDenoise = source.ASRDenoise,
            // T-10 per-segment language detection applies to batch transcription too (same shared ASR services).
            ASRPerSegmentLanguage = source.ASRPerSegmentLanguage,
            ResegmentSubtitles = source.ResegmentSubtitles,
            SubtitleMaxCharsPerLine = source.SubtitleMaxCharsPerLine,
            SubtitleMaxLinesPerCue = source.SubtitleMaxLinesPerCue,
            SubtitleMaxCjkCharsPerLine = source.SubtitleMaxCjkCharsPerLine,
            SubtitleMaxCueDurationSec = source.SubtitleMaxCueDurationSec,
            SubtitleMinCueDurationSec = source.SubtitleMinCueDurationSec,
            FixAllCaps = source.FixAllCaps,
            // F-16 persistence flag: a scalar on SubtitlesConfig, so the reflection-completeness guard requires it
            // here. The batch path does not read it directly (the dialog composes the disk voice provider), but it
            // is copied for snapshot parity per the scalar-completeness guard.
            PersistPerLineVoices = source.PersistPerLineVoices,
            TesseractOcrRegions = new Dictionary<string, string>(source.TesseractOcrRegions),
            MsOcrRegions = new Dictionary<string, string>(source.MsOcrRegions),
            TranslateServiceType = source.TranslateServiceType,
            TranslateWordServiceType = source.TranslateWordServiceType,
            // F-11: an interactive (word-popup) setting like TranslateWordServiceType; copied for snapshot
            // completeness (the batch path does not read it) per the scalar-completeness guard.
            WordDefinitionServiceType = source.WordDefinitionServiceType,
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

        // Per-slot subtitle language preferences + unknown-source fallbacks (primary/secondary). These were dropped
        // from the batch snapshot, so batch transcription/translation silently ignored the user's language prefs
        // (same snapshot bug class as the #42/#43 settings). The getters lazily fall back to GetSystemLanguages(),
        // which can throw in headless/secondary contexts, so guard the copy exactly like CloneAudioConfig does.
        try
        {
            // Read Languages FIRST: the only fallback (catch) sets just Languages, so on a throw the rest must not
            // have been partially copied. The Fallback* getters also chain through Languages.
            snapshot.Languages = new List<Language>(source.Languages);
            snapshot.LanguageAutoDetect = source.LanguageAutoDetect;
            snapshot.LanguageFallbackPrimary = source.LanguageFallbackPrimary;
            snapshot.LanguageFallbackSecondarySame = source.LanguageFallbackSecondarySame;
            snapshot.LanguageFallbackSecondary = source.LanguageFallbackSecondary;
        }
        catch (NullReferenceException)
        {
            snapshot.Languages = [Language.English];
        }

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
                // Quantization MUST be copied: the reflection-completeness guard
                // (BatchSubtitleTranslatorTests.CreateSubtitlesConfig_ShouldCopyEveryScalarSubtitlesConfigSetting)
                // only enumerates scalars declared on SubtitlesConfig and explicitly skips nested config objects, so
                // dropping it here would silently make a batch run resolve ggml-{model}.bin (the full model) instead
                // of the user's quantized file — pinned by CloneWhisperCppConfig_ShouldCopyModelQuantization.
                : new WhisperCppModel
                {
                    Model = source.Model.Model,
                    Quantization = source.Model.Quantization,
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
