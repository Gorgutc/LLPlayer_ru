using System.IO;
using System.Windows;
using System.Windows.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;

namespace LLPlayer.Services;

/// <summary>
/// Watch-folder watcher (F-09): watches a folder for new video files and raises <see cref="FileReady"/> once a
/// file has finished being copied/downloaded (size + last-write stable across polls AND openable for shared read).
/// Opt-in and dialog-scoped — the batch view-model owns one instance and starts/stops it with the "Watch folder"
/// toggle. Every event is raised on the WPF UI thread, and all internal state is touched only on the UI thread
/// (FileSystemWatcher callbacks are marshalled via the dispatcher), so no locking is needed.
///
/// The business rules — which files are eligible and when one is "ready" — live in the pure FlyleafLib helpers
/// <see cref="WatchFolderPolicy"/> and <see cref="FileReadiness"/>; this class is only the FileSystemWatcher +
/// DispatcherTimer plumbing.
/// </summary>
public sealed class BatchFolderWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    // Pending candidates (full path -> last stability snapshot). Only touched on the UI thread.
    private readonly Dictionary<string, FileStabilityState> _pending = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _fsw;
    private string? _folder;
    private bool _recursive;
    private bool _disposed;

    /// <summary>Raised on the UI thread with the full path of a video file that is ready to process.</summary>
    public event Action<string>? FileReady;

    /// <summary>Raised on the UI thread when the watcher fails (e.g. the watched folder was deleted/renamed).</summary>
    public event Action<string>? Error;

    public BatchFolderWatcher()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTick;
    }

    /// <summary>Begin watching the folder (recursively when requested). Replaces any previous watch.</summary>
    public void Start(string folderPath, bool recursive)
    {
        Stop();

        if (_disposed || string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        _folder = folderPath;
        _recursive = recursive;

        _fsw = new FileSystemWatcher(folderPath)
        {
            IncludeSubdirectories = recursive,
            // A fresh copy surfaces as Created and grows (Size/LastWrite) until done; a downloader's final rename
            // (file.part -> file.mkv) surfaces as Renamed. Watch both. The bigger buffer tolerates bursts.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            InternalBufferSize = 64 * 1024
        };
        _fsw.Created += OnFsCreatedOrChanged;
        _fsw.Renamed += OnFsRenamed;
        _fsw.Error += OnFsError;
        _fsw.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (_fsw != null)
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Created -= OnFsCreatedOrChanged;
            _fsw.Renamed -= OnFsRenamed;
            _fsw.Error -= OnFsError;
            _fsw.Dispose();
            _fsw = null;
        }

        _timer.Stop();
        _pending.Clear();
    }

    private void OnFsCreatedOrChanged(object sender, FileSystemEventArgs e) => Track(e.FullPath);
    private void OnFsRenamed(object sender, RenamedEventArgs e) => Track(e.FullPath);

    private void OnFsError(object sender, ErrorEventArgs e)
    {
        Exception? ex = e.GetException();

        // InternalBufferOverflow is RECOVERABLE: the watcher keeps raising events, only the overflowed batch was
        // dropped (common when many files land at once in a recursive watch — the canonical watch-folder case). Do
        // NOT tear the watch down; re-enumerate the folder so the missed files are still picked up (already-done
        // files are filtered out downstream by readiness + WatchFolderPolicy).
        if (ex is InternalBufferOverflowException)
        {
            PostToUi(() =>
            {
                if (_fsw != null)
                    ReseedFromFolder();
            });
            return;
        }

        string message = ex?.Message ?? "The watched folder is no longer accessible.";
        PostToUi(() => Error?.Invoke(message));
    }

    // Marshal every candidate onto the UI thread so _pending / the timer stay single-threaded (no lock). A
    // partial download (.part/.tmp/.crdownload) is filtered out here because its extension isn't a video one.
    private void Track(string path)
    {
        if (!Utils.IsVideoExtension(path))
            return;

        PostToUi(() =>
        {
            if (_fsw == null)
                return;

            // (Re)seed the candidate; a duplicate event just refreshes it. The poll measures stability.
            _pending[path] = FileStabilityState.Initial;
            if (!_timer.IsEnabled)
                _timer.Start();
        });
    }

    // Re-enumerate the watched folder into the pending set (UI thread). Used to recover after a buffer overflow.
    private void ReseedFromFolder()
    {
        if (string.IsNullOrWhiteSpace(_folder) || !Directory.Exists(_folder))
            return;

        try
        {
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = _recursive,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            };

            foreach (string path in Directory.EnumerateFiles(_folder, "*", options))
            {
                if (Utils.IsVideoExtension(path) && !_pending.ContainsKey(path))
                    _pending[path] = FileStabilityState.Initial;
            }

            if (_pending.Count > 0 && !_timer.IsEnabled)
                _timer.Start();
        }
        catch
        {
            // best-effort recovery
        }
    }

    // Post to the UI thread, but no-op if disposed or the dispatcher is shutting down (an FSW callback can race an
    // app quit). The posted action re-checks _disposed before running.
    private void PostToUi(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            return;

        try
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed)
                    return;
                action();
            });
        }
        catch (InvalidOperationException)
        {
            // the dispatcher began shutting down between the check and the post — ignore
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_pending.Count == 0)
        {
            _timer.Stop();
            return;
        }

        List<string> ready = new();
        List<string> gone = new();

        foreach (string path in _pending.Keys.ToList())
        {
            if (!File.Exists(path))
            {
                gone.Add(path); // renamed away (the .part) or deleted mid-copy
                continue;
            }

            long size;
            DateTime writeUtc;
            try
            {
                FileInfo info = new(path);
                size = info.Length;
                writeUtc = info.LastWriteTimeUtc;
            }
            catch
            {
                continue; // transient IO error — re-probe next tick
            }

            FileStabilityState next = FileReadiness.Step(_pending[path], size, writeUtc, CanOpenForRead(path));
            _pending[path] = next;

            if (next.IsReady)
                ready.Add(path);
        }

        foreach (string path in gone)
            _pending.Remove(path);

        foreach (string path in ready)
        {
            _pending.Remove(path);
            FileReady?.Invoke(path); // already on the UI thread
        }

        if (_pending.Count == 0)
            _timer.Stop();
    }

    private static bool CanOpenForRead(string path)
    {
        try
        {
            using FileStream _ = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch
        {
            return false; // a writer (copy/download) still holds the file
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Tick -= OnTick;
        Stop();
    }
}
