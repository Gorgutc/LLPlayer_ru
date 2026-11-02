using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;

namespace LLPlayer.Core.Batch;

/// <summary>Session-tagged native events. Filesystem waits never hold the lifecycle gate or run on the UI thread.</summary>
public sealed class BatchFolderWatcher : IBatchFolderWatcher, IAsyncDisposable
{
    readonly object gate = new();
    readonly HashSet<Task> workers = [];
    readonly IBatchWatchFileSystem fileSystem;
    readonly Func<string, bool, IBatchWatchEvents> createEvents;
    readonly TimeSpan interval;
    Session? current;
    long nextSession;
    bool disposed;
    public event Action<BatchWatchReady>? FileReady;
    public event Action<BatchWatchError>? Error;

    public BatchFolderWatcher() : this(new NativeFileSystem(), (folder, recursive) => new NativeEvents(folder, recursive), TimeSpan.FromSeconds(1)) { }
    internal BatchFolderWatcher(IBatchWatchFileSystem fileSystem, Func<string, bool, IBatchWatchEvents> createEvents, TimeSpan interval)
    { this.fileSystem = fileSystem; this.createEvents = createEvents; this.interval = interval; }

    public long Start(string path, bool recursive)
    {
        Session? retired;
        long sessionId;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            retired = current; current = null; sessionId = ++nextSession;
        }
        retired?.Dispose();
        // Native watcher construction and subscription happen outside the lifecycle lock.
        var events = createEvents(path, recursive);
        Session? session = null;
        lock (gate)
        {
            if (!disposed && nextSession == sessionId) { session = new Session(sessionId, path, recursive, events); current = session; }
        }
        if (session == null) { events.Dispose(); return sessionId; }
        events.Changed += changed => Track(session, changed);
        events.Error += error => OnSourceError(session, error);
        var timer = new Timer(_ => _ = PollAsync(session), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        session.AttachTimer(timer);
        try
        {
            events.Start();
            lock (gate) { if (!ReferenceEquals(current, session)) { timer.Dispose(); return session.Id; } }
            timer.Change(interval, interval);
            return session.Id;
        }
        catch
        {
            Retire(session);
            throw;
        }
    }

    public void Stop()
    {
        Session? retired;
        lock (gate) { nextSession++; retired = current; current = null; }
        retired?.Dispose();
    }
    void Retire(Session session)
    {
        lock (gate) { if (ReferenceEquals(current, session)) current = null; }
        session.Dispose();
    }
    void Track(Session session, string path)
    {
        if (!Utils.IsVideoExtension(path)) return;
        lock (gate)
        {
            if (disposed || !ReferenceEquals(current, session)) return;
            session.Pending.TryAdd(path, FileStabilityState.Initial);
        }
    }

    internal Task PollNowAsync()
    {
        Session? session; lock (gate) session = current;
        return session == null ? Task.CompletedTask : PollAsync(session);
    }
    Task PollAsync(Session session)
    {
        KeyValuePair<string, FileStabilityState>[] pending;
        lock (gate)
        {
            if (disposed || !ReferenceEquals(current, session) || session.PollRunning) return Task.CompletedTask;
            session.PollRunning = true;
            pending = session.Pending.ToArray();
            return StartWorkerLocked(() => PollFiles(session, pending));
        }
    }
    void PollFiles(Session session, KeyValuePair<string, FileStabilityState>[] pending)
    {
        try
        {
            var token = session.Token;
            token.ThrowIfCancellationRequested();
            fileSystem.EnsureAccessible(session.Folder, token);
            List<(string Path, FileStabilityState? State)> observed = [];
            foreach (var pair in pending)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var snapshot = fileSystem.Probe(pair.Key, token);
                    token.ThrowIfCancellationRequested();
                    observed.Add((pair.Key, snapshot == null ? null : FileReadiness.Step(pair.Value, snapshot.Value.Size, snapshot.Value.WriteUtc, snapshot.Value.CanOpen)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { /* A transient per-file error retries next tick. Root access is checked separately. */ }
            }
            List<string> ready = [];
            lock (gate)
            {
                if (disposed || !ReferenceEquals(current, session) || token.IsCancellationRequested) return;
                foreach (var item in observed)
                {
                    if (!session.Pending.ContainsKey(item.Path)) continue;
                    if (item.State == null) session.Pending.Remove(item.Path);
                    else if (item.State.Value.IsReady) { session.Pending.Remove(item.Path); ready.Add(item.Path); }
                    else session.Pending[item.Path] = item.State.Value;
                }
            }
            // The session travels with the callback through any subsequent UI-dispatch wait.
            if (ready.Count > 0) FileReady?.Invoke(new(session.Id, Utils.GetMoviesSorted(ready)));
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Fail(session, ex.Message); }
        finally { lock (gate) session.PollRunning = false; }
    }
    void OnSourceError(Session session, Exception error)
    {
        if (error is not InternalBufferOverflowException) { Fail(session, error.Message); return; }
        lock (gate)
        {
            if (disposed || !ReferenceEquals(current, session) || session.ReseedRunning) return;
            session.ReseedRunning = true;
            _ = StartWorkerLocked(() => Reseed(session));
        }
    }
    void Reseed(Session session)
    {
        try
        {
            var paths = fileSystem.Scan(session.Folder, session.Recursive, session.Token);
            session.Token.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (disposed || !ReferenceEquals(current, session)) return;
                foreach (var path in paths) session.Pending.TryAdd(path, FileStabilityState.Initial);
            }
        }
        catch (OperationCanceledException) when (session.Token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Fail(session, ex.Message); }
        finally { lock (gate) session.ReseedRunning = false; }
    }
    Task StartWorkerLocked(Action action)
    {
        Task worker = Task.Run(action);
        workers.Add(worker);
        _ = worker.ContinueWith(completed =>
        {
            _ = completed.Exception; // Observe unexpected callback failures as well as expected I/O errors.
            lock (gate) workers.Remove(completed);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return worker;
    }
    void Fail(Session session, string message)
    {
        lock (gate)
        {
            if (disposed || !ReferenceEquals(current, session)) return;
            current = null;
        }
        session.Dispose();
        Error?.Invoke(new(session.Id, message));
    }
    public async Task DrainAsync()
    {
        while (true)
        {
            Task[] active;
            lock (gate) active = workers.ToArray();
            if (active.Length == 0) return;
            await Task.WhenAll(active).ConfigureAwait(false);
            lock (gate) workers.RemoveWhere(task => task.IsCompleted);
        }
    }
    public void Dispose()
    {
        Session? retired;
        lock (gate) { disposed = true; nextSession++; retired = current; current = null; }
        retired?.Dispose();
    }
    public async ValueTask DisposeAsync() { Dispose(); await DrainAsync().ConfigureAwait(false); }

    sealed class Session : IDisposable
    {
        readonly CancellationTokenSource cancellation = new();
        Timer? timer;
        int retired;
        public Session(long id, string folder, bool recursive, IBatchWatchEvents events)
        { Id = id; Folder = folder; Recursive = recursive; Events = events; Token = cancellation.Token; }
        public long Id { get; }
        public string Folder { get; }
        public bool Recursive { get; }
        public IBatchWatchEvents Events { get; }
        public CancellationToken Token { get; }
        public Dictionary<string, FileStabilityState> Pending { get; } = new(BatchWorkflow.PathComparer);
        public void AttachTimer(Timer value)
        {
            Interlocked.Exchange(ref timer, value)?.Dispose();
            if (Volatile.Read(ref retired) != 0) Interlocked.Exchange(ref timer, null)?.Dispose();
        }
        public bool PollRunning, ReseedRunning;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref retired, 1) != 0) return;
            cancellation.Cancel(); Interlocked.Exchange(ref timer, null)?.Dispose(); Events.Dispose(); cancellation.Dispose();
        }
    }
    sealed class NativeEvents : IBatchWatchEvents
    {
        readonly FileSystemWatcher watcher;
        public event Action<string>? Changed;
        public event Action<Exception>? Error;
        public NativeEvents(string folder, bool recursive)
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024
            };
            watcher.Created += (_, e) => Changed?.Invoke(e.FullPath);
            watcher.Changed += (_, e) => Changed?.Invoke(e.FullPath);
            watcher.Renamed += (_, e) => Changed?.Invoke(e.FullPath);
            watcher.Error += (_, e) => Error?.Invoke(e.GetException() ?? new IOException("The watched folder is not accessible."));
        }
        public void Start() => watcher.EnableRaisingEvents = true;
        public void Dispose() => watcher.Dispose();
    }
    sealed class NativeFileSystem : IBatchWatchFileSystem
    {
        public void EnsureAccessible(string folder, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator();
            _ = entries.MoveNext(); token.ThrowIfCancellationRequested();
        }
        public BatchWatchSnapshot? Probe(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            bool open;
            try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); open = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { open = false; }
            return new(info.Length, info.LastWriteTimeUtc, open);
        }
        public IReadOnlyList<string> Scan(string folder, bool recursive, CancellationToken token) => BatchVideoScanner.Scan(folder, recursive, token);
    }
}

internal interface IBatchWatchEvents : IDisposable
{
    event Action<string>? Changed;
    event Action<Exception>? Error;
    void Start();
}
internal readonly record struct BatchWatchSnapshot(long Size, DateTime WriteUtc, bool CanOpen);
internal interface IBatchWatchFileSystem
{
    void EnsureAccessible(string folder, CancellationToken token);
    BatchWatchSnapshot? Probe(string path, CancellationToken token);
    IReadOnlyList<string> Scan(string folder, bool recursive, CancellationToken token);
}
