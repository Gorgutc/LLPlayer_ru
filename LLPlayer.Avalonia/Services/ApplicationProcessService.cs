using System.Diagnostics;

namespace LLPlayer.Avalonia.Services;

public enum ApplicationLaunchKind { New, Clone, Restart }

/// <summary>Resolved apphost or dotnet host plus its entry assembly; no command-line reparsing.</summary>
public sealed record ApplicationLaunchContext(string ExecutablePath, string WorkingDirectory,
    string? EntryAssemblyPath = null, IReadOnlyList<string>? RetainedOptions = null);

public static class ApplicationProcessPlanner
{
    public static ProcessStartInfo Create(ApplicationLaunchKind kind, ApplicationLaunchContext context,
        string? mediaUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ExecutablePath);
        ProcessStartInfo plan = new()
        {
            FileName = context.ExecutablePath,
            WorkingDirectory = context.WorkingDirectory,
            UseShellExecute = false,
        };
        if (!string.IsNullOrWhiteSpace(context.EntryAssemblyPath)) plan.ArgumentList.Add(context.EntryAssemblyPath);
        foreach (string option in context.RetainedOptions ?? []) plan.ArgumentList.Add(option);
        if (kind != ApplicationLaunchKind.New && !string.IsNullOrWhiteSpace(mediaUrl))
        {
            plan.ArgumentList.Add("--");
            plan.ArgumentList.Add(mediaUrl);
        }
        return plan;
    }
}

/// <summary>Spawn must succeed before an orderly restart drain is requested.</summary>
public sealed class ApplicationProcessService(Func<ProcessStartInfo, bool> spawn, Func<Task> requestExit)
{
    int restartStarted;
    public string? Error { get; private set; }

    public async Task<bool> LaunchAsync(ApplicationLaunchKind kind, ApplicationLaunchContext context, string? mediaUrl)
    {
        if (Volatile.Read(ref restartStarted) != 0) return false;
        bool restart = kind == ApplicationLaunchKind.Restart;
        if (restart && Interlocked.CompareExchange(ref restartStarted, 1, 0) != 0) return false;
        Error = null;
        bool started;
        try
        {
            started = spawn(ApplicationProcessPlanner.Create(kind, context, mediaUrl));
            if (!started) Error = "The new application instance could not be started.";
        }
        catch (Exception error)
        {
            Error = "The new application instance could not be started: " + error.Message;
            started = false;
        }
        if (!started)
        {
            if (restart) Volatile.Write(ref restartStarted, 0);
            return false;
        }
        if (restart) await requestExit();
        return true;
    }
}
