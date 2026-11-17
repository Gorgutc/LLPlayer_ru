#nullable enable

using System.ComponentModel;
using System.Diagnostics;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib.MediaPlayer;

/// <summary>Identity of one independently contained engine tree, durable across a host interruption.</summary>
public sealed record EngineProcessIdentity(Guid Id, string Platform, string PlatformToken, int? ProcessId = null,
    string? ProcessStartToken = null);

public interface IEngineProcessOwner
{
    void Record(EngineProcessIdentity process);
    void Drained(Guid id);
}

public static class EngineProcessOwnership
{
    private static readonly AsyncLocal<IEngineProcessOwner?> Owner = new();

    internal static IEngineProcessOwner? Current => Owner.Value;

    /// <summary>Flows the active durable job through the existing asynchronous engine owners.</summary>
    public static IDisposable Enter(IEngineProcessOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var scope = new RestoreOwner(Owner.Value);
        Owner.Value = owner;
        return scope;
    }

    /// <summary>Returns false if the persisted boundary is occupied or cannot be revalidated.</summary>
    public static bool IsDrained(EngineProcessIdentity identity)
    {
        try
        {
            if (OperatingSystem.IsWindows() && identity.Platform == "windows-job")
                return identity.PlatformToken == "global" &&
                    JobObjectGuard.IsNamedJobDrained(WindowsJobName(identity.Id));
            if (OperatingSystem.IsLinux() && identity.Platform == "linux-session" &&
                identity.ProcessId is int pid && pid > 1 && !string.IsNullOrWhiteSpace(identity.ProcessStartToken))
            {
                if (LinuxBootToken() != identity.PlatformToken)
                    return true;

                // The original session leader may already be gone. Its surviving members still
                // retain the session ID. If a new leader owns the numeric ID, the old session ended.
                var root = ReadLinuxProcess($"/proc/{pid}/stat");
                if (root is { } current && current.Session == pid && current.Start != identity.ProcessStartToken)
                    return true;
                return !LinuxSessionHasLiveMembers(pid);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
        return false;
    }

    // Keep recovery possible after a new logon session. The creating token supplies the job ACL.
    internal static string WindowsJobName(Guid id) => $@"Global\LLPlayer.Engine.{id:N}";

    internal static EngineProcessIdentity IdentifyLinuxProcess(int processId)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            var process = ReadLinuxProcess($"/proc/{processId}/stat")
                ?? throw new IOException("Cannot read the contained engine process identity.");
            if (process.Session == processId)
                return new(Guid.NewGuid(), "linux-session", LinuxBootToken(), processId, process.Start);
            Thread.Sleep(5);
        }
        while (watch.Elapsed < TimeSpan.FromSeconds(2));
        throw new IOException("The engine supervisor did not establish its process session.");
    }

    internal static bool LinuxSessionHasLiveMembers(int sessionId)
    {
        foreach (string path in Directory.EnumerateDirectories("/proc"))
        {
            if (int.TryParse(Path.GetFileName(path), out _) &&
                ReadLinuxProcess(Path.Combine(path, "stat")) is { } member &&
                member.Session == sessionId && member.State is not ("Z" or "X"))
                return true;
        }
        return false;
    }

    private static string LinuxBootToken()
    {
        string token = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
        return Guid.TryParse(token, out _) ? token : throw new IOException("Cannot read Linux boot identity.");
    }

    private static LinuxProcess? ReadLinuxProcess(string path)
    {
        string stat;
        try { stat = File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        int nameEnd = stat.LastIndexOf(')');
        if (nameEnd < 0)
            throw new IOException("Cannot verify the engine process identity.");
        string[] fields = stat[(nameEnd + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length <= 19 || !int.TryParse(fields[3], out int session))
            throw new IOException("Cannot verify the engine process identity.");
        return new(session, fields[0], fields[19]);
    }

    private sealed record LinuxProcess(int Session, string State, string Start);

    private sealed class RestoreOwner(IEngineProcessOwner? previous) : IDisposable
    {
        public void Dispose() => Owner.Value = previous;
    }
}
