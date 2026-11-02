using System.Text;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;

namespace LLPlayer.Core.Batch;

/// <summary>Captures engine configuration and per-file selection before any worker starts.</summary>
public sealed class BatchRunFactory(Func<Config> getConfig, Func<int, bool>? isUserActive = null,
    Func<Config, IDubbingRenderer?>? createDubbingRenderer = null,
    Func<IDubbingVoiceAssignmentProvider?>? snapshotVoiceAssignments = null,
    Func<IDisposable>? acquireRunLease = null, Func<bool>? getUtf8Bom = null) : IBatchRunFactory
{
    public IBatchRun Create(BatchSettings settings, IReadOnlyList<BatchSubtitleJob> jobs)
    {
        IDisposable? lease = acquireRunLease?.Invoke();
        try
        {
            Config snapshot = BatchSubtitleConfigSnapshot.Create(getConfig());
            var options = CaptureOptions(settings, snapshot.Subtitles.DubbingConfig.OutputFormat);
            var overrides = jobs.ToDictionary(j => j.MediaPath, j => j.AudioStreamIndexOverride, BatchWorkflow.PathComparer);
            Func<bool> preferCpu = () => settings.RunOnCpuWhenActive && isUserActive?.Invoke(settings.ActiveIdleThresholdSeconds) == true;
            var asr = new BatchAsrTranscriber(snapshot, preferCpu, settings.PreferRussianAudio, path => overrides.GetValueOrDefault(path));
            var translator = new BatchSubtitleTranslator(snapshot.Subtitles);
            var assignments = settings.GenerateDubbing ? snapshotVoiceAssignments?.Invoke() : null;
            IDubbingRenderer? renderer = settings.GenerateDubbing
                ? createDubbingRenderer?.Invoke(snapshot) ?? throw new InvalidOperationException("Dubbing is not configured. Open dubbing settings before starting the batch.") : null;
            return new Run(jobs, asr, translator, new SrtSubtitleWriter(new UTF8Encoding(options.Utf8Bom)), options,
                renderer, assignments, () => snapshot.Subtitles.ASREngine == SubASREngineType.FasterWhisper && preferCpu(), lease);
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
    sealed class Run(IReadOnlyList<BatchSubtitleJob> jobs, IBatchAsrTranscriber asr, IBatchSubtitleTranslator translator,
        IBatchSubtitleWriter writer, BatchSubtitleOptions options, IDubbingRenderer? renderer,
        IDubbingVoiceAssignmentProvider? assignments, Func<bool> cpu, IDisposable? lease) : IBatchRun
    {
        public bool IsRunningOnCpu => cpu();
        public Task ProcessAsync(IProgress<BatchSubtitleProgress> progress, CancellationToken token) =>
            Task.Run(() => new BatchSubtitleProcessor(asr, translator, writer, options, progress, renderer, assignments).ProcessAsync(jobs, token), CancellationToken.None);
        public async ValueTask DisposeAsync()
        { try { if (renderer != null) await renderer.DisposeAsync(); } finally { lease?.Dispose(); } }
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
