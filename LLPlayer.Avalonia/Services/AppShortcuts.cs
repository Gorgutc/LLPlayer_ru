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
/// <para>
/// The app-level chords are rebindable through the persisted <see cref="AppPrefs.AppShortcutOverrides"/> map; the engine
/// key bindings keep their defaults in this stage.
/// </para>
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

    static readonly HashSet<string> KeyNames = new(Enum.GetNames<WpfKey>(), StringComparer.OrdinalIgnoreCase);

    static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = nameof(WpfKey.Escape),
        ["Enter"] = nameof(WpfKey.Return),
        ["Space"] = nameof(WpfKey.Space),
        ["PgUp"] = nameof(WpfKey.PageUp),
        ["PgDn"] = nameof(WpfKey.PageDown),
        ["Del"] = nameof(WpfKey.Delete),
        ["Ins"] = nameof(WpfKey.Insert),
        ["←"] = nameof(WpfKey.Left),
        ["→"] = nameof(WpfKey.Right),
        ["↑"] = nameof(WpfKey.Up),
        ["↓"] = nameof(WpfKey.Down),
    };

    readonly Dictionary<string, Action> actions;
    readonly IReadOnlyList<AppShortcut> effective;

    public AppShortcutMap(IReadOnlyDictionary<string, Action> actions, IReadOnlyDictionary<string, string>? overrides = null)
    {
        this.actions = new Dictionary<string, Action>(actions, StringComparer.Ordinal);
        effective = BuildEffective(overrides);
    }

    /// <summary>Distinct app action names in the stable default order.</summary>
    public static IReadOnlyList<string> ActionNames { get; } =
        [.. Defaults.Select(s => s.ActionName).Distinct(StringComparer.Ordinal)];

    /// <summary>The effective chords: default chords with the valid persisted overrides applied (one chord per action).</summary>
    public IReadOnlyList<AppShortcut> Shortcuts => effective;

    public static string FormatChord(WpfKey key, bool ctrl, bool alt, bool shift)
    {
        if (key is WpfKey.None)
            return "";
        List<string> parts = [];
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Parses <c>Ctrl+Alt+Shift+Key</c> (canonical key enum name or a few display aliases).</summary>
    public static bool TryParseChord(string? chord, out WpfKey key, out bool ctrl, out bool alt, out bool shift)
    {
        key = WpfKey.None;
        ctrl = alt = shift = false;
        if (string.IsNullOrWhiteSpace(chord))
            return false;

        string[] parts = chord.Split('+', StringSplitOptions.None);
        if (parts.Length == 0)
            return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            string modifier = parts[i].Trim();
            if (modifier.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || modifier.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                if (ctrl) return false;
                ctrl = true;
            }
            else if (modifier.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                if (alt) return false;
                alt = true;
            }
            else if (modifier.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                if (shift) return false;
                shift = true;
            }
            else
            {
                return false;
            }
        }

        string token = parts[^1].Trim();
        if (token.Length == 0)
            return false;
        if (Aliases.TryGetValue(token, out string? canonical))
            token = canonical;
        if (!KeyNames.Contains(token) || !Enum.TryParse(token, true, out WpfKey parsed) || parsed is WpfKey.None)
            return false;
        if (parsed is WpfKey.LeftCtrl or WpfKey.RightCtrl or WpfKey.LeftAlt or WpfKey.RightAlt
            or WpfKey.LeftShift or WpfKey.RightShift or WpfKey.LWin or WpfKey.RWin)
            return false;

        key = parsed;
        return true;
    }

    /// <summary>Builds the effective list: an override replaces every default chord of its action with one chord.</summary>
    public static IReadOnlyList<AppShortcut> BuildEffective(IReadOnlyDictionary<string, string>? overrides)
    {
        List<AppShortcut> result = [];
        foreach (IGrouping<string, AppShortcut> group in Defaults.GroupBy(s => s.ActionName, StringComparer.Ordinal))
        {
            AppShortcut first = group.First();
            if (overrides != null && overrides.TryGetValue(group.Key, out string? chord)
                && TryParseChord(chord, out WpfKey key, out bool ctrl, out bool alt, out bool shift))
            {
                result.Add(first with { Key = key, Ctrl = ctrl, Alt = alt, Shift = shift });
            }
            else
            {
                result.AddRange(group);
            }
        }

        return result;
    }

    /// <summary>Sanitizes a raw override map: known actions only, parseable chords only, no chord duplicates.</summary>
    public static bool TryNormalizeOverrides(IReadOnlyDictionary<string, string>? raw, out Dictionary<string, string> normalized)
    {
        normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (raw is null)
            return true;

        // Collect every changed entry first and validate the whole candidate against the same effective map that
        // Settings Save validates. This keeps swaps of two default chords (A->B's default, B->A's default) accepted
        // instead of silently dropping both during a greedy per-action pass.
        Dictionary<string, string> candidate = [];
        foreach (string action in ActionNames)
        {
            if (!raw.TryGetValue(action, out string? chord)
                || !TryParseChord(chord, out WpfKey key, out bool ctrl, out bool alt, out bool shift))
                continue;

            string canonical = FormatChord(key, ctrl, alt, shift);
            AppShortcut first = Defaults.First(d => d.ActionName == action);
            string defaultCanonical = FormatChord(first.Key, first.Ctrl, first.Alt, first.Shift);
            // An override equal to the first default chord is dropped: the action keeps every default chord.
            if (canonical == defaultCanonical)
                continue;
            candidate[action] = canonical;
        }

        if (!HasDuplicateChord(BuildEffective(candidate), out _))
        {
            normalized = candidate;
            return true;
        }

        // Real duplicate: accept changed entries deterministically, skipping only the conflicting ones.
        foreach (string action in ActionNames)
        {
            if (!candidate.TryGetValue(action, out string? chord))
                continue;
            Dictionary<string, string> trial = new(normalized, StringComparer.Ordinal)
            {
                [action] = chord,
            };
            if (HasDuplicateChord(BuildEffective(trial), out _))
                continue;
            normalized[action] = chord;
        }

        return true;
    }

    static bool HasDuplicateChord(IReadOnlyList<AppShortcut> shortcuts, out string duplicate)
    {
        duplicate = "";
        Dictionary<(WpfKey, bool, bool, bool), string> seen = [];
        foreach (AppShortcut s in shortcuts)
        {
            (WpfKey, bool, bool, bool) signature = (s.Key, s.Ctrl, s.Alt, s.Shift);
            if (!seen.TryAdd(signature, s.ActionName))
            {
                duplicate = FormatChord(s.Key, s.Ctrl, s.Alt, s.Shift);
                return true;
            }
        }

        return false;
    }

    /// <summary>Duplicate-check of the effective map (the settings draft blocks Save on a conflict).</summary>
    public bool TryValidateEffective(out string error)
    {
        error = "";
        if (!HasDuplicateChord(effective, out string duplicate))
            return true;
        AppShortcut[] owners = [.. effective.Where(s => FormatChord(s.Key, s.Ctrl, s.Alt, s.Shift) == duplicate)];
        error = $"The shortcut {duplicate} is assigned more than once ({string.Join(", ", owners.Select(o => o.Description))}).";
        return false;
    }

    public AppShortcut? Find(WpfKey key, bool ctrl, bool alt, bool shift)
        => effective.FirstOrDefault(s => s.Matches(key, ctrl, alt, shift));

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
    public IReadOnlyList<ShortcutInfo> BuildCheatSheet(IEnumerable<KeyBinding> engineBindings)
        => BuildCheatSheet(engineBindings, effective);

    /// <summary>Cheat-sheet rows for an explicit app-shortcut list (defaults are used when omitted).</summary>
    public static IReadOnlyList<ShortcutInfo> BuildCheatSheet(IEnumerable<KeyBinding> engineBindings,
        IReadOnlyList<AppShortcut>? appShortcuts = null)
    {
        List<ShortcutInfo> rows = [];

        foreach (IGrouping<string, AppShortcut> g in (appShortcuts ?? Defaults).GroupBy(s => s.ActionName, StringComparer.Ordinal))
        {
            rows.Add(new ShortcutInfo(AppGroup(g.Key), g.Key, g.First().Description,
                g.Select(s => string.Join("+", KeyMapper.ChordParts(s.Key, s.Ctrl, s.Alt, s.Shift))).ToList()));
        }

        foreach (IGrouping<KeyBindingAction, KeyBinding> g in engineBindings
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
