using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using FlyleafLib.MediaPlayer.Translation;
using LLPlayer.ControlApi;
using LLPlayer.Core.Services;

namespace LLPlayer.Core.Automation;

/// <summary>Runs detached language work using the host's model files and GPU admission.</summary>
public sealed class ControlWorkflows
{
    internal delegate Task<BatchAsrResult> Transcribe(Config config, string mediaPath, int? streamIndex,
        TimeSpan startTime, int? slot, PauseToken pause, IProgress<BatchAsrProgress>? progress,
        CancellationToken token, Language? sourceLanguage);

    readonly Func<Config> captureConfig;
    readonly Action ensureEngine;
    readonly GpuWorkCoordinator gpu;
    readonly LanguageResourceCoordinator resources;
    readonly Transcribe transcribe;
    readonly string? managedWhisperDirectory;

    public ControlWorkflows(Func<Config> captureConfig, Action ensureEngine, GpuWorkCoordinator gpu,
        LanguageResourceCoordinator resources, string? managedWhisperDirectory = null)
        : this(captureConfig, ensureEngine, gpu, resources, RunAsrAsync, managedWhisperDirectory) { }

    internal ControlWorkflows(Func<Config> captureConfig, Action ensureEngine, GpuWorkCoordinator gpu,
        LanguageResourceCoordinator resources, Transcribe transcribe, string? managedWhisperDirectory = null)
    {
        this.captureConfig = captureConfig;
        this.ensureEngine = ensureEngine;
        this.gpu = gpu;
        this.resources = resources;
        this.transcribe = transcribe;
        this.managedWhisperDirectory = managedWhisperDirectory;
    }

    public Task<MediaAudioProbeResult> ProbeAsync(string mediaPath, int? streamIndex, CancellationToken token)
    {
        RequireLocalFile(mediaPath);
        return Task.Run(() =>
        {
            ensureEngine();
            Config snapshot = CaptureLocalConfig();
            try { return new MediaAudioProbe(snapshot).Probe(mediaPath, token, streamIndex, strictSource: true); }
            catch (MediaAudioSelectionException error) { throw AudioSelectionError(error); }
        }, token);
    }

    public async Task<BatchAsrResult> TranscribeAsync(string mediaPath, int? streamIndex, SubASREngineType? engine,
        TimeSpan startTime, int? slot, PauseToken pause, IProgress<BatchAsrProgress>? progress, CancellationToken token,
        Action<bool>? waitingForResource = null, Config? savedConfig = null, Language? sourceLanguage = null)
    {
        RequireLocalFile(mediaPath);
        if (streamIndex < 0) throw new ArgumentOutOfRangeException(nameof(streamIndex));
        if (startTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(startTime));
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        if (engine.HasValue && !Enum.IsDefined(engine.Value)) throw new ArgumentOutOfRangeException(nameof(engine));
        token.ThrowIfCancellationRequested();
        ensureEngine();
        Config snapshot = CaptureLocalConfig(savedConfig);
        if (engine.HasValue) snapshot.Subtitles.ASREngine = engine.Value;

        try
        {
            return await RunOwnedAsync("Speech recognition", FasterWhisperDevicePolicy.RequiresGpuAdmission(snapshot),
                () => transcribe(snapshot, mediaPath, streamIndex, startTime, slot, pause, progress, token, sourceLanguage),
                token, waitingForResource).ConfigureAwait(false);
        }
        catch (MediaAudioSelectionException error) { throw AudioSelectionError(error); }
    }

    public Task RenderDubbingAsync(IReadOnlyList<SubtitleData> cues, string mediaPath, string stagedOutputPath,
        IProgress<DubbingProgress>? progress, CancellationToken token, Action<bool>? waitingForResource = null,
        Config? savedConfig = null)
    {
        RequireLocalFile(mediaPath);
        if (!Path.IsPathFullyQualified(stagedOutputPath) || File.Exists(stagedOutputPath))
            throw new IOException("Dubbing requires an unused, absolute staging path.");
        if (cues.All(cue => string.IsNullOrWhiteSpace(cue.TranslatedText)))
            throw new ArgumentException("Translate the cues before rendering a Russian dub.", nameof(cues));

        Config snapshot = CaptureLocalConfig(savedConfig);
        return RunOwnedAsync("Dubbing", needsGpu: true, async () =>
        {
            await using IDubbingRenderer renderer = DubbingWorkflow.CreateRenderer(snapshot.Subtitles.DubbingConfig,
                confirmProcessExit: true);
            await renderer.RenderAsync(cues, mediaPath, stagedOutputPath, progress, token).ConfigureAwait(false);
            if (!File.Exists(stagedOutputPath)) throw new IOException("The dubbing engine did not produce an output file.");
            return true;
        }, token, waitingForResource);
    }

    async Task<T> RunOwnedAsync<T>(string subject, bool needsGpu, Func<Task<T>> run, CancellationToken token,
        Action<bool>? waitingForResource)
    {
        string operation = $"{subject} ({Guid.NewGuid():N})";
        GpuWorkLease? permit = null;
        IDisposable? modelLease = null;
        try
        {
            if (needsGpu)
            {
                using var admission = CancellationTokenSource.CreateLinkedTokenSource(token);
                Task<GpuWorkLease> acquisition = gpu.AcquireAsync(operation, admission.Token).AsTask();
                bool waiting = !acquisition.IsCompleted;
                try
                {
                    if (waiting) waitingForResource?.Invoke(true);
                    permit = await acquisition.ConfigureAwait(false);
                }
                catch
                {
                    admission.Cancel();
                    try { (await acquisition.ConfigureAwait(false)).Complete(); }
                    catch (OperationCanceledException) { }
                    throw;
                }
                if (waiting) waitingForResource?.Invoke(false);
            }

            token.ThrowIfCancellationRequested();
            modelLease = resources.AcquireUse();
            T result = await run().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (EngineProcessDrainException failure)
        {
            IDisposable? heldModelLease = modelLease;
            modelLease = null;
            async ValueTask ConfirmStopAsync()
            {
                await failure.RetryStop().ConfigureAwait(false);
                heldModelLease?.Dispose();
            }

            if (permit == null)
                throw new EngineProcessDrainException(failure.Message, failure, ConfirmStopAsync);

            permit.RequireDrain(ConfirmStopAsync);
            throw new EngineProcessDrainException(failure.Message, failure,
                async () => { await gpu.RetryDrainAsync(operation).ConfigureAwait(false); });
        }
        finally
        {
            try { modelLease?.Dispose(); }
            finally { permit?.Complete(); }
        }
    }

    public Task TranslateDocumentAsync(IList<SubtitleData> cues, Language source, TargetLanguage target,
        IProgress<(int Completed, int Total)>? progress, CancellationToken token, Config? savedConfig = null)
    {
        Config snapshot = savedConfig ?? captureConfig();
        return new BatchSubtitleTranslator(snapshot.Subtitles).TranslateDocumentAsync(cues, source, target, progress, token);
    }

#if !WINDOWS
    public Task<BatchAsrResult> RecognizeBitmapAsync(string mediaPath, int streamIndex, bool external, int slot,
        Language? languageOverride, IProgress<int>? progress, CancellationToken token, Config? savedConfig = null)
    {
        RequireLocalFile(mediaPath);
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));

        return Task.Run(() =>
        {
            ensureEngine();
            Config config = CaptureLocalConfig(savedConfig);
            using IDisposable modelLease = resources.AcquireUse();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var reader = new SubtitleReader(new SubManager(config, slot), config, slot);
            try { reader.Open(mediaPath, streamIndex, external ? MediaType.Subs : MediaType.Video, cancellation.Token); }
            catch (InvalidDataException error) { throw new ControlException(new("stale_track", error.Message)); }
            if (!reader.IsBitmap)
                throw new ControlException(new("not_bitmap_track", "The selected subtitle track does not contain bitmap cues."));

            Language language = languageOverride ?? reader.TrackLanguage;
            if (language == Language.Unknown)
                language = slot == 0 ? config.Subtitles.LanguageFallbackPrimary : config.Subtitles.LanguageFallbackSecondary;
            SubOCREngineType engine = config.Subtitles[slot].OCREngine;
            using IOCRService ocr = ControlLanguageRuntime.CreateOcrService(config.Subtitles, engine)
                ?? throw new PlatformNotSupportedException($"Bitmap subtitle OCR ({engine}) is unavailable on this platform.");
            if (!ocr.TryInitialize(language, out string initializationError))
                throw new InvalidOperationException(initializationError);

            List<SubtitleData> cues = [];
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
            try
            {
                reader.ReadAll(useBitmap: true, cue =>
                {
                    using (cue)
                    {
                        try
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            if (cue.Bitmap != null) cue.Text = SubtitlesOCR.Process(ocr, slot, cue.Bitmap);
                            cue.Index = cues.Count;
                            cue.Language = language;
                            cues.Add(cue);
                            progress?.Report(cues.Count);
                        }
                        catch (Exception error)
                        {
                            // Let the subtitle reader dispose any next bitmap already decoded before this callback.
                            failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error);
                            cancellation.Cancel();
                        }
                    }
                }, cancellation.Token);
            }
            catch (OperationCanceledException) when (failure != null) { failure.Throw(); }

            failure?.Throw();
            token.ThrowIfCancellationRequested();
            return new BatchAsrResult(cues, language);
        }, token);
    }
#endif

    internal Config CaptureLocalConfig(Config? savedConfig = null)
    {
        Config snapshot = BatchSubtitleConfigSnapshot.CreateForRecognition(savedConfig ?? captureConfig());
        WhisperCppConfig whisper = snapshot.Subtitles.WhisperCppConfig;
        if (managedWhisperDirectory != null && whisper.Model is { } model &&
            string.IsNullOrWhiteSpace(whisper.ModelDirectory) && File.Exists(Path.Combine(managedWhisperDirectory, model.ModelFileName)))
            whisper.ModelDirectory = managedWhisperDirectory;
        snapshot.Demuxer.FormatOptToUnderlying = true;
        foreach (var options in new[] { snapshot.Demuxer.FormatOpt, snapshot.Demuxer.AudioFormatOpt,
                     snapshot.Demuxer.SubtitlesFormatOpt })
            options["protocol_whitelist"] = "file";
        return snapshot;
    }

    static void RequireLocalFile(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new FileNotFoundException("An existing local media file is required.");
    }

    static ControlException AudioSelectionError(MediaAudioSelectionException error) => new(new(
        error.Failure == MediaAudioSelectionFailure.StaleTrack ? "stale_track" : "live_source", error.Message));

    static Task<BatchAsrResult> RunAsrAsync(Config config, string mediaPath, int? streamIndex,
        TimeSpan startTime, int? slot, PauseToken pause, IProgress<BatchAsrProgress>? progress,
        CancellationToken token, Language? sourceLanguage) =>
        new BatchAsrTranscriber(config, confirmProcessExit: true, forRecognition: slot.HasValue)
            .TranscribeSelectedAsync(mediaPath, streamIndex, startTime, slot, pause, token, progress, sourceLanguage);
}
