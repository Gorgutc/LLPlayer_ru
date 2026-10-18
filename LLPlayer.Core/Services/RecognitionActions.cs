using FlyleafLib;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Core.Services;

public sealed class RecognitionActionCanceledException(string message) : OperationCanceledException(message);

public interface IRecognitionBackend
{
    bool HasAudio { get; }
    bool IsLive { get; }
    bool IsBusy { get; }
    bool IsPaused { get; }
    void Start(int slot);
    void Pause();
    void Resume();
    void Cancel(int slot);
    void Ocr(int slot);
    void Ocr(int slot, CancellationToken cancellationToken) => Ocr(slot);
    void Apply(RecognitionSettings settings);
}

/// <summary>Canonical Flyleaf command adapter; native OCR initialization is invoked by the serialized worker.</summary>
public sealed class PlayerRecognitionBackend(Player player) : IRecognitionBackend
{
    public bool HasAudio => player.Audio.IsOpened;
    public bool IsLive => player.IsLive;
    public bool IsBusy => player.IsASRRunning || Enumerable.Range(0, 2).Any(slot => player.Subtitles[slot].EnabledASR) || Enumerable.Range(0, 2).Any(slot => player.SubtitlesManager[slot].IsLoading);
    public bool IsPaused => player.IsASRPaused;
    public void Start(int slot)
    {
        if (!player.SubtitlesASR.CanExecute(out string error, out _)) throw new InvalidOperationException(error);
        player.Commands.OpenSubtitlesASRAction(slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        player.Subtitles[slot].ASRStartup.GetAwaiter().GetResult();
    }
    public void Pause() => player.SubtitlesASR.Pause();
    public void Resume() => player.SubtitlesASR.Resume();
    public void Cancel(int slot) => player.Commands.SubtitlesOffAction(slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
public sealed class RecognitionActions(IRecognitionBackend backend) : IDisposable, IAsyncDisposable
{
    readonly object admission = new();
    Task tail = Task.CompletedTask;
    int generation;
    volatile bool disposed;
    int pending;
    ActiveCommand? active;
    public bool IsDispatching => Volatile.Read(ref pending) != 0;
    public bool IsBusy => backend.IsBusy || IsDispatching;
    public void Invalidate()
    {
        lock (admission)
        {
            generation++;
            active?.Cancel();
        }
    }

    public Task StartAsync(int slot) => RunAsync(() =>
    {
        ValidateSlot(slot);
        if (!backend.HasAudio) throw new InvalidOperationException("Open media with an audio track before starting recognition.");
        if (backend.IsLive) throw new InvalidOperationException("Recognition is unavailable for live streams.");
        backend.Start(slot);
    });
    public Task TogglePauseAsync() => RunAsync(() => { if (backend.IsPaused) backend.Resume(); else backend.Pause(); });
    public Task CancelAsync(int slot) => RunAsync(() => { ValidateSlot(slot); backend.Cancel(slot); });
    public Task OcrAsync(int slot) => RunAsync(token => { ValidateSlot(slot); backend.Ocr(slot, token); });
    public Task MutateAsync(Action mutation, bool mediaChange = false)
        => MutateAsync(_ => mutation(), mediaChange);
    public Task MutateAsync(Action<CancellationToken> mutation, bool mediaChange = false)
    {
        if (mediaChange) Invalidate();
        return RunAsync(mutation);
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
                        if (disposed || expected != generation)
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
        lock (admission) { disposed = true; Invalidate(); }
    }
    public async ValueTask DisposeAsync()
    {
        Task drain;
        lock (admission) { disposed = true; Invalidate(); drain = tail; }
        try { await drain.ConfigureAwait(false); } catch { /* Rejected queued work has already reported cancellation. */ }
    }

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
