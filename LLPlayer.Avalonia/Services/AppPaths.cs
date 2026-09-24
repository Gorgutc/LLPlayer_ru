namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Per-user file locations of the Avalonia app. Platform-neutral (F-13 target "one core + one UI"):
/// <list type="bullet">
/// <item>Linux/other (XDG Base Directory specification): config <c>$LLPLAYER_CONFIG_DIR</c>, else
/// <c>$XDG_CONFIG_HOME/LLPlayer</c>, else <c>~/.config/LLPlayer</c>; state (logs, crash log)
/// <c>$XDG_STATE_HOME/LLPlayer</c>, else <c>~/.local/state/LLPlayer</c>. Relative XDG values are ignored, as the
/// specification requires.</item>
/// <item>Windows: config <c>$LLPLAYER_CONFIG_DIR</c>, else <c>%APPDATA%\LLPlayer</c>; state
/// <c>%LOCALAPPDATA%\LLPlayer</c>. File names differ from anything the WPF app writes.</item>
/// </list>
/// </summary>
public sealed record AppPaths(string ConfigDir, string StateDir)
{
    public const string ConfigDirVariable = "LLPLAYER_CONFIG_DIR";
    public const string AppFolderName = "LLPlayer";

    /// <summary>App preferences (volume, theme, recent files, ...).</summary>
    public string PrefsFile => Path.Combine(ConfigDir, "LLPlayer.Avalonia.json");

    /// <summary>Unhandled-exception log.</summary>
    public string CrashLogFile => Path.Combine(StateDir, "crash.log");

    /// <summary>FlyleafLib engine log (warnings and errors).</summary>
    public string EngineLogFile => Path.Combine(StateDir, "flyleaf.log");

    /// <param name="getEnvironmentVariable">Environment lookup (tests inject a dictionary).</param>
    /// <param name="homeDirectory">Home folder override (tests); also makes the fallbacks deterministic.</param>
    /// <param name="isWindows">Platform override (tests); defaults to <see cref="OperatingSystem.IsWindows"/>.</param>
    public static AppPaths FromEnvironment(Func<string, string?>? getEnvironmentVariable = null, string? homeDirectory = null, bool? isWindows = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        bool windows = isWindows ?? OperatingSystem.IsWindows();

        string home = homeDirectory
            ?? NonEmpty(getEnvironmentVariable(windows ? "USERPROFILE" : "HOME"))
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string? configOverride = NonEmpty(getEnvironmentVariable(ConfigDirVariable)) is { } overrideDir
            ? Path.GetFullPath(overrideDir)
            : null;

        if (windows)
        {
            string roaming = AbsoluteOrNull(getEnvironmentVariable("APPDATA"))
                ?? KnownFolderOrNull(Environment.SpecialFolder.ApplicationData, homeDirectory)
                ?? Path.Combine(home, "AppData", "Roaming");
            string local = AbsoluteOrNull(getEnvironmentVariable("LOCALAPPDATA"))
                ?? KnownFolderOrNull(Environment.SpecialFolder.LocalApplicationData, homeDirectory)
                ?? Path.Combine(home, "AppData", "Local");

            return new AppPaths(configOverride ?? Path.Combine(roaming, AppFolderName), Path.Combine(local, AppFolderName));
        }

        string configDir = configOverride
            ?? Path.Combine(AbsoluteOrNull(getEnvironmentVariable("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config"), AppFolderName);

        string stateDir = Path.Combine(
            AbsoluteOrNull(getEnvironmentVariable("XDG_STATE_HOME")) ?? Path.Combine(home, ".local", "state"),
            AppFolderName);

        return new AppPaths(configDir, stateDir);
    }

    // The real known folder only when the caller did not pin the home folder (tests stay independent of the host OS).
    static string? KnownFolderOrNull(Environment.SpecialFolder folder, string? homeDirectory)
        => homeDirectory is null ? NonEmpty(Environment.GetFolderPath(folder)) : null;

    static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    static string? AbsoluteOrNull(string? value)
        => NonEmpty(value) is { } v && Path.IsPathRooted(v) ? v : null;
}
