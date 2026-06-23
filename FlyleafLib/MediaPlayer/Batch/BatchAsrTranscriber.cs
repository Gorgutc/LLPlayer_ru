using System.Linq;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public sealed class BatchAsrTranscriber : IBatchAsrTranscriber
{
    private readonly Config _batchConfig;
    private readonly Func<bool>? _preferCpu;

    /// <param name="preferCpu">Optional per-chunk device policy for faster-whisper: when it returns true the
    /// NEXT audio chunk is transcribed on CPU instead of GPU (the chunk in flight finishes on its current
    /// device, so nothing computed is lost). Null = always use the configured device.</param>
    public BatchAsrTranscriber(Config sourceConfig, Func<bool>? preferCpu = null)
    {
        _batchConfig = BatchSubtitleConfigSnapshot.Create(sourceConfig);
        _preferCpu = preferCpu;
        ValidateAsrConfig(_batchConfig);
    }

    public Task<BatchAsrResult> TranscribeAsync(
        string mediaPath,
        CancellationToken token,
        IProgress<BatchAsrProgress>? asrProgress = null)
        => Task.Run(() => Transcribe(mediaPath, token, asrProgress), token);

    private BatchAsrResult Transcribe(string mediaPath, CancellationToken token, IProgress<BatchAsrProgress>? asrProgress)
    {
        MediaAudioProbeResult audio = new MediaAudioProbe(_batchConfig).Probe(mediaPath, token);

        return TranscribeInternal(_batchConfig, audio, token, asrProgress, _preferCpu);
    }

    private static BatchAsrResult TranscribeInternal(
        Config batchConfig,
        MediaAudioProbeResult audio,
        CancellationToken token,
        IProgress<BatchAsrProgress>? asrProgress,
        Func<bool>? preferCpu)
    {
        List<SubtitleData> subtitles = [];
        Language sourceLanguage = GetInitialSourceLanguage(batchConfig);

        using AudioReader reader = new(batchConfig, 0, preferCpu);
        reader.Open(audio.MediaPath, audio.StreamIndex, audio.MediaType, token);
        token.ThrowIfCancellationRequested();

        reader.ReadAll(TimeSpan.Zero, data =>
        {
            if (token.IsCancellationRequested)
                return;

            Language asrLanguage = GetKnownLanguage(data.Language);
            if (asrLanguage != Language.Unknown)
                sourceLanguage = asrLanguage;

            SubtitleData subtitle = new()
            {
                Index = subtitles.Count,
                Text = data.Text,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
#if DEBUG
                ChunkNo = data.ChunkNo,
                StartTimeChunk = data.StartTimeChunk,
                EndTimeChunk = data.EndTimeChunk,
#endif
            };

            subtitles.Add(subtitle);

            // Stream per-segment progress so the UI shows live feedback during the (otherwise opaque) ASR.
            asrProgress?.Report(new BatchAsrProgress(
                audio.MediaPath,
                subtitles.Count,
                data.Text,
                data.EndTime,
                audio.Duration));
        }, token);
        token.ThrowIfCancellationRequested();

        subtitles = subtitles
            .OrderBy(s => s.StartTime)
            .Select((s, index) =>
            {
                s.Index = index;
                return s;
            })
            .ToList();

        return new BatchAsrResult(subtitles, sourceLanguage);
    }

    private static Language GetInitialSourceLanguage(Config batchConfig)
    {
        if (batchConfig.Subtitles.ASREngine == SubASREngineType.WhisperCpp &&
            batchConfig.Subtitles.WhisperCppConfig.IsEnglishModel)
        {
            return Language.English;
        }

        if (batchConfig.Subtitles.ASREngine == SubASREngineType.FasterWhisper &&
            batchConfig.Subtitles.FasterWhisperConfig.IsEnglishModel)
        {
            return Language.English;
        }

        if (!batchConfig.Subtitles.WhisperConfig.LanguageDetection)
            return GetKnownLanguage(batchConfig.Subtitles.WhisperConfig.Language);

        return Language.Unknown;
    }

    private static Language GetKnownLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return Language.Unknown;

        Language resolved = Language.Get(language);

        return resolved == Language.Unknown ? Language.Unknown : resolved;
    }

    private static void ValidateAsrConfig(Config config)
    {
        if (config.Subtitles.ASREngine == SubASREngineType.WhisperCpp)
        {
            if (config.Subtitles.WhisperCppConfig.Model == null)
                throw new InvalidOperationException("whisper.cpp model is not set. Please download it from the settings.");

            if (!File.Exists(config.Subtitles.WhisperCppConfig.Model.ModelFilePath))
            {
                throw new InvalidOperationException(
                    $"whisper.cpp model file '{config.Subtitles.WhisperCppConfig.Model.ModelFileName}' does not exist in the folder. Please download it from the settings.");
            }

            return;
        }

        if (config.Subtitles.ASREngine == SubASREngineType.FasterWhisper)
        {
            if (config.Subtitles.FasterWhisperConfig.UseManualEngine)
            {
                if (!File.Exists(config.Subtitles.FasterWhisperConfig.ManualEnginePath))
                    throw new InvalidOperationException("faster-whisper engine does not exist in the manual path.");
            }
            else if (!File.Exists(FasterWhisperConfig.DefaultEnginePath))
            {
                throw new InvalidOperationException("faster-whisper engine is not downloaded. Please download it from the settings.");
            }

            if (config.Subtitles.FasterWhisperConfig.UseManualModel &&
                !Directory.Exists(config.Subtitles.FasterWhisperConfig.ManualModelDir))
            {
                throw new InvalidOperationException("faster-whisper manual model directory does not exist.");
            }
        }
    }
}
