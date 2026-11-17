using System.Collections.ObjectModel;
using System.ComponentModel;
using FlyleafLib.MediaPlayer.Batch;

namespace LLPlayer.Core.Batch;

/// <summary>Batch workflow state and actions consumed by the desktop view.</summary>
public interface IBatchWorkspace : INotifyPropertyChanged, IAsyncDisposable
{
    BatchSettings Settings { get; }
    ObservableCollection<BatchSubtitleJob> Jobs { get; }
    bool WatchFolder { get; }
    bool IsOpen { get; }
    bool IsRunning { get; }
    bool LastRunCanceled { get; }
    bool IsScanning { get; }
    bool IsRunningOnCpu { get; }
    bool IsIdle { get; }
    bool KeepAlive { get; }
    bool CanScan { get; }
    bool CanStart { get; }
    bool CanCancel { get; }
    bool CanRetryFailed { get; }
    string Message { get; }
    string Summary { get; }
    double OverallProgress { get; }
    string EtaText { get; }
    BatchSubtitleJob? ActiveJob { get; }
    event Action? Changed;
    void Open();
    void Closed();
    void SetMessage(string message);
    Task ApplyWorkspaceSettingsAsync(BatchSettings settings, bool updateWatcher);
    Task ScanAsync();
    Task StartAsync();
    Task RetryAsync(BatchSubtitleJob job);
    Task RetryFailedAsync();
    void Cancel();
    void BeginShutdown();
    void ResumeAfterFailedShutdown();
    Task DrainAsync();
    void Tick();
}

/// <summary>A detached, per-run session; disposal releases any local TTS sidecar it owns.</summary>
public interface IBatchRun : IAsyncDisposable
{
    bool IsRunningOnCpu { get; }
    bool IsWaitingForGpu => false;
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
