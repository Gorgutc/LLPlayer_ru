using System.Globalization;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Wires FlyleafLib's portable seams to the Avalonia app and starts the engine (mirrors the WPF app's FlyleafLoader:
/// same engine options where they apply, same default player config tweaks).
/// </summary>
public static class EngineBootstrap
{
    /// <summary>Installs the UI dispatcher, host services, collection bridge and key-state provider.</summary>
    public static void InstallSeams(IUIDispatcher dispatcher, IHostServices hostServices, CollectionSyncBridge bridge, KeyMapper keys)
    {
        Utils.UIDispatcher = dispatcher;
        Utils.HostServices = hostServices;
        bridge.Install();
        Player.KeyStateProvider = keys.IsKeyDown;
    }

    /// <summary>Environment variable overriding the engine log level (Quiet, Error, Warn, Info, Debug, Trace).</summary>
    public const string LogLevelVariable = "LLPLAYER_LOG_LEVEL";

    public static EngineConfig CreateEngineConfig(string ffmpegDir, AppPaths paths) => new()
    {
        FFmpegPath = ffmpegDir,
        FFmpegHLSLiveSeek = true,
        FFmpegLoadProfile = Flyleaf.FFmpeg.LoadProfile.Filters,
        // No plugins on Linux yet (the YoutubeDL plugin drives yt-dlp.exe).
        PluginsPath = null,
        UIRefresh = true,
        UIRefreshInterval = 100,
        LogOutput = paths.EngineLogFile,
        LogLevel = Enum.TryParse(Environment.GetEnvironmentVariable(LogLevelVariable), true, out LogLevel level) ? level : LogLevel.Warn,
        FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Warn,
    };

    /// <summary>Starts the engine (FFmpeg load). Throws when FFmpeg cannot be loaded.</summary>
    public static void Start(EngineConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(config.LogOutput) ?? ".");
        AudioBackendSelector.Apply();
        Engine.Start(config);
    }

    /// <summary>Default player config of the Linux app (the WPF app's DefaultConfig plus a smoother time bar).</summary>
    public static Config CreatePlayerConfig(AppPrefs prefs)
    {
        Config config = new();
        config.Demuxer.FormatOptToUnderlying = true;
        config.Video.GPUAdapter = "";
        config.Subtitles.SearchLocal = true;
        config.Subtitles.TranslateTargetLanguage = ResolveTargetLanguage(prefs.TranslateTargetLanguage);
        // Seek bar / time label refresh on the engine's UI refresh interval instead of once per second.
        config.Player.UICurTime = UIRefreshType.PerUIRefreshInterval;
        return config;
    }

    public static TargetLanguage ResolveTargetLanguage(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Enum.TryParse(configured, true, out TargetLanguage parsed))
            return parsed;

        CultureInfo culture = Utils.OriginalCulture ?? CultureInfo.CurrentUICulture;
        if (!string.IsNullOrEmpty(culture.Name) && Language.Get(culture).ToTargetLanguage() is { } native)
            return native;

        return TargetLanguage.EnglishAmerican;
    }
}
