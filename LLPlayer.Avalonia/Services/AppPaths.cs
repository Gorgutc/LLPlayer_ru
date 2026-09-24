namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Per-user file locations of the Linux app, following the XDG Base Directory specification:
/// <list type="bullet">
/// <item>config: <c>$LLPLAYER_CONFIG_DIR</c>, else <c>$XDG_CONFIG_HOME/LLPlayer</c>, else <c>~/.config/LLPlayer</c></item>
/// <item>state (logs, crash log): <c>$XDG_STATE_HOME/LLPlayer</c>, else <c>~/.local/state/LLPlayer</c></item>
/// </list>
/// Relative XDG values are ignored, as the specification requires.
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

    public static AppPaths FromEnvironment(Func<string, string?>? getEnvironmentVariable = null, string? homeDirectory = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string home = homeDirectory
            ?? NonEmpty(getEnvironmentVariable("HOME"))
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string configDir = NonEmpty(getEnvironmentVariable(ConfigDirVariable)) is { } overrideDir
            ? Path.GetFullPath(overrideDir)
            : Path.Combine(AbsoluteOrNull(getEnvironmentVariable("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config"), AppFolderName);

        string stateDir = Path.Combine(
            AbsoluteOrNull(getEnvironmentVariable("XDG_STATE_HOME")) ?? Path.Combine(home, ".local", "state"),
            AppFolderName);

        return new AppPaths(configDir, stateDir);
    }

    static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    static string? AbsoluteOrNull(string? value)
        => NonEmpty(value) is { } v && Path.IsPathRooted(v) ? v : null;
}
