using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;
using LLPlayer.ControlApi;
using LLPlayer.Core.Batch;

namespace LLPlayer.Avalonia.Services.Automation;

internal sealed record ControlBatchVoiceCue(long StartTicks, long EndTicks, string? Text, string? VoiceId);
internal sealed record ControlBatchVoiceSnapshot(string MediaPath, IReadOnlyList<ControlBatchVoiceCue> Cues);

/// <summary>The batch window displays and edits the host's queue. Closing the window only stops observation.</summary>
internal sealed class ControlBatchWorkspace : NotifyPropertyChanged, IBatchWorkspace
{
    static readonly ControlScope Scope = new(ControlProtocol.DefaultProfileId);
    static readonly JsonSerializerOptions Responses = new(ControlProtocol.Json)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };
    readonly AppPaths paths;
    readonly BatchSettingsStore store;
    readonly Func<ControlBatchVoiceSnapshot?> captureVoices;
    readonly CancellationTokenSource observing = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly SemaphoreSlim readGate = new(1);
    readonly Dictionary<string, BatchSubtitleJob> known = new(BatchWorkflow.PathComparer);
    Task pendingEdits = Task.CompletedTask;
    Task refreshing = Task.CompletedTask;
    Exception? failedEdit;
    bool applyingSnapshot, disposed, shuttingDown, admitting;
    Guid? admittedJob;
    BatchState? snapshot;
    WatchState? watcher;

    public ControlBatchWorkspace(AppPaths paths, BatchSettingsStore store, BatchSettings initial,
        Func<ControlBatchVoiceSnapshot?> captureVoices)
    {
        this.paths = paths;
        this.store = store;
        this.captureVoices = captureVoices;
        Settings = initial.Clone();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
    }

    public BatchSettings Settings { get; private set; }
    public ObservableCollection<BatchSubtitleJob> Jobs { get; } = [];
    public bool WatchFolder => watcher?.Active == true;
    public bool IsOpen { get; private set; }
    public bool IsRunning => snapshot?.IsRunning == true;
    public bool LastRunCanceled => snapshot?.LastRunCanceled == true;
    public bool IsScanning => snapshot?.IsScanning == true;
    public bool IsRunningOnCpu => snapshot?.IsRunningOnCpu == true;
    public bool IsIdle => !IsRunning && !IsScanning && !admitting;
    public bool KeepAlive => !disposed && (IsOpen || IsRunning || IsScanning || WatchFolder);
    public bool CanScan => !shuttingDown && IsIdle && Directory.Exists(Settings.LastFolder);
    public bool CanStart => !shuttingDown && IsIdle && Jobs.Any(job => job.Include);
    public bool CanCancel => admittedJob.HasValue || snapshot?.CanCancel == true || IsRunning || IsScanning;
    public bool CanRetryFailed => !shuttingDown && IsIdle && (snapshot?.CanRetryFailed ?? Jobs.Any(job => job.Status == BatchSubtitleStatus.Failed));
    public string Message { get; private set; } = "";
    public string Summary => snapshot?.Summary ?? $"{Jobs.Count} files";
    public double OverallProgress => snapshot?.Progress ?? 0;
    public string EtaText => snapshot?.EtaText ?? "";
    public string ActivityStatus => snapshot?.ActivityStatus ?? "Batch runs in the control host.";
    public BatchSubtitleJob? ActiveJob => snapshot?.ActiveMediaPath is { } path && known.TryGetValue(path, out var job) ? job : null;
    public event Action? Changed;

    public void Open()
    {
        IsOpen = true;
        Notify();
        Tick();
    }

    public void Closed() { IsOpen = false; Notify(); }
    public void SetMessage(string message) { Message = message; Notify(); }
    public void ReloadSavedSettings() { Settings = store.Load(); Notify(); }

    public async Task ApplyWorkspaceSettingsAsync(BatchSettings settings, bool updateWatcher)
    {
        ReloadSavedSettings();
        if (!updateWatcher) return;
        await RefreshAsync();
        if (settings.WatchFolder)
        {
            if (!WatchFolder || watcher!.Folder != Settings.LastFolder || watcher.Recursive != Settings.Recursive)
                await RunJobAsync("watch.start", new FolderArgs(Settings.LastFolder, Settings.Recursive), trackAdmission: false);
        }
        else if (WatchFolder)
            await RunJobAsync("watch.stop", new Empty(), trackAdmission: false);
    }

    public Task ScanAsync() => RunBatchCommandAsync("batch.scan", () => new FolderArgs(Settings.LastFolder, Settings.Recursive));
    public Task StartAsync() => RunBatchCommandAsync("batch.start", () => new StartBatch(Settings.GenerateDubbing ? captureVoices() : null));
    public Task RetryAsync(BatchSubtitleJob job) => RetryCoreAsync(job.MediaPath);
    public Task RetryFailedAsync() => RetryCoreAsync(null);
    Task RetryCoreAsync(string? mediaPath) => RunBatchCommandAsync("batch.retry",
        () => new RetryBatch(mediaPath, Settings.GenerateDubbing ? captureVoices() : null));

    async Task RunBatchCommandAsync(string operation, Func<object> arguments)
    {
        if (disposed || shuttingDown || admitting) return;
        admitting = true;
        Notify();
        try
        {
            await pendingEdits;
            if (failedEdit is { } editError)
            {
                failedEdit = null;
                await RefreshAsync();
                throw new ControlException(new("batch_selection_failed",
                    $"Review the refreshed queue before running it. {ControlGuiExecutor.PublicError(editError)}"));
            }
            await RunJobAsync(operation, arguments(), trackAdmission: true);
        }
        catch (Exception error) { Handle(error); }
        finally { admittedJob = null; admitting = false; Notify(); }
    }

    public void Cancel() => _ = CancelAsync();

    async Task CancelAsync()
    {
        try
        {
            if (admittedJob is not { } jobId)
                await RunJobAsync("batch.cancel", new Empty(), trackAdmission: false);
            else
            {
                using ControlClient client = await ConnectAsync();
                Require(await client.CancelAsync(new(jobId, Scope), observing.Token));
                await RefreshAsync();
            }
        }
        catch (Exception error) { Handle(error); }
    }

    async Task RunJobAsync(string operation, object args, bool trackAdmission)
    {
        CancellationToken token = observing.Token;
        using ControlClient client = await ConnectAsync();
        ControlResult accepted = await InvokeAsync(client, operation, args, token);
        Guid id = accepted.JobId ?? throw new InvalidOperationException("The batch host did not return a job.");
        if (trackAdmission) { admittedJob = id; Notify(); }
        while (true)
        {
            ControlJob job = await client.GetAsync(Scope, id, token)
                ?? throw new InvalidOperationException("The batch job was not found in the control host.");
            if (job.Status == ControlJobStatus.Completed) break;
            if (job.Status == ControlJobStatus.Cancelled) break;
            if (job.Status is ControlJobStatus.Failed or ControlJobStatus.Interrupted)
                throw new ControlException(job.Error ?? new("batch_failed", $"Batch ended with status {job.Status}."));
            await Task.Delay(TimeSpan.FromMilliseconds(400), token);
        }
        await RefreshAsync();
    }

    void OnFileEdited(object? sender, PropertyChangedEventArgs args)
    {
        if (applyingSnapshot || disposed || shuttingDown || sender is not BatchSubtitleJob file) return;
        if (args.PropertyName == nameof(BatchSubtitleJob.Include))
            pendingEdits = SendEditAsync(pendingEdits, "batch.set_included", new IncludeArgs(file.MediaPath, file.Include));
        if (args.PropertyName == nameof(BatchSubtitleJob.AudioStreamIndexOverride))
            pendingEdits = SendEditAsync(pendingEdits, "batch.select_audio", new AudioChoice(file.MediaPath, file.AudioStreamIndexOverride));
        Notify();
    }

    async Task SendEditAsync(Task previous, string operation, object request)
    {
        try
        {
            await previous;
            await RunJobAsync(operation, request, trackAdmission: false);
        }
        catch (Exception error) { failedEdit = error; Handle(error); }
    }

    public void Tick()
    {
        if (disposed || !refreshing.IsCompleted) return;
        refreshing = RefreshQuietlyAsync();
    }

    async Task RefreshQuietlyAsync()
    {
        try { await RefreshAsync(); }
        catch (Exception error) { Handle(error); }
    }

    async Task RefreshAsync()
    {
        CancellationToken token = observing.Token;
        await readGate.WaitAsync(token);
        try
        {
            using ControlClient client = await ConnectAsync();
            BatchState current = Read<BatchState>(await InvokeAsync(client, "batch.status", new BatchPage(0), token));
            int? offset = current.NextOffset;
            var files = current.Files.ToList();
            while (offset.HasValue)
            {
                BatchState next = Read<BatchState>(await InvokeAsync(client, "batch.status", new BatchPage(offset.Value), token));
                if (next.NextOffset.HasValue && next.NextOffset <= offset) throw new InvalidOperationException("Invalid batch cursor.");
                files.AddRange(next.Files);
                offset = next.NextOffset;
            }
            watcher = Read<WatchState>(await InvokeAsync(client, "watch.get", new Empty(), token));
            snapshot = current;
            if (current.Message.Length > 0) Message = current.Message;
            ApplyFiles(files);
            Notify();
        }
        finally { readGate.Release(); }
    }

    void ApplyFiles(IReadOnlyList<FileState> files)
    {
        applyingSnapshot = true;
        try
        {
            var seen = new HashSet<string>(BatchWorkflow.PathComparer);
            foreach (FileState file in files)
            {
                if (!seen.Add(file.MediaPath)) continue;
                if (!known.TryGetValue(file.MediaPath, out BatchSubtitleJob? job))
                {
                    job = new(file.MediaPath, watcher?.Folder ?? Settings.LastFolder);
                    known.Add(file.MediaPath, job);
                    job.PropertyChanged += OnFileEdited;
                    Jobs.Add(job);
                }
                UpdateAudioChoices(job, file.AudioTracks);
                if (pendingEdits.IsCompleted)
                {
                    job.Include = file.Include;
                    job.AudioStreamIndexOverride = file.StreamIndex;
                }
                job.Status = file.Status; job.Error = file.Error; job.SubtitleCount = file.SubtitleCount; job.Progress = file.Progress;
                job.StartedAt = file.StartedAt; job.CompletedAt = file.CompletedAt; job.Elapsed = file.Elapsed; job.Eta = file.Eta;
                job.ProcessedTime = file.ProcessedTime; job.TotalDuration = file.TotalDuration;
                job.IsIndeterminateProgress = file.IsIndeterminateProgress; job.Throughput = file.Throughput ?? "";
                if (file.Transcript != null && !job.Transcript.SequenceEqual(file.Transcript))
                {
                    job.Transcript.Clear();
                    foreach (string line in file.Transcript) job.Transcript.Add(line);
                }
            }
            foreach (string removed in known.Keys.Where(path => !seen.Contains(path)).ToArray())
            {
                BatchSubtitleJob job = known[removed];
                job.PropertyChanged -= OnFileEdited;
                Jobs.Remove(job);
                known.Remove(removed);
            }
        }
        finally { applyingSnapshot = false; }
    }

    static void UpdateAudioChoices(BatchSubtitleJob job, IReadOnlyList<AudioTrack> tracks)
    {
        if (job.AudioTracks.Count == tracks.Count + 1 && job.AudioTracks.Skip(1).Select(track => track.StreamIndex)
            .SequenceEqual(tracks.Select(track => track.StreamIndex))) return;
        job.AudioTracks.Clear();
        job.AudioTracks.Add(new MediaAudioTrack(-1, Language.Unknown, null, null, 0));
        foreach (AudioTrack track in tracks)
            job.AudioTracks.Add(new MediaAudioTrack(track.StreamIndex, Language.Get(track.Language), track.Title,
                track.Codec, track.Channels, track.BitRate));
    }

    Task<ControlClient> ConnectAsync() => ControlClient.ConnectOrStartAsync(paths.ConfigDir, paths.StateDir, observing.Token);

    static async Task<ControlResult> InvokeAsync(ControlClient client, string operation, object args, CancellationToken token)
    {
        ControlResult result = await client.InvokeAsync(new(Guid.NewGuid(), operation, Scope, null, ControlProtocol.Encode(args)), token);
        Require(result);
        return result;
    }

    static T Read<T>(ControlResult result) where T : class => result.Value?.Deserialize<T>(Responses)
        ?? throw new InvalidOperationException("The batch host did not return a snapshot.");

    static void Require(ControlResult result)
    {
        if (result.Error is { } error) throw new ControlException(error);
    }

    void Handle(Exception error)
    {
        if (!disposed && error is not OperationCanceledException) SetMessage(ControlGuiExecutor.PublicError(error));
    }

    void Notify()
    {
        Raise("");
        Changed?.Invoke();
    }

    public void BeginShutdown() { shuttingDown = true; Notify(); }
    public void ResumeAfterFailedShutdown() { shuttingDown = false; Notify(); }
    public Task DrainAsync() => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        await observing.CancelAsync();
        await Task.WhenAll(refreshing, pendingEdits);
        foreach (BatchSubtitleJob job in Jobs) job.PropertyChanged -= OnFileEdited;
        observing.Dispose();
    }

    sealed record Empty { }
    sealed record FolderArgs(string Folder, bool Recursive);
    sealed record BatchPage(int Offset, int Limit = 100);
    sealed record StartBatch(ControlBatchVoiceSnapshot? VoiceAssignments);
    sealed record RetryBatch(string? MediaPath, ControlBatchVoiceSnapshot? VoiceAssignments);
    sealed record IncludeArgs(string MediaPath, bool Include);
    sealed record AudioChoice(string MediaPath, int? StreamIndex);
    sealed record WatchState(bool Active, string Folder, bool Recursive, string Message);
    sealed record BatchState(bool IsRunning, bool IsScanning, bool LastRunCanceled, double Progress, string Message,
        FileState[] Files, int? NextOffset, string? ActiveMediaPath = null, string? Summary = null, string? EtaText = null,
        bool IsRunningOnCpu = false, bool? CanCancel = null, bool? CanRetryFailed = null, string? ActivityStatus = null);
    sealed record FileState(string MediaPath, string OutputPath, bool Include, int? StreamIndex,
        BatchSubtitleStatus Status, string? Error, int SubtitleCount, double Progress, AudioTrack[] AudioTracks,
        DateTimeOffset? StartedAt = null, DateTimeOffset? CompletedAt = null, TimeSpan? Elapsed = null, TimeSpan? Eta = null,
        TimeSpan? ProcessedTime = null, TimeSpan? TotalDuration = null, bool IsIndeterminateProgress = false,
        string? Throughput = null, string[]? Transcript = null);
    sealed record AudioTrack(int StreamIndex, string Language, string? Title, string Display, string? Codec = null,
        int Channels = 0, long BitRate = 0);
}
