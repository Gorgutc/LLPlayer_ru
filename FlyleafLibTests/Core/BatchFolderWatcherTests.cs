using FlyleafLib.MediaPlayer.Batch;
using LLPlayer.Core.Batch;

namespace FlyleafLibTests.Core;

public sealed class BatchFolderWatcherTests
{
    [Fact]
    public async Task Stop_does_not_wait_for_filesystem_probe_and_retired_probe_cannot_publish()
    {
        var source = new Events(); var fileSystem = new FileSystem();
        await using var watcher = new BatchFolderWatcher(fileSystem, (_, _) => source, Timeout.InfiniteTimeSpan);
        var ready = new List<BatchWatchReady>(); watcher.FileReady += ready.Add;
        watcher.Start("root", false); source.Add("lesson.mkv");
        Task poll = watcher.PollNowAsync();
        try
        {
            await fileSystem.ProbeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Task.Run(watcher.Stop, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(source.Disposed);
        }
        finally { fileSystem.ReleaseProbe.TrySetResult(); }
        await poll; await watcher.DrainAsync(); Assert.Empty(ready);
    }

    [Fact]
    public async Task Overflow_rescan_does_not_block_stop_and_async_quit_drains_retired_io()
    {
        var source = new Events(); var fileSystem = new FileSystem();
        var watcher = new BatchFolderWatcher(fileSystem, (_, _) => source, Timeout.InfiniteTimeSpan);
        var errors = new List<BatchWatchError>(); watcher.Error += errors.Add;
        watcher.Start("root", true); source.Fail(new InternalBufferOverflowException());
        try
        {
            await fileSystem.ScanEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Task.Run(watcher.Stop, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Task quit = watcher.DisposeAsync().AsTask();
            Assert.False(quit.IsCompleted);
            fileSystem.ReleaseScan.TrySetResult(); await quit;
            Assert.Empty(errors);
        }
        finally { fileSystem.ReleaseScan.TrySetResult(); await watcher.DisposeAsync(); }
    }

    [Fact]
    public void Scanner_checks_cancellation_before_requesting_the_next_entry_and_disposes_enumerator()
    {
        using var cancellation = new CancellationTokenSource(); bool disposed = false;
        IEnumerable<string> Entries()
        {
            try
            {
                yield return "lesson1.mkv";
                cancellation.Cancel();
                yield return "lesson2.mkv";
                throw new InvalidOperationException("Scanner traversed beyond cancellation.");
            }
            finally { disposed = true; }
        }
        Assert.Throws<OperationCanceledException>(() => BatchVideoScanner.ScanPaths(Entries(), cancellation.Token));
        Assert.True(disposed);
    }

    sealed class Events : IBatchWatchEvents
    {
        public event Action<string>? Changed;
        public event Action<Exception>? Error;
        public bool Disposed { get; private set; }
        public void Start() { }
        public void Add(string path) => Changed?.Invoke(path);
        public void Fail(Exception error) => Error?.Invoke(error);
        public void Dispose() => Disposed = true;
    }
    sealed class FileSystem : IBatchWatchFileSystem
    {
        public TaskCompletionSource ProbeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseProbe { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ScanEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseScan { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void EnsureAccessible(string folder, CancellationToken token) { token.ThrowIfCancellationRequested(); }
        public BatchWatchSnapshot? Probe(string path, CancellationToken token)
        {
            ProbeEntered.TrySetResult(); ReleaseProbe.Task.GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested(); return new(10, DateTime.UnixEpoch, true);
        }
        public IReadOnlyList<string> Scan(string folder, bool recursive, CancellationToken token)
        {
            ScanEntered.TrySetResult(); ReleaseScan.Task.GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested(); return ["lesson.mkv"];
        }
    }
}
