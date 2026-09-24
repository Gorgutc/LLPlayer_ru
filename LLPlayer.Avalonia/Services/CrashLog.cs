using System.Text;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Appends unhandled exceptions to <see cref="AppPaths.CrashLogFile"/> (XDG state dir). Never throws: a failing crash
/// log must not hide the original error.
/// </summary>
public static class CrashLog
{
    static readonly Lock writeLock = new();
    static string? path;

    public static string? CurrentPath => path;

    /// <summary>Hooks AppDomain / TaskScheduler unhandled-exception events. Call once, first thing in Main.</summary>
    public static void Install(AppPaths paths)
    {
        path = paths.CrashLogFile;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException" + (e.IsTerminating ? " (terminating)" : ""), e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string source, Exception? exception)
    {
        string? target = path;
        if (target == null)
            return;

        try
        {
            StringBuilder sb = new();
            sb.Append('[').Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")).Append("] ")
              .Append(source).AppendLine();
            sb.AppendLine(exception?.ToString() ?? "(no exception object)");
            sb.AppendLine();

            lock (writeLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.AppendAllText(target, sb.ToString());
            }
        }
        catch
        {
            // ignored: never mask the original failure
        }
    }
}
