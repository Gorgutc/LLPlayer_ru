using FlyleafLib;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;

namespace LLPlayer.Core.Services;

public sealed class RecognitionActionCanceledException(string message) : OperationCanceledException(message);

public interface IRecognitionBackend
{
    bool HasAudio { get; }
    bool IsLive { get; }
    bool IsBusy { get; }
    bool IsPaused { get; }
    void Start(int slot);
    void Start(int slot, bool detectAsrOmissions) => Start(slot);
    void Pause();
    void Resume();
    void Cancel(int slot);
    void Ocr(int slot);
    void Ocr(int slot, CancellationToken cancellationToken) => Ocr(slot);
    void Apply(RecognitionSettings settings);
}

internal readonly record struct RecognitionAttempt(Task Started, Task Completion);

internal interface ICoordinatedRecognitionBackend
{
    object CaptureInput();
    Config CaptureSettings();
    bool IsCurrentInput(object input);
    bool CanAttach(int slot, bool detectAsrOmissions);
    void SignalStop();
    RecognitionAttempt Start(int slot, object input, Config settings, bool attachOnly, bool confirmProcessExit,
        CancellationToken cancellationToken, bool detectAsrOmissions);
}

/// <summary>Canonical Flyleaf command adapter; native OCR initialization is invoked by the serialized worker.</summary>
public sealed class PlayerRecognitionBackend(Player player) : IRecognitionBackend, ICoordinatedRecognitionBackend
{
    public bool HasAudio => player.Audio.IsOpened;
    public bool IsLive => player.IsLive;
    public bool IsBusy => player.IsASRRunning || Enumerable.Range(0, 2).Any(slot => player.Subtitles[slot].EnabledASR) || Enumerable.Range(0, 2).Any(slot => player.SubtitlesManager[slot].IsLoading);
    public bool IsPaused => player.IsASRPaused;
    public void Start(int slot) => Start(slot, detectAsrOmissions: false);
    public void Start(int slot, bool detectAsrOmissions)
    {
        if (!player.SubtitlesASR.CanExecute(out string error, out _)) throw new InvalidOperationException(error);
        if (!player.Commands.StartSubtitlesASR(slot, detectAsrOmissions: detectAsrOmissions)) throw StaleInput();
        player.Subtitles[slot].ASRStartup.GetAwaiter().GetResult();
    }
    public void Pause() => player.SubtitlesASR.Pause();
    public void Resume() => player.SubtitlesASR.Resume();
    public void Cancel(int slot) => player.Commands.SubtitlesOffAction(slot.ToString(System.Globalization.CultureInfo.InvariantCulture));

    sealed record Input(long Generation, PlaylistItem? Item, AudioStream? Audio, string? Url);

    object ICoordinatedRecognitionBackend.CaptureInput() => new Input(player.MediaGeneration, player.Playlist?.Selected,
        player.AudioDecoder.AudioStream, player.AudioDecoder.Demuxer.Url);
    Config ICoordinatedRecognitionBackend.CaptureSettings() => BatchSubtitleConfigSnapshot.CreateForRecognition(player.Config);
    bool ICoordinatedRecognitionBackend.IsCurrentInput(object input) => IsCurrent((Input)input);
    bool ICoordinatedRecognitionBackend.CanAttach(int slot, bool detectAsrOmissions)
        => player.SubtitlesASR.CanAttach(slot, detectAsrOmissions);
    void ICoordinatedRecognitionBackend.SignalStop() => player.SubtitlesASR.CancelForRestart();

    bool IsCurrent(Input input) => !player.IsDisposed && !player.IsMediaResetting && player.MediaGeneration == input.Generation
        && ReferenceEquals(player.Playlist?.Selected, input.Item) && ReferenceEquals(player.AudioDecoder.AudioStream, input.Audio)
        && player.AudioDecoder.Demuxer.Url == input.Url;

    RecognitionAttempt ICoordinatedRecognitionBackend.Start(int slot, object input, Config settings, bool attachOnly,
        bool confirmProcessExit, CancellationToken cancellationToken, bool detectAsrOmissions)
    {
        Input expected = (Input)input;
        RecognitionAttempt attempt = default;
        void Launch()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!player.SubtitlesASR.CanExecute(out string error, out _, settings)) throw new InvalidOperationException(error);
            if (!player.Commands.StartSubtitlesASR(slot, settings, attachOnly, confirmProcessExit, cancellationToken, detectAsrOmissions))
                throw StaleInput();
            attempt = new(player.Subtitles[slot].ASRStartup, player.Subtitles[slot].ASRCompletion);
        }
#if WINDOWS
        if (!IsCurrent(expected)) throw StaleInput();
        Launch();
#else
        if (expected.Item == null || !player.TryMutateMedia(expected.Generation, expected.Item, () => IsCurrent(expected),
            Launch, cancellationToken)) throw StaleInput();
#endif
        return attempt;
    }

    static RecognitionActionCanceledException StaleInput() => new("Recognition was canceled because the audio source changed.");
    public void Ocr(int slot) => Ocr(slot, CancellationToken.None);
    public void Ocr(int slot, CancellationToken cancellationToken)
    {
        object? stream = player.Subtitles.Streams?.FirstOrDefault(candidate => candidate.GetSubEnabled(slot) && candidate.IsBitmap);
        stream ??= player.Playlist?.Selected?.ExternalSubtitlesStreamsAll.FirstOrDefault(candidate => candidate.GetSubEnabled(slot) && candidate.IsBitmap);
        if (stream == null)
            throw new InvalidOperationException("Select a bitmap subtitle track for this slot before starting OCR.");
        object argument = ((object)slot.ToString(System.Globalization.CultureInfo.InvariantCulture), stream, (object)SelectSubMethod.OCR);
        RunSubtitleCommand(player, stream, () => player.Commands.OpenSubtitlesAction(argument), cancellationToken);
    }
    public static void RunSubtitleCommand(Player player, object stream, Action command, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<string?> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Embedded(object? sender, FlyleafLib.MediaFramework.MediaContext.DecoderContext.OpenSubtitlesStreamCompletedArgs e)
        { if (ReferenceEquals(e.Stream, stream)) { completed.TrySetResult(e.Error); } }
        void External(object? sender, FlyleafLib.MediaFramework.MediaContext.DecoderContext.OpenExternalSubtitlesStreamCompletedArgs e)
        { if (ReferenceEquals(e.ExtStream, stream)) { completed.TrySetResult(e.Error); } }
        void Configuration(object? sender, KnownErrorOccurredEventArgs e)
        { if (e.SettingsTab == KnownErrorSettingsTab.SubtitlesOCR) { completed.TrySetResult(e.Message); } }
        void Opening(object? sender, OpeningArgs e)
        { if (!e.IsSubtitles) { Superseded(); } }
        void Opened(object? sender, OpenCompletedArgs e)
        { if (!e.IsSubtitles) { Superseded(); } }
        void Superseded() => completed.TrySetException(new RecognitionActionCanceledException("Subtitle opening was canceled because media changed."));
        player.OpenSubtitlesStreamCompleted += Embedded;
        player.OpenExternalSubtitlesStreamCompleted += External;
        player.KnownErrorOccurred += Configuration;
        player.Opening += Opening;
        player.OpenCompleted += Opened;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            command();
            // Keep ownership through real completion, unless media replacement or shutdown invalidates it.
            string? failure = completed.Task.WaitAsync(cancellationToken).GetAwaiter().GetResult();
            if (failure != null) throw new InvalidOperationException(failure);
        }
        finally
        {
            player.OpenSubtitlesStreamCompleted -= Embedded;
            player.OpenExternalSubtitlesStreamCompleted -= External;
            player.KnownErrorOccurred -= Configuration;
            player.Opening -= Opening;
            player.OpenCompleted -= Opened;
        }
    }

    public void Apply(RecognitionSettings settings) => settings.Apply(player.Config.Subtitles);
}

/// <summary>One gate for recognition and ordinary media/track mutations; queued work belongs to its media generation.</summary>
public sealed class RecognitionActions(IRecognitionBackend backend, Func<IDisposable>? acquireResourceUse,
    GpuWorkCoordinator? gpuCoordinator) : IDisposable, IAsyncDisposable
{
    public RecognitionActions(IRecognitionBackend backend, Func<IDisposable>? acquireResourceUse = null)
        : this(backend, acquireResourceUse, null) { }

    readonly object admission = new();
    Task tail = Task.CompletedTask;
    int generation;
    volatile bool disposed;
    int pending;
    ActiveCommand? active;
    bool shuttingDown;
    readonly SemaphoreSlim starts = new(1, 1);
    readonly StartRequest?[] startRequests = new StartRequest?[2];
    readonly HashSet<Task> startTasks = [];
    Task asrRun = Task.CompletedTask;
    int waitingForGpu;

    public bool IsDispatching => Volatile.Read(ref pending) != 0;
    public bool IsBusy => backend.IsBusy || IsDispatching;
    public bool IsWaitingForGpu => Volatile.Read(ref waitingForGpu) != 0;
    public event Action? GpuWaitChanged;
    public void Invalidate()
    {
        lock (admission)
        {
            generation++;
            active?.Cancel();
            foreach (StartRequest? request in startRequests) request?.Command.Cancel();
        }
    }

    public void CancelStaleStarts()
    {
        if (gpuCoordinator == null || backend is not ICoordinatedRecognitionBackend gpuBackend) return;
        lock (admission)
        {
            foreach (StartRequest? request in startRequests)
                if (request != null && !gpuBackend.IsCurrentInput(request.Input)) request.Command.Cancel();
        }
    }

    public void CancelPendingStarts()
    {
        lock (admission)
            foreach (StartRequest? request in startRequests) request?.Command.Cancel();
    }

    public Task StartAsync(int slot, bool detectAsrOmissions = true)
    {
        if (gpuCoordinator == null || backend is not ICoordinatedRecognitionBackend gpuBackend)
            return RunAsync(() =>
            {
                ValidateSlot(slot);
                ValidateStart();
                using IDisposable? resourceUse = acquireResourceUse?.Invoke();
                backend.Start(slot, detectAsrOmissions);
            });

        ValidateSlot(slot);
        lock (admission)
        {
            if (disposed || shuttingDown)
                return Task.FromException(new RecognitionActionCanceledException("Recognition was canceled because the player is closing."));
            var request = new StartRequest(gpuBackend.CaptureInput(), new ActiveCommand(), detectAsrOmissions);
            startRequests[slot]?.Command.Cancel();
            startRequests[slot] = request;
            Task task = Task.Run(() => StartCoordinatedAsync(slot, gpuBackend, request, gpuCoordinator));
            startTasks.Add(task);
            _ = task.ContinueWith(_ => { lock (admission) startTasks.Remove(task); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    async Task StartCoordinatedAsync(int slot, ICoordinatedRecognitionBackend gpuBackend, StartRequest request,
        GpuWorkCoordinator coordinator)
    {
        CancellationToken token = request.Command.Source.Token;
        bool entered = false;
        GpuWorkLease? lease = null;
        try
        {
            await starts.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            bool attachOnly = false, needsGpu = false;
            Task previous = Task.CompletedTask;
            await RunAsync(() =>
            {
                ValidateCurrent(gpuBackend, request.Input, token);
                ValidateStart();
                attachOnly = gpuBackend.CanAttach(slot, request.DetectAsrOmissions);
                if (attachOnly) return;

                lock (admission) previous = asrRun;
                if (!previous.IsCompleted) gpuBackend.SignalStop();
                needsGpu = FasterWhisperDevicePolicy.RequiresGpuAdmission(gpuBackend.CaptureSettings());
            }).ConfigureAwait(false);
            await previous.WaitAsync(token).ConfigureAwait(false);
            if (needsGpu)
            {
                ValueTask<GpuWorkLease> acquisition = coordinator.AcquireAsync("interactive speech recognition", token);
                bool waiting = !acquisition.IsCompleted;
                try
                {
                    if (waiting) ChangeGpuWaiting(1);
                    lease = await acquisition.ConfigureAwait(false);
                }
                finally { if (waiting) ChangeGpuWaiting(-1); }
            }

            await RunAsync(() =>
            {
                ValidateCurrent(gpuBackend, request.Input, token);
                ValidateStart();
                if (attachOnly && !gpuBackend.CanAttach(slot, request.DetectAsrOmissions))
                    throw new RecognitionActionCanceledException("The ASR run for the other subtitle slot is no longer available.");
                Config settings = gpuBackend.CaptureSettings();
                if (!attachOnly && FasterWhisperDevicePolicy.RequiresGpuAdmission(settings) != needsGpu)
                    throw new RecognitionActionCanceledException("Recognition was canceled because the engine device changed.");

                IDisposable? resourceUse = attachOnly ? null : acquireResourceUse?.Invoke();
                try
                {
                    RecognitionAttempt attempt = gpuBackend.Start(slot, request.Input, settings, attachOnly, needsGpu, token,
                        request.DetectAsrOmissions);
                    if (!attachOnly)
                    {
                        Task run = TrackRunAsync(attempt.Completion, lease, resourceUse);
                        lock (admission) asrRun = run;
                        lease = null;
                        resourceUse = null;
                    }
                    attempt.Started.GetAwaiter().GetResult();
                }
                finally { resourceUse?.Dispose(); }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (exception is not RecognitionActionCanceledException)
        {
            throw new RecognitionActionCanceledException("Recognition start was canceled.");
        }
        catch (ObjectDisposedException) when (token.IsCancellationRequested)
        {
            throw new RecognitionActionCanceledException("Recognition start was canceled.");
        }
        finally
        {
            lease?.Complete();
            if (entered) starts.Release();
            Task cancellation;
            lock (admission)
            {
                if (ReferenceEquals(startRequests[slot], request)) startRequests[slot] = null;
                cancellation = request.Command.Cancellation;
            }
            try { await cancellation.ConfigureAwait(false); }
            finally { request.Command.Dispose(); }
        }
    }

    static void ValidateCurrent(ICoordinatedRecognitionBackend backend, object input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!backend.IsCurrentInput(input))
            throw new RecognitionActionCanceledException("Recognition was canceled because the audio source changed.");
    }

    void ChangeGpuWaiting(int delta)
    {
        Interlocked.Add(ref waitingForGpu, delta);
        try { GpuWaitChanged?.Invoke(); }
        catch (Exception) { /* A status subscriber must not abandon an acquired or queued GPU lease. */ }
    }

    static async Task TrackRunAsync(Task completion, GpuWorkLease? lease, IDisposable? resourceUse)
    {
        try { await completion.ConfigureAwait(false); }
        catch (Exception error)
        {
            EngineProcessDrainException? drain = error as EngineProcessDrainException;
            if (error is AggregateException aggregate)
                drain = aggregate.Flatten().InnerExceptions.OfType<EngineProcessDrainException>().FirstOrDefault();
            if (lease != null && drain != null)
            {
                IDisposable? heldResource = resourceUse;
                lease.RequireDrain(async () =>
                {
                    await drain.RetryStop().ConfigureAwait(false);
                    heldResource?.Dispose();
                });
                resourceUse = null;
            }
            // The Flyleaf worker reports engine failures. Here we retain ownership if its process did not drain.
        }
        finally
        {
            try { resourceUse?.Dispose(); }
            finally { lease?.Complete(); }
        }
    }

    void ValidateStart()
    {
        if (!backend.HasAudio) throw new InvalidOperationException("Open media with an audio track before starting recognition.");
        if (backend.IsLive) throw new InvalidOperationException("Recognition is unavailable for live streams.");
    }
    public Task TogglePauseAsync() => RunAsync(() => { if (backend.IsPaused) backend.Resume(); else backend.Pause(); });
    public async Task CancelAsync(int slot)
    {
        ValidateSlot(slot);
        lock (admission) startRequests[slot]?.Command.Cancel();
        await RunAsync(() => backend.Cancel(slot)).ConfigureAwait(false);
        if (gpuCoordinator?.PendingDrain != null) await gpuCoordinator.RetryDrainAsync().ConfigureAwait(false);
    }
    public Task OcrAsync(int slot) => RunAsync(token =>
    {
        ValidateSlot(slot);
        using IDisposable? resourceUse = acquireResourceUse?.Invoke();
        backend.Ocr(slot, token);
    });
    public Task MutateAsync(Action mutation, bool mediaChange = false)
        => MutateAsync(_ => mutation(), mediaChange);
    public Task MutateAsync(Action<CancellationToken> mutation, bool mediaChange = false)
    {
        if (mediaChange) Invalidate();
        return RunAsync(mutation);
    }
    /// <summary>Explicit modal settings actions fail promptly instead of waiting behind media/recognition work.</summary>
    public Task MutateIfIdleAsync(Action<CancellationToken> mutation)
    {
        lock (admission)
        {
            ObjectDisposedException.ThrowIf(disposed || shuttingDown, this);
            if (pending != 0 || backend.IsBusy)
                throw new InvalidOperationException("Wait for media opening, recognition and subtitle loading to finish before changing playback settings.");
            return RunAsync(mutation);
        }
    }
    public Task SaveAndApplyAsync(RecognitionSettings settings, RecognitionSettingsStore store)
    {
        if (IsDispatching || backend.IsBusy) throw new InvalidOperationException("Wait for recognition and subtitle loading to finish before saving settings.");
        RecognitionSettings detached = settings.Clone();
        return RunAsync(() =>
        {
            if (backend.IsBusy) throw new InvalidOperationException("Wait for recognition and subtitle loading to finish before saving settings.");
            store.Save(detached);
            backend.Apply(detached);
            CancelPendingStarts();
        });
    }
    Task RunAsync(Action action) => RunAsync(_ => action());
    Task RunAsync(Action<CancellationToken> action)
    {
        lock (admission)
        {
            int expected = generation;
            Interlocked.Increment(ref pending);
            Task previous = tail;
            tail = Task.Run(async () =>
            {
                using var command = new ActiveCommand();
                try
                {
                    try { await previous.ConfigureAwait(false); } catch { /* A failed predecessor does not poison the queue. */ }
                    lock (admission)
                    {
                        if (disposed || shuttingDown || expected != generation)
                            throw new RecognitionActionCanceledException("Recognition action was canceled because media changed or the player closed.");
                        active = command;
                    }
                    action(command.Source.Token);
                }
                catch (OperationCanceledException) when (command.Source.IsCancellationRequested)
                {
                    throw new RecognitionActionCanceledException("Recognition action was canceled because media changed or the player closed.");
                }
                finally
                {
                    Task cancellation;
                    lock (admission)
                    {
                        if (ReferenceEquals(active, command)) active = null;
                        cancellation = command.Cancellation;
                    }
                    try { await cancellation.ConfigureAwait(false); }
                    finally { Interlocked.Decrement(ref pending); }
                }
            });
            return tail;
        }
    }
    static void ValidateSlot(int slot)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot), "Subtitle slot must be primary or secondary.");
    }
    public void Dispose()
    {
        lock (admission) disposed = true;
        BeginShutdown();
    }
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await DrainAsync().ConfigureAwait(false);
    }

    public void BeginShutdown()
    {
        lock (admission)
        {
            shuttingDown = true;
            Invalidate();
        }
        if (gpuCoordinator != null && backend is ICoordinatedRecognitionBackend gpuBackend) gpuBackend.SignalStop();
    }

    public void ResumeAfterFailedShutdown()
    {
        lock (admission) { if (!disposed) shuttingDown = false; }
    }

    public async Task DrainAsync()
    {
        Task drain;
        Task[] requests;
        lock (admission) { drain = tail; requests = startTasks.ToArray(); }
        try { await drain.ConfigureAwait(false); } catch { /* Rejected queued work has already reported cancellation. */ }
        try { await Task.WhenAll(requests).ConfigureAwait(false); } catch { /* Requests have reported their errors. */ }
        lock (admission) drain = asrRun;
        await drain.ConfigureAwait(false);
    }

    sealed record StartRequest(object Input, ActiveCommand Command, bool DetectAsrOmissions);

    // Admission owns cancellation; the worker disposes the source only after its callbacks settle.
    sealed class ActiveCommand : IDisposable
    {
        public CancellationTokenSource Source { get; } = new();
        public Task Cancellation { get; private set; } = Task.CompletedTask;
        public void Cancel()
        {
            // CancelAsync marks the token now and schedules callbacks outside the admission lock.
            if (!Source.IsCancellationRequested) Cancellation = Source.CancelAsync();
        }
        public void Dispose() => Source.Dispose();
    }
}
