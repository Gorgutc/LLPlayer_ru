using System.ComponentModel;
using System.Reflection;
using FlyleafLib.MediaPlayer;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// App-level shortcut (owned by the Linux app, not by the engine). <see cref="ActionName"/> uses the WPF app's
/// <c>CustomKeyBindingAction</c> names so configs, docs and the cheat sheet speak the same language.
/// </summary>
public sealed record AppShortcut(string ActionName, WpfKey Key, bool Ctrl, bool Alt, bool Shift, string Description)
{
    public bool Matches(WpfKey key, bool ctrl, bool alt, bool shift)
        => Key == key && Ctrl == ctrl && Alt == alt && Shift == shift;
}

/// <summary>A row of the cheat sheet.</summary>
public sealed record ShortcutInfo(string Group, string ActionName, string Description, IReadOnlyList<string> Keys);

/// <summary>
/// Keyboard routing of the Linux app. App-level chords (sidebar, search, cheat sheet, subtitle size/position, copy)
/// are matched here first with the same default chords as the WPF app (AppActions.DefaultCustomActionsMap); every other
/// key goes to the engine's key bindings (<c>Player.KeyDown/KeyUp</c> with FlyleafLib's default map: Space
/// play/pause, arrows seek/volume, A/S/D subtitle seek, F fullscreen, Ctrl+O open, ...).
/// </summary>
public sealed class AppShortcutMap
{
    public const string ToggleSidebar = "ToggleSidebar";
    public const string ActivateSubsSearch = "ActivateSubsSearch";
    public const string OpenWindowCheatSheet = "OpenWindowCheatSheet";
    public const string SubsSizeIncrease = "SubsSizeIncrease";
    public const string SubsSizeDecrease = "SubsSizeDecrease";
    public const string SubsPositionUp = "SubsPositionUp";
    public const string SubsPositionDown = "SubsPositionDown";
    public const string SubsPrimaryTextCopy = "SubsPrimaryTextCopy";
    public const string OpenSubtitles = "OpenSubtitles";
    public const string ToggleTheme = "ToggleTheme";

    public static readonly IReadOnlyList<AppShortcut> Defaults =
    [
        new(SubsPositionUp,       WpfKey.Up,    false, false, true,  "Subtitles Position Up"),
        new(SubsPositionDown,     WpfKey.Down,  false, false, true,  "Subtitles Position Down"),
        new(SubsSizeIncrease,     WpfKey.Right, false, false, true,  "Subtitles Size Increase"),
        new(SubsSizeDecrease,     WpfKey.Left,  false, false, true,  "Subtitles Size Decrease"),
        new(SubsPrimaryTextCopy,  WpfKey.C,     true,  false, false, "Copy Primary Subtitles Text"),
        new(ActivateSubsSearch,   WpfKey.F,     true,  false, false, "Activate Subtitles Search in Sidebar"),
        new(ToggleSidebar,        WpfKey.B,     true,  false, false, "Toggle Subtitles Sidebar"),
        new(OpenWindowCheatSheet, WpfKey.F1,    false, false, false, "Open Cheat Sheet Window"),
        // Linux app additions (no default chord in the WPF app)
        new(OpenWindowCheatSheet, WpfKey.OemQuestion, false, false, true, "Open Cheat Sheet Window"),
        new(OpenSubtitles,        WpfKey.O,     true,  false, true,  "Open Subtitles File"),
        new(ToggleTheme,          WpfKey.T,     true,  false, true,  "Toggle Dark / Light Theme"),
    ];

    readonly Dictionary<string, Action> actions;

    public AppShortcutMap(IReadOnlyDictionary<string, Action> actions)
    {
        this.actions = new Dictionary<string, Action>(actions, StringComparer.Ordinal);
    }

    public IReadOnlyList<AppShortcut> Shortcuts => Defaults;

    public AppShortcut? Find(WpfKey key, bool ctrl, bool alt, bool shift)
        => Defaults.FirstOrDefault(s => s.Matches(key, ctrl, alt, shift));

    /// <summary>Runs the app action bound to the chord. Returns false when the chord is not an app shortcut.</summary>
    public bool TryExecute(WpfKey key, bool ctrl, bool alt, bool shift)
    {
        AppShortcut? s = Find(key, ctrl, alt, shift);
        if (s == null || !actions.TryGetValue(s.ActionName, out Action? action))
            return false;

        action();
        return true;
    }

    /// <summary>Cheat-sheet rows: app shortcuts + enabled engine bindings, grouped, chords merged per action.</summary>
    public static IReadOnlyList<ShortcutInfo> BuildCheatSheet(IEnumerable<KeyBinding> engineBindings)
    {
        List<ShortcutInfo> rows = [];

        foreach (var g in Defaults.GroupBy(s => s.ActionName))
        {
            rows.Add(new ShortcutInfo(AppGroup(g.Key), g.Key, g.First().Description,
                g.Select(s => string.Join("+", KeyMapper.ChordParts(s.Key, s.Ctrl, s.Alt, s.Shift))).ToList()));
        }

        foreach (var g in engineBindings
                     .Where(b => b.IsEnabled && b.Action != KeyBindingAction.Custom)
                     .GroupBy(b => b.Action))
        {
            rows.Add(new ShortcutInfo(EngineGroup(g.Key), g.Key.ToString(), Describe(g.Key),
                g.Select(b => string.Join("+", KeyMapper.ChordParts(b.Key, b.Ctrl, b.Alt, b.Shift))).Distinct().ToList()));
        }

        string[] order = ["Playback", "Subtitles", "Audio", "Video", "Window", "Other"];
        return rows.OrderBy(r => Array.IndexOf(order, r.Group) is var i && i < 0 ? order.Length : i)
                   .ThenBy(r => r.Description, StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    static string AppGroup(string action) => action switch
    {
        ToggleSidebar or OpenWindowCheatSheet or ToggleTheme => "Window",
        _ => "Subtitles",
    };

    static string EngineGroup(KeyBindingAction action)
    {
        string name = action.ToString();
        if (name.StartsWith("Subs", StringComparison.Ordinal) || name.Contains("Subtitles", StringComparison.Ordinal))
            return "Subtitles";
        if (name.Contains("Audio", StringComparison.Ordinal) || name.Contains("Volume", StringComparison.Ordinal) || name.Contains("Mute", StringComparison.Ordinal))
            return "Audio";
        if (name.Contains("Zoom", StringComparison.Ordinal) || name.Contains("Video", StringComparison.Ordinal) || name.Contains("Ratio", StringComparison.Ordinal))
            return "Video";
        if (name.Contains("Screen", StringComparison.Ordinal))
            return "Window";
        if (name.StartsWith("Open", StringComparison.Ordinal) || name.StartsWith("Copy", StringComparison.Ordinal))
            return "Other";
        return "Playback";
    }

    static string Describe(KeyBindingAction action)
    {
        FieldInfo? field = typeof(KeyBindingAction).GetField(action.ToString());
        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? action.ToString();
    }
}
