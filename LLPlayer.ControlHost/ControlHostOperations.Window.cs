using System.ComponentModel;
using System.Diagnostics;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace LLPlayer.ControlHost;

internal sealed partial class ControlHostOperations
{
    private static readonly TimeSpan WindowObservationTimeout = TimeSpan.FromSeconds(30);

    private void RegisterWindow(string configDirectory, string stateDirectory)
    {
        const string operation = "ui.window.open";
        string executable = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "LLPlayer.Avalonia.exe" : "LLPlayer.Avalonia");
        handlers.Add(operation, (context, ct) => OpenWindowAsync(context, executable, configDirectory, stateDirectory, ct));
        catalog.Add(new(operation, "ui", "Start a packaged Avalonia window and wait for its control registration.",
                true, ControlOperationCompletion.Job),
            typeof(ControlGuiOperations.Empty), typeof(ControlApplicationWindow),
            (request, args, ct) => jobs.EnqueueAsync(Normalize(request, args), ct),
            _ => WindowBlocked(executable, stateDirectory)?.Code, requireAvailabilityAtInvocation: false);
    }

    private async Task<ControlJobOutcome> OpenWindowAsync(ControlJobContext context, string executable,
        string configDirectory, string stateDirectory, CancellationToken token)
    {
        if (WindowBlocked(executable, stateDirectory) is { } blocked)
            return new(ControlJobStatus.Failed, Error: blocked);

        token.ThrowIfCancellationRequested();
        var launchId = Guid.NewGuid();
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["LLPLAYER_CONFIG_DIR"] = configDirectory;
        start.Environment[ControlProtocol.GuiLaunchIdVariable] = launchId.ToString("D");

        context.RequireOriginalHost();
        using var process = StartPackagedWindow(start);
        context.Report("waiting_for_gui_registration");
        try
        {
            if (await ObserveWindowAttachmentAsync(process, launchId, token) is { } window)
                return Done(window);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(ControlJobStatus.Interrupted, Error: new("gui_launch_interrupted",
                $"Avalonia process {process.Id} started. Waiting for its GUI registration was cancelled."));
        }
        return new(ControlJobStatus.Interrupted, Error: new("gui_attach_timeout",
            $"Avalonia process {process.Id} started but did not register within 30 seconds."));
    }

    private async Task<ControlApplicationWindow?> ObserveWindowAttachmentAsync(Process process, Guid launchId,
        CancellationToken token)
    {
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < WindowObservationTimeout)
        {
            token.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new ControlJobException("gui_exited_before_attach", "The Avalonia process exited before registering.");
            if (broker.ClaimLaunchedSession(process.Id, launchId, out bool? sourceOpened) is { } session)
            {
                if (sourceOpened == false)
                    throw new ControlJobException("gui_startup_media_failed", "The new window could not open the launch source.");
                return new(session.SessionId, process.Id, session.LastSeen);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100), token);
        }
        return null;
    }

    private static Process StartPackagedWindow(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new ControlJobException("gui_launch_failed", "The packaged Avalonia app could not start.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            throw new ControlJobException("gui_launch_failed", "The packaged Avalonia app could not start.");
        }
    }

    private static ControlError? WindowBlocked(string executable, string stateDirectory)
    {
        if (OperatingSystem.IsLinux() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            return new("requires_display", "Start the host in a graphical session or launch the player from the desktop.");
        if (!ControlPaths.SameDirectory(stateDirectory, ControlPaths.DefaultAppDirectories().StateDir))
            return new("profile_conflict", "The desktop launcher cannot use this host's custom state directory.");
        if (!File.Exists(executable))
            return new("gui_apphost_missing", "The packaged Avalonia executable was not found alongside the control host.");
        return null;
    }
}
