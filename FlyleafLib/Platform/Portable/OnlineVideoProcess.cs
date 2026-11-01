using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FlyleafLib.Plugins;

/// <summary>One extractor and its descendants, including children that survive the root process.</summary>
public sealed partial class OnlineVideoProcess : IDisposable
{
    readonly Process root;
    readonly SafeFileHandle windowsJob;
    bool stopped;
    public Stream Output { get; }
    public Stream Error { get; }
    public int ExitCode => root.ExitCode;
    internal int Id => root.Id;
    public bool WaitForExit(int milliseconds) => root.WaitForExit(milliseconds);

    OnlineVideoProcess(Process root, Stream output, Stream error, SafeFileHandle windowsJob = null)
        => (this.root, Output, Error, this.windowsJob) = (root, output, error, windowsJob);

    public static OnlineVideoProcess Start(ProcessStartInfo info)
    {
        if (!OnlineVideoRequest.IsExecutablePath(info.FileName) || info.Arguments.Length != 0 || info.UseShellExecute)
            throw new ArgumentException("An absolute executable and individual arguments are required.");
        if (OperatingSystem.IsWindows())
            return StartWindows(info);
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException();

        // Without --fork the new session leader execs the configured helper under this PID.
        const string setsid = "/usr/bin/setsid";
        if (!OnlineVideoRequest.IsExecutablePath(setsid))
            throw new FileNotFoundException("Online video requires /usr/bin/setsid (util-linux).");
        info.ArgumentList.Insert(0, info.FileName);
        info.ArgumentList.Insert(0, "--");
        info.FileName = setsid;
        var process = Process.Start(info);
        return new(process, process.StandardOutput.BaseStream, process.StandardError.BaseStream);
    }

    // Close the containment boundary even when the root exited before its descendants.
    public void Stop()
    {
        if (stopped) return;
        if (windowsJob != null)
            windowsJob.Dispose();
        else
        {
            // On very early cancellation setsid might not have established the group yet.
            // Kill the root first and then its group; no descendants can branch after this.
            if (!root.HasExited)
            {
                try { root.Kill(); }
                catch (InvalidOperationException) when (root.HasExited) { }
            }
            if (Kill(-root.Id, 9) < 0 && Marshal.GetLastPInvokeError() != 3) // ESRCH: group already gone.
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        if (!root.WaitForExit(1500))
            throw new IOException("The extractor did not terminate.");
        stopped = true;
    }

    public void Dispose()
    {
        try { Stop(); }
        finally
        {
            Output.Dispose();
            Error.Dispose();
            root.Dispose();
        }
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int signal);
}
