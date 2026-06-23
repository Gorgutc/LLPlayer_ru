using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;

namespace LLPlayer.Services;

/// <summary>
/// Keeps a long batch run friendly to interactive use. While the user is actively at the keyboard/mouse it
/// (a) suspends a running faster-whisper child process so the GPU is freed almost immediately — even mid-file,
/// and (b) holds the queue so the next ASR/translation unit does not start. It resumes once input has been
/// idle for the configured threshold.
///
/// Implementation stays in the app layer (Win32 <c>GetLastInputInfo</c> for idle + <c>NtSuspendProcess</c>/
/// <c>NtResumeProcess</c> on the engine's own child process, matched by exe name), so the shared engine code
/// is untouched. whisper.cpp runs in-process and cannot be suspended this way — for it only the queue hold
/// applies (boundary pause), which is still useful.
/// </summary>
public sealed class BatchThrottle : IBatchThrottle
{
    private readonly Func<bool> _enabled;
    private readonly Func<int> _idleThresholdSeconds;
    private readonly Func<string?> _enginePath;
    private readonly Action<bool>? _onPausedChanged;
    private readonly LogHandler Log = new("[App] [BatchThrottle ] ");

    private readonly object _sync = new();
    private readonly HashSet<int> _suspended = [];
    private CancellationTokenSource? _watchCts;
    private Task? _watchTask;
    private bool _paused;

    /// <param name="enabled">Whether the "pause while active" feature is on (read live from config).</param>
    /// <param name="idleThresholdSeconds">Required idle time before resuming (read live from config).</param>
    /// <param name="enginePath">Full path of the faster-whisper engine exe, or null when the engine is
    /// whisper.cpp / not external (then only the queue hold applies, no process suspend).</param>
    /// <param name="onPausedChanged">Optional callback when the paused-by-user state flips. The callee is
    /// responsible for marshalling onto the UI thread.</param>
    public BatchThrottle(
        Func<bool> enabled,
        Func<int> idleThresholdSeconds,
        Func<string?> enginePath,
        Action<bool>? onPausedChanged = null)
    {
        _enabled = enabled;
        _idleThresholdSeconds = idleThresholdSeconds;
        _enginePath = enginePath;
        _onPausedChanged = onPausedChanged;
    }

    public void Start()
    {
        if (_watchTask != null || !_enabled())
            return;

        // Safety net: if the app exits (e.g. tray Quit / Exit App) while we hold a process suspended, the
        // per-run Stop() in the processor's finally never runs — so a suspended faster-whisper child would be
        // orphaned WHILE FROZEN (it can never finish and pins GPU memory). Kill those on process exit.
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        _watchCts = new CancellationTokenSource();
        _watchTask = Task.Run(() => WatchLoopAsync(_watchCts.Token));
    }

    public void Stop()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        Task? watch = _watchTask;
        try { _watchCts?.Cancel(); }
        catch { /* ignore */ }

        // Join the watch loop BEFORE the final resume so it cannot re-suspend a process (or fire a late
        // paused callback) after we have resumed/reset. The loop's paused callback marshals via
        // Dispatcher.BeginInvoke (non-blocking), so waiting here cannot deadlock even on the UI thread.
        try { watch?.Wait(TimeSpan.FromSeconds(2)); }
        catch { /* ignore */ }

        ResumeAll();
        SetPaused(false);

        _watchTask = null;
        _watchCts?.Dispose();
        _watchCts = null;
    }

    public async Task WaitWhileBlockedAsync(CancellationToken token)
    {
        if (!_enabled())
            return;

        // Hold the queue while the user is active. The watcher loop handles suspending an already-running
        // process; this prevents the NEXT unit (next file's ASR, or the LLM translation step) from starting.
        while (!token.IsCancellationRequested && IsUserActive())
        {
            await Task.Delay(1000, token);
        }
    }

    private async Task WatchLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (IsUserActive())
                {
                    SuspendEngineProcesses();
                    SetPaused(true);
                }
                else
                {
                    ResumeAll();
                    SetPaused(false);
                }

                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on Stop()
        }
        catch (Exception ex)
        {
            Log.Error($"watch loop failed: {ex.Message}");
        }
        finally
        {
            ResumeAll();
        }
    }

    private bool IsUserActive()
    {
        if (!_enabled())
            return false;

        uint idleMs = GetIdleMilliseconds();
        long thresholdMs = Math.Max(1, _idleThresholdSeconds()) * 1000L;
        return idleMs < thresholdMs;
    }

    private void SetPaused(bool paused)
    {
        lock (_sync)
        {
            if (_paused == paused)
                return;
            _paused = paused;
        }

        // Invoke outside the lock — the callback marshals onto the UI thread.
        try { _onPausedChanged?.Invoke(paused); }
        catch (Exception ex) { Log.Error($"paused callback failed: {ex.Message}"); }
    }

    private void SuspendEngineProcesses()
    {
        lock (_sync)
        {
            foreach (int pid in FindEnginePids())
            {
                if (_suspended.Contains(pid))
                    continue;

                // Record only after a confirmed suspend, so _suspended stays an accurate record of state.
                if (SetSuspended(pid, suspend: true))
                    _suspended.Add(pid);
            }
        }
    }

    private void ResumeAll()
    {
        lock (_sync)
        {
            foreach (int pid in _suspended)
            {
                SetSuspended(pid, suspend: false);
            }

            _suspended.Clear();
        }
    }

    // Fires when the process is exiting (e.g. tray Quit / Exit App) and the per-run Stop() did not get to run.
    // A process we suspended can never finish on its own, so terminate it rather than leak a frozen child.
    private void OnProcessExit(object? sender, EventArgs e) => KillSuspended();

    private void KillSuspended()
    {
        int[] pids;
        lock (_sync)
        {
            pids = [.. _suspended];
            _suspended.Clear();
        }

        foreach (int pid in pids)
        {
            try
            {
                // Process.Kill -> TerminateProcess works on a suspended process, so no resume is needed first.
                using Process p = Process.GetProcessById(pid);
                p.Kill();
            }
            catch
            {
                // already gone / access denied — best-effort cleanup on exit
            }
        }
    }

    private IEnumerable<int> FindEnginePids()
    {
        string? engine = _enginePath();
        if (string.IsNullOrWhiteSpace(engine))
            yield break;

        string name;
        try { name = Path.GetFileNameWithoutExtension(engine); }
        catch { yield break; }

        if (string.IsNullOrEmpty(name))
            yield break;

        Process[] procs;
        try { procs = Process.GetProcessesByName(name); }
        catch { yield break; }

        foreach (Process p in procs)
        {
            int pid;
            try { pid = p.Id; }
            catch { p.Dispose(); continue; }

            p.Dispose();
            yield return pid;
        }
    }

    private bool SetSuspended(int pid, bool suspend)
    {
        IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            // NtSuspendProcess/NtResumeProcess return an NTSTATUS (negative as signed int on failure).
            int status = suspend ? NtSuspendProcess(handle) : NtResumeProcess(handle);
            if (status < 0)
            {
                Log.Error($"{(suspend ? "suspend" : "resume")} pid {pid} failed: NTSTATUS 0x{status:X8}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"{(suspend ? "suspend" : "resume")} pid {pid} failed: {ex.Message}");
            return false;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    #region Win32
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private static uint GetIdleMilliseconds()
    {
        LASTINPUTINFO info = new() { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return 0;

        // Unsigned subtraction is correct across the ~49-day GetTickCount wrap.
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }

    private const int PROCESS_SUSPEND_RESUME = 0x0800;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);
    #endregion
}
