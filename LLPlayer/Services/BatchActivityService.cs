using System.Windows;

namespace LLPlayer.Services;

/// <summary>
/// Process-wide, long-lived snapshot of background batch-subtitle activity, deliberately decoupled from the
/// batch dialog's view-model lifetime (which is created/destroyed each time the dialog opens). The batch VM
/// pushes state here; the tray icon and the app-lifetime logic read it.
///
/// This is the single source of truth that lets the app (and an in-flight batch run) keep living after the
/// main video window is closed: while a batch is running or its window is open, the app stays alive in the
/// tray instead of shutting down.
/// </summary>
public sealed class BatchActivityService
{
    /// <summary>The batch dialog window is currently open.</summary>
    public bool IsDialogOpen { get; private set; }

    /// <summary>A batch run (ASR + translate + save) is currently in progress.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Overall progress of the current run, 0..1.</summary>
    public double Progress { get; private set; }

    /// <summary>Short human-readable status (e.g. "3/7 • 42%").</summary>
    public string ProgressText { get; private set; } = string.Empty;

    /// <summary>True once an explicit app shutdown has begun, so the main-window close handler stops keeping
    /// the app alive in the tray and the batch dialog stops prompting before closing.</summary>
    public bool IsShuttingDown { get; private set; }

    /// <summary>
    /// True while batch work or its window should keep the process alive after the main window is closed.
    /// </summary>
    public bool KeepAlive => !IsShuttingDown && (IsRunning || IsDialogOpen);

    /// <summary>Raised (on the UI thread) whenever any of the above state changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a batch run transitions from running to finished (for a tray balloon). The bool
    /// argument is <c>true</c> when the run was cancelled rather than completing normally.</summary>
    public event EventHandler<bool>? RunCompleted;

    /// <summary>The batch VM subscribes to this to cancel its in-flight run when the app is quitting.</summary>
    public event EventHandler? CancelRequested;

    public void SetDialogOpen(bool open)
    {
        if (IsDialogOpen == open)
            return;

        IsDialogOpen = open;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetRunning(bool running, bool canceled = false)
    {
        if (IsRunning == running)
            return;

        bool finished = IsRunning && !running;
        IsRunning = running;
        if (!running)
        {
            Progress = 0;
            ProgressText = string.Empty;
        }

        Changed?.Invoke(this, EventArgs.Empty);

        if (finished)
            RunCompleted?.Invoke(this, canceled);
    }

    public void ReportProgress(double progress, string text)
    {
        Progress = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        ProgressText = text ?? string.Empty;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Begin an explicit application shutdown: flag it (so the main-window close handler does not divert to
    /// the tray and the batch dialog does not prompt), ask the batch VM to cancel any in-flight run, then
    /// shut WPF down. Safe to call more than once. Note: on quit, batch cancellation/cleanup is best-effort —
    /// `Shutdown()` is called synchronously, so the run's cancellation continuations may not all complete.
    /// </summary>
    public void RequestQuit()
    {
        if (IsShuttingDown)
        {
            Application.Current.Shutdown();
            return;
        }

        IsShuttingDown = true;
        CancelRequested?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
        Application.Current.Shutdown();
    }
}
