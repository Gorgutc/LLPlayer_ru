using FlyleafLib.MediaPlayer.Batch;

namespace LLPlayer.Core.Batch;

/// <summary>A detached, per-run session; disposal releases any local TTS sidecar it owns.</summary>
public interface IBatchRun : IAsyncDisposable
{
    bool IsRunningOnCpu { get; }
    Task ProcessAsync(IProgress<BatchSubtitleProgress> progress, CancellationToken token);
}

public interface IBatchRunFactory
{
    IBatchRun Create(BatchSettings settings, IReadOnlyList<BatchSubtitleJob> jobs);
}

public interface IBatchFolderWatcher : IDisposable
{
    event Action<BatchWatchReady>? FileReady;
    event Action<BatchWatchError>? Error;
    long Start(string path, bool recursive);
    void Stop();
    Task DrainAsync() => Task.CompletedTask;
    async ValueTask DisposeAsync() { Dispose(); await DrainAsync(); }
}

public sealed record BatchWatchReady(long SessionId, IReadOnlyList<string> Paths);
public sealed record BatchWatchError(long SessionId, string Message);

public interface IBatchMediaCatalog
{
    bool FolderExists(string path);
    bool TranslationExists(string path);
    bool DubExists(string path);
    Task<IReadOnlyList<string>> ScanAsync(string folder, bool recursive, CancellationToken token);
    Task<IReadOnlyList<MediaAudioTrack>> ProbeAsync(string path, CancellationToken token);
}
