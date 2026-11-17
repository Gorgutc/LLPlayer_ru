namespace LLPlayer.ControlApi;

public static class ControlPaths
{
    public static string UserConnectionDirectory
    {
        get
        {
            var userRoot = Environment.GetFolderPath(OperatingSystem.IsWindows()
                ? Environment.SpecialFolder.LocalApplicationData
                : Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userRoot))
                throw new InvalidOperationException("Could not locate the user's home directory.");
            return Path.GetFullPath(OperatingSystem.IsWindows()
                ? Path.Combine(userRoot, "LLPlayer", "Control")
                : Path.Combine(userRoot, ".local", "state", "LLPlayer", "Control"));
        }
    }

    public static (string ConfigDir, string StateDir) DefaultAppDirectories()
    {
        var windows = OperatingSystem.IsWindows();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = windows
            ? AbsoluteEnvironment("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : AbsoluteEnvironment("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
        var state = windows
            ? AbsoluteEnvironment("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : AbsoluteEnvironment("XDG_STATE_HOME") ?? Path.Combine(home, ".local", "state");
        var configOverride = Environment.GetEnvironmentVariable("LLPLAYER_CONFIG_DIR");
        return (string.IsNullOrWhiteSpace(configOverride)
            ? Path.Combine(config, "LLPlayer") : Path.GetFullPath(configOverride), Path.Combine(state, "LLPlayer"));
    }

    public static string ResolveDirectory(string directory)
        => Path.TrimEndingDirectorySeparator(Resolve(new DirectoryInfo(directory)).FullName);

    public static bool SameDirectory(string left, string right)
        => string.Equals(ResolveDirectory(left), ResolveDirectory(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static DirectoryInfo Resolve(DirectoryInfo directory)
    {
        if (directory.Parent is not { } parent)
            return directory;
        var relative = new DirectoryInfo(Path.Combine(Resolve(parent).FullName, directory.Name));
        return relative.LinkTarget is not null && relative.ResolveLinkTarget(true) is DirectoryInfo target
            ? target : relative;
    }

    private static string? AbsoluteEnvironment(string name)
        => Environment.GetEnvironmentVariable(name) is { } value && Path.IsPathFullyQualified(value) ? value : null;
}
