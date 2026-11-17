using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using LLPlayer.ControlApi;

namespace LLPlayer.ControlHost;

internal sealed class ControlHostDiscovery : IDisposable
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly FileStream ownership;
    private readonly string discoveryPath;
    private bool published;

    private ControlHostDiscovery(string directory, FileStream ownership)
    {
        this.ownership = ownership;
        discoveryPath = Path.Combine(directory, "host.json");
    }

    public static ControlHostDiscovery? TryAcquire()
    {
        var directory = ControlPaths.UserConnectionDirectory;
        EnsurePrivateDirectory(directory);
        var lockPath = Path.Combine(directory, "control.lock");
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = PrivateFile;
        try
        {
            return new(directory, new FileStream(lockPath, options));
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static void EnsurePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            EnsureWindowsPrivateDirectory(directory);
            return;
        }
        var info = Directory.CreateDirectory(directory, PrivateDirectory);
        if (info.LinkTarget is not null || (File.GetUnixFileMode(directory) & ~PrivateDirectory) != 0)
            throw new UnauthorizedAccessException("The control state requires a private directory.");
    }

    public void BindState(string stateDirectory)
    {
        // The discovery lock is user-wide; engine recovery evidence is stored under the
        // bound state directory. A crash must not let a new profile bypass that evidence.
        if (ownership.Length != 0)
        {
            if (ownership.Length > 32_768)
                throw new InvalidDataException("The retained control state binding is invalid.");
            ownership.Position = 0;
            var retained = JsonSerializer.Deserialize<string>(ownership, ControlProtocol.Json);
            if (string.IsNullOrWhiteSpace(retained) || !ControlPaths.SameDirectory(retained, stateDirectory))
                throw new ControlException(new("cleanup_pending", "Recover the previous control state before switching profiles."));
            return;
        }
        JsonSerializer.Serialize(ownership, stateDirectory, ControlProtocol.Json);
        ownership.Flush(flushToDisk: true);
    }

    public void ReleaseState()
    {
        ownership.SetLength(0);
        ownership.Flush(flushToDisk: true);
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureWindowsPrivateDirectory(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new UnauthorizedAccessException("Current Windows user SID is unavailable.");
        var info = new DirectoryInfo(path);
        if (info.Exists && info.LinkTarget is not null)
            throw new UnauthorizedAccessException("The control state requires a private physical directory.");

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        if (info.Exists)
            info.SetAccessControl(security);
        else
            info.Create(security);
    }

    public async Task PublishAsync(ControlDiscovery discovery, CancellationToken cancellationToken)
    {
        var staging = discoveryPath + "." + Guid.NewGuid().ToString("N");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = PrivateFile;
        try
        {
            await using (var stream = new FileStream(staging, options))
                await JsonSerializer.SerializeAsync(stream, discovery, ControlProtocol.Json, cancellationToken);
            File.Move(staging, discoveryPath, overwrite: true);
            published = true;
        }
        finally
        {
            File.Delete(staging);
        }
    }

    public void Dispose()
    {
        try
        {
            if (published)
                File.Delete(discoveryPath);
        }
        finally
        {
            ownership.Dispose();
        }
    }

    public static string CreateCredential() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static bool MatchesCredential(string expected, string supplied)
        => supplied.Length == expected.Length
           && CryptographicOperations.FixedTimeEquals(
               System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(supplied));
}
