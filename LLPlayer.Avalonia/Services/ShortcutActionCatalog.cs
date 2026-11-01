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
            ]).ToArray());

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

    public static string AppGroup(string action) => action switch
    {
        AppShortcutMap.ToggleSidebar or AppShortcutMap.OpenWindowCheatSheet or AppShortcutMap.ToggleTheme
            or AppShortcutMap.OpenWindowSettings or AppShortcutMap.OpenWindowWordManager
            or AppShortcutMap.OpenWindowSubsExporter => "Window",
        _ => "Subtitles",
    };

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
