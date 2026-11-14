using System.Text;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;

namespace LLPlayer.Core.Batch;

/// <summary>Captures engine configuration and per-file selection before any worker starts.</summary>
public sealed class BatchRunFactory(Func<Config> getConfig, Func<int, bool>? isUserActive = null,
    Func<Config, IDubbingRenderer?>? createDubbingRenderer = null,
    Func<IDubbingVoiceAssignmentProvider?>? snapshotVoiceAssignments = null,
    Func<IDisposable>? acquireRunLease = null, Func<bool>? getUtf8Bom = null,
    GpuWorkCoordinator? gpuCoordinator = null) : IBatchRunFactory
{
    public IBatchRun Create(BatchSettings settings, IReadOnlyList<BatchSubtitleJob> jobs)
    {
        IDisposable? lease = gpuCoordinator == null ? acquireRunLease?.Invoke() : null;
        try
        {
            Config snapshot = BatchSubtitleConfigSnapshot.Create(getConfig());
            var options = CaptureOptions(settings, snapshot.Subtitles.DubbingConfig.OutputFormat);
            var overrides = jobs.ToDictionary(j => j.MediaPath, j => j.AudioStreamIndexOverride, BatchWorkflow.PathComparer);
            Func<bool> preferCpu = () => settings.RunOnCpuWhenActive && isUserActive?.Invoke(settings.ActiveIdleThresholdSeconds) == true;
            bool coordinateGpu = gpuCoordinator != null &&
                (settings.GenerateDubbing || FasterWhisperDevicePolicy.RequiresGpuAdmission(snapshot));
            IBatchAsrTranscriber CreateTranscriber() => new BatchAsrTranscriber(snapshot, preferCpu,
                settings.PreferRussianAudio, path => overrides.GetValueOrDefault(path), confirmProcessExit: coordinateGpu);
            IBatchAsrTranscriber? transcriber = gpuCoordinator == null ? CreateTranscriber() : null;
            var translator = new BatchSubtitleTranslator(snapshot.Subtitles);
            var assignments = settings.GenerateDubbing ? snapshotVoiceAssignments?.Invoke() : null;
            IDubbingRenderer? renderer = settings.GenerateDubbing
                ? createDubbingRenderer?.Invoke(snapshot) ?? throw new InvalidOperationException("Dubbing is not configured. Open dubbing settings before starting the batch.") : null;
            return new Run(jobs, () => transcriber ?? CreateTranscriber(), translator,
                new SrtSubtitleWriter(new UTF8Encoding(options.Utf8Bom)), options, renderer, assignments,
                () => snapshot.Subtitles.ASREngine == SubASREngineType.FasterWhisper && preferCpu(), lease,
                gpuCoordinator == null ? null : acquireRunLease, coordinateGpu ? gpuCoordinator : null);
        }
        catch { lease?.Dispose(); throw; }
    }
    internal BatchSubtitleOptions CaptureOptions(BatchSettings settings, string dubbingOutputFormat) => new()
    {
        Recursive = settings.Recursive, OverwriteExisting = settings.OverwriteExisting,
        // The saved playback/export owner is read once at this run boundary; batch has no persisted BOM setting.
        Utf8Bom = getUtf8Bom?.Invoke() ?? true,
        SerializeAsrAndTranslate = settings.SerializeAsrAndTranslate, GenerateDubbing = settings.GenerateDubbing,
        DubbingOutputFormat = dubbingOutputFormat
    };
    sealed class Run(IReadOnlyList<BatchSubtitleJob> jobs, Func<IBatchAsrTranscriber> createTranscriber, IBatchSubtitleTranslator translator,
        IBatchSubtitleWriter writer, BatchSubtitleOptions options, IDubbingRenderer? renderer,
        IDubbingVoiceAssignmentProvider? assignments, Func<bool> cpu, IDisposable? resourceLease,
        Func<IDisposable>? acquireResource, GpuWorkCoordinator? coordinator) : IBatchRun
    {
        IDisposable? models = resourceLease;
        GpuWorkLease? gpuLease;
        EngineProcessDrainException? uncertainStop;
        volatile bool waitingForGpu;
        bool disposed;

        public bool IsRunningOnCpu => cpu();
        public bool IsWaitingForGpu => waitingForGpu;

        public async Task ProcessAsync(IProgress<BatchSubtitleProgress> progress, CancellationToken token)
        {
            if (coordinator != null)
            {
                ValueTask<GpuWorkLease> admission = coordinator.AcquireAsync("Batch subtitles", token);
                waitingForGpu = !admission.IsCompleted;
                try { gpuLease = await admission.ConfigureAwait(false); }
                finally { waitingForGpu = false; }
            }

            token.ThrowIfCancellationRequested();
            // Engine paths from the detached snapshot are checked by the transcriber after taking the model lease.
            models ??= acquireResource?.Invoke();
            try
            {
                await Task.Run(() =>
                    new BatchSubtitleProcessor(createTranscriber(), translator, writer, options, progress, renderer, assignments)
                        .ProcessAsync(jobs, token), CancellationToken.None).ConfigureAwait(false);
            }
            catch (EngineProcessDrainException error)
            {
                uncertainStop = error;
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            if (gpuLease == null)
            {
                try { if (renderer != null) await renderer.DisposeAsync().ConfigureAwait(false); }
                finally { models?.Dispose(); }
                return;
            }

            try { await ConfirmStopAsync().ConfigureAwait(false); }
            catch
            {
                // Retain this run's model lease and its retryable cleanup after the workflow retires the UI run.
                gpuLease.RequireDrain(ConfirmStopAsync);
                throw;
            }
            finally { gpuLease.Complete(); }
        }

        async ValueTask ConfirmStopAsync()
        {
            try
            {
                if (renderer != null) await renderer.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (uncertainStop != null)
                {
                    await uncertainStop.RetryStop().ConfigureAwait(false);
                    uncertainStop = null;
                }
            }
            models?.Dispose();
            models = null;
        }
    }
}

public sealed class FlyleafBatchMediaCatalog(Func<Config> getConfig) : IBatchMediaCatalog
{
    public bool FolderExists(string path) => Directory.Exists(path);
    public bool TranslationExists(string path) => SubtitleOutputPathBuilder.TranslationExists(path);
    public bool DubExists(string path) => DubbingOutputPathBuilder.DubExistsAnyFormat(path);
    public Task<IReadOnlyList<string>> ScanAsync(string folder, bool recursive, CancellationToken token) =>
        Task.Run(() => BatchVideoScanner.Scan(Path.GetFullPath(folder), recursive, token), token);
    public Task<IReadOnlyList<MediaAudioTrack>> ProbeAsync(string path, CancellationToken token)
    {
        Config snapshot = BatchSubtitleConfigSnapshot.Create(getConfig());
        return Task.Run<IReadOnlyList<MediaAudioTrack>>(() => new MediaAudioProbe(snapshot).Probe(path, token).Tracks, token);
    }
}
