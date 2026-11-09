using FlyleafLib.MediaPlayer;

namespace LLPlayer.Avalonia.Services;

public enum ShortcutActionTarget { Engine, Application }

/// <summary>Immutable editor metadata. Runtime delegates stay in the existing app/native handlers.</summary>
public sealed record ShortcutActionDescriptor(ShortcutActionTarget Target, string Name, string Description,
    string Group, KeyBindingAction? EngineAction = null)
{
    public override string ToString() => $"{Description} ({Name})";
}

/// <summary>Only implemented portable actions are selectable; adding metadata never adds a default binding.</summary>
public static class ShortcutActionCatalog
{
    public static IReadOnlyList<ShortcutActionDescriptor> EngineActions { get; } = Array.AsReadOnly(
        Enum.GetValues<KeyBindingAction>().Where(action => action != KeyBindingAction.Custom)
            .Select(action => new ShortcutActionDescriptor(ShortcutActionTarget.Engine, action.ToString(),
                EngineKeyBindings.Describe(action), EngineGroup(action), action)).ToArray());

    public static IReadOnlyList<ShortcutActionDescriptor> AppActions { get; } = Array.AsReadOnly(
        AppShortcutMap.Defaults.DistinctBy(row => row.ActionName)
            .Select(row => new ShortcutActionDescriptor(ShortcutActionTarget.Application, row.ActionName,
                row.Description, AppGroup(row.ActionName)))
            .Concat([
                new(ShortcutActionTarget.Application, AppShortcutMap.OpenWindowSettings, "Open Settings Window", "Window"),
                new(ShortcutActionTarget.Application, AppShortcutMap.OpenWindowWordManager, "Open Word Manager Window", "Window"),
                new(ShortcutActionTarget.Application, AppShortcutMap.OpenWindowSubsExporter, "Open Subtitles Export Window", "Window"),
                App(AppShortcutMap.OpenNextFile, "Open Next File", "Playback"),
                App(AppShortcutMap.OpenPrevFile, "Open Previous File", "Playback"),
                App(AppShortcutMap.OpenCurrentPath, "Open Current Media Location", "Playback"),
                App(AppShortcutMap.CopyMediaPath, "Copy Current Media Path", "Playback"),
                App(AppShortcutMap.SubsPrimarySizeIncrease, "Primary Subtitles Size Increase", "Subtitles"),
                App(AppShortcutMap.SubsPrimarySizeDecrease, "Primary Subtitles Size Decrease", "Subtitles"),
                App(AppShortcutMap.SubsSecondarySizeIncrease, "Secondary Subtitles Size Increase", "Subtitles"),
                App(AppShortcutMap.SubsSecondarySizeDecrease, "Secondary Subtitles Size Decrease", "Subtitles"),
                App(AppShortcutMap.SubsDistanceIncrease, "Primary/Secondary Subtitles Distance Increase", "Subtitles"),
                App(AppShortcutMap.SubsDistanceDecrease, "Primary/Secondary Subtitles Distance Decrease", "Subtitles"),
                App(AppShortcutMap.SubsTextCopy, "Copy All Subtitles Text", "Subtitles"),
                App(AppShortcutMap.SubsSecondaryTextCopy, "Copy Secondary Subtitles Text", "Subtitles"),
                App(AppShortcutMap.ToggleSubsAutoTextCopy, "Toggle Auto Subtitles Text Copy", "Subtitles"),
                App(AppShortcutMap.ToggleSidebarShowSecondary, "Toggle Primary / Secondary in Subtitles Sidebar", "Subtitles"),
                App(AppShortcutMap.ToggleSidebarShowOriginalText, "Toggle Original Text in Subtitles Sidebar", "Subtitles"),
                App(AppShortcutMap.ToggleAlwaysOnTop, "Toggle Always On Top", "Window"),
                App(AppShortcutMap.OpenWindowSubsDownloader, "Open Subtitles Downloader Window", "Window"),
                App(AppShortcutMap.OpenWindowAiInsights, "Open AI Insights Window", "Window"),
                App(AppShortcutMap.OpenWindowBatchSubtitles, "Open Batch Subtitles Window", "Window"),
                App(AppShortcutMap.OpenWindowAbout, "About LLPlayer", "Window"),
                App(AppShortcutMap.OpenWindowPlaybackSettings, "Open Playback Settings", "Window"),
                App(AppShortcutMap.OpenWindowPluginOptions, "Open Plugin Options", "Window"),
                App(AppShortcutMap.OpenWindowPlaylist, "Open Playlist", "Window"),
                App(AppShortcutMap.OpenWindowPdicSettings, "Open PDIC Settings", "Window"),
                App(AppShortcutMap.OpenWindowOnlineVideo, "Open Online Video", "Window"),
                App(AppShortcutMap.OpenWindowTranslation, "Open Translation Settings", "Window"),
                App(AppShortcutMap.OpenWindowRecognition, "Open Recognition and Model Management", "Window"),
                App(AppShortcutMap.OpenWindowDubbing, "Open Dubbing Settings", "Window"),
                App(AppShortcutMap.AppNew, "Launch New Application", "Application"),
                App(AppShortcutMap.AppClone, "Launch Clone Application", "Application"),
                App(AppShortcutMap.AppRestart, "Restart Application", "Application"),
                App(AppShortcutMap.AppExit, "Exit Application", "Application"),
            ]).DistinctBy(row => row.Name, StringComparer.Ordinal).ToArray());

    static ShortcutActionDescriptor App(string name, string description, string group)
        => new(ShortcutActionTarget.Application, name, description, group);

    public static bool TryGetEngine(string? name, out ShortcutActionDescriptor descriptor)
    {
        descriptor = EngineActions.FirstOrDefault(row => row.Name == name)!;
        return descriptor != null;
    }

    public static bool TryGetApp(string? name, out ShortcutActionDescriptor descriptor)
    {
        descriptor = AppActions.FirstOrDefault(row => row.Name == name)!;
        return descriptor != null;
    }

    public static string AppGroup(string action)
    {
        if (action is AppShortcutMap.AppNew or AppShortcutMap.AppClone or AppShortcutMap.AppRestart or AppShortcutMap.AppExit)
            return "Application";
        if (action.StartsWith("OpenWindow", StringComparison.Ordinal) || action is AppShortcutMap.OpenCommandPalette
            or AppShortcutMap.ToggleDebugOverlay or AppShortcutMap.ToggleSidebar or AppShortcutMap.ToggleTheme or AppShortcutMap.ToggleAlwaysOnTop)
            return "Window";
        if (action is AppShortcutMap.OpenNextFile or AppShortcutMap.OpenPrevFile or AppShortcutMap.OpenCurrentPath or AppShortcutMap.CopyMediaPath)
            return "Playback";
        return "Subtitles";
    }

    public static string EngineGroup(KeyBindingAction action)
    {
        string name = action.ToString();
        if (name.StartsWith("Subs", StringComparison.Ordinal) || name.Contains("Subtitles", StringComparison.Ordinal))
            return "Subtitles";
        if (name.Contains("Audio", StringComparison.Ordinal) || name.Contains("Volume", StringComparison.Ordinal) || name.Contains("Mute", StringComparison.Ordinal))
            return "Audio";
        if (name.Contains("Zoom", StringComparison.Ordinal) || name.Contains("Video", StringComparison.Ordinal) || name.Contains("Ratio", StringComparison.Ordinal))
            return "Video";
        if (name.Contains("Screen", StringComparison.Ordinal)) return "Window";
        if (name.StartsWith("Open", StringComparison.Ordinal) || name.StartsWith("Copy", StringComparison.Ordinal)) return "Other";
        return "Playback";
    }
}
