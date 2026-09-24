namespace FlyleafLib;

/// <summary>
/// UI-thread dispatcher contract used by the portable (non-Windows) build of FlyleafLib.
/// <para>
/// On Windows the engine marshals to the WPF <c>Application.Current.Dispatcher</c>. On the portable TFM there is no
/// WPF, so the host application (e.g. the Avalonia app, via <c>Dispatcher.UIThread</c>) assigns an implementation to
/// <see cref="Utils.UIDispatcher"/> before calling <see cref="Engine.Start"/>. When no dispatcher is assigned
/// (headless runs, unit tests) every UI action runs inline on the calling thread.
/// </para>
/// </summary>
public interface IUIDispatcher
{
    /// <summary>True when the calling thread is the UI thread (no marshalling required).</summary>
    bool CheckAccess();

    /// <summary>Queues <paramref name="action"/> to run asynchronously on the UI thread (fire and forget).</summary>
    void Post(Action action);

    /// <summary>Runs <paramref name="action"/> synchronously on the UI thread and blocks until it completes.</summary>
    void Invoke(Action action);
}
