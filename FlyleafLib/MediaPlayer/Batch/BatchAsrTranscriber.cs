using System.Linq;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public sealed class BatchAsrTranscriber : IBatchAsrTranscriber
{
    private readonly Config _batchConfig;
    private readonly Func<bool>? _preferCpu;
    private readonly bool _preferRussianAudio;
    private readonly Func<string, int?>? _forcedStreamIndexResolver;

    /// <param name="preferCpu">Optional per-chunk device policy for faster-whisper: when it returns true the
    /// NEXT audio chunk is transcribed on CPU instead of GPU (the chunk in flight finishes on its current
    /// device, so nothing computed is lost). Null = always use the configured device.</param>
    /// <param name="preferRussianAudio">When true, a file that has a Russian-tagged audio track has that track
    /// transcribed (so ASR yields Russian and translation is skipped) before falling back to the configured
    /// language order.</param>
    /// <param name="forcedStreamIndexResolver">Optional per-file manual audio-track override: given the media
    /// path, returns the audio stream index to force (or null for automatic selection).</param>
    public BatchAsrTranscriber(
        Config sourceConfig,
        Func<bool>? preferCpu = null,
        bool preferRussianAudio = false,
        Func<string, int?>? forcedStreamIndexResolver = null)
    {
        _batchConfig = BatchSubtitleConfigSnapshot.Create(sourceConfig);
        _preferCpu = preferCpu;
        _preferRussianAudio = preferRussianAudio;
        _forcedStreamIndexResolver = forcedStreamIndexResolver;
        ValidateAsrConfig(_batchConfig);
    }

    public Task<BatchAsrResult> TranscribeAsync(
        string mediaPath,
        CancellationToken token,
        IProgress<BatchAsrProgress>? asrProgress = null)
        => Task.Run(() => Transcribe(mediaPath, token, asrProgress), token);

    private BatchAsrResult Transcribe(string mediaPath, CancellationToken token, IProgress<BatchAsrProgress>? asrProgress)
    {
        int? forced = _forcedStreamIndexResolver?.Invoke(mediaPath);
        MediaAudioProbeResult audio = new MediaAudioProbe(_batchConfig)
            .Probe(mediaPath, token, forced, _preferRussianAudio);

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
        Language sourceLanguage = ResolveInitialSourceLanguage(batchConfig, audio);
        Language selectedAudioLanguage = GetKnownLanguage(audio.Language?.ISO6391);

        using AudioReader reader = new(batchConfig, 0, preferCpu);
        reader.Open(audio.MediaPath, audio.StreamIndex, audio.MediaType, token);
        token.ThrowIfCancellationRequested();

        reader.ReadAll(TimeSpan.Zero, data =>
        {
            if (token.IsCancellationRequested)
                return;

            Language asrLanguage = GetKnownLanguage(data.Language);
            sourceLanguage = ResolveReportedSourceLanguage(sourceLanguage, selectedAudioLanguage, asrLanguage);

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

        subtitles = subtitles.OrderBy(s => s.StartTime).ToList();

        // F-18: normalize ALL-CAPS ASR artifacts to sentence-case (gated). Engine-agnostic, applied before
        // re-segmentation so the split cues carry the corrected casing.
        if (batchConfig.Subtitles.FixAllCaps)
        {
            foreach (SubtitleData s in subtitles)
            {
                if (!string.IsNullOrEmpty(s.Text))
                    s.Text = SubtitleCaseFixer.FixAllCaps(s.Text);
            }
        }

        // Re-segment over-long Whisper cues into short, capped-line cues (proportional timings) so a single
        // subtitle does not fill the frame. Engine-agnostic, gated by the config toggle. Cues that already fit
        // pass through unchanged.
        if (batchConfig.Subtitles.ResegmentSubtitles)
        {
            SubtitleSegmentOptions segOpt = batchConfig.Subtitles.SubtitleSegmentOptions;
            subtitles = subtitles
                .SelectMany(s => SubtitleSegmenter
                    .Resegment(s.Text ?? string.Empty, s.StartTime, s.EndTime, segOpt)
                    .Select(c => new SubtitleData
                    {
                        Text = c.Text,
                        StartTime = c.Start,
                        EndTime = c.End,
#if DEBUG
                        ChunkNo = s.ChunkNo,
                        StartTimeChunk = s.StartTimeChunk,
                        EndTimeChunk = s.EndTimeChunk,
#endif
                    }))
                .ToList();
        }

        for (int i = 0; i < subtitles.Count; i++)
        {
            subtitles[i].Index = i;
        }

        return new BatchAsrResult(subtitles, sourceLanguage);
    }

    internal static Language ResolveInitialSourceLanguage(Config batchConfig, MediaAudioProbeResult audio)
    {
        if (UsesEnglishOnlyModel(batchConfig))
            return Language.English;

        Language audioLanguage = GetKnownLanguage(audio.Language?.ISO6391);
        if (audioLanguage != Language.Unknown)
            return audioLanguage;

        return GetInitialSourceLanguage(batchConfig);
    }

    internal static Language ResolveReportedSourceLanguage(
        Language current,
        Language selectedAudioLanguage,
        Language asrReportedLanguage)
    {
        if (selectedAudioLanguage != Language.Unknown)
        {
            if (current != Language.Unknown && current != selectedAudioLanguage)
                return current;

            return selectedAudioLanguage;
        }

        return asrReportedLanguage != Language.Unknown ? asrReportedLanguage : current;
    }

    private static Language GetInitialSourceLanguage(Config batchConfig)
    {
        if (UsesEnglishOnlyModel(batchConfig))
            return Language.English;

        if (!batchConfig.Subtitles.WhisperConfig.LanguageDetection)
            return GetKnownLanguage(batchConfig.Subtitles.WhisperConfig.Language);

        return Language.Unknown;
    }

    private static bool UsesEnglishOnlyModel(Config batchConfig)
    {
        return (batchConfig.Subtitles.ASREngine == SubASREngineType.WhisperCpp &&
                batchConfig.Subtitles.WhisperCppConfig.IsEnglishModel)
               || (batchConfig.Subtitles.ASREngine == SubASREngineType.FasterWhisper &&
                   batchConfig.Subtitles.FasterWhisperConfig.IsEnglishModel);
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
            // Same VC++ preflight as the interactive path (SubtitlesASR.CanExecute): whisper.cpp loads its
            // native runtime in-process, so without the redistributable the load aborts the whole process.
            // Surfacing it as a per-file InvalidOperationException fails just this batch file with a clear
            // message instead of crashing the app. faster-whisper (external exe) is not checked.
            if (!VcRedistChecker.IsRuntimePresent(out _))
                throw new InvalidOperationException(VcRedistChecker.BuildMissingMessage("Speech-to-text (whisper.cpp)"));

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
