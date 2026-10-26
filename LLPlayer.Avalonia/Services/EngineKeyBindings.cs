using System.ComponentModel;
using System.Reflection;
using FlyleafLib.MediaPlayer;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>One engine key binding as the Avalonia editor sees it (the WPF SettingsKeys row equivalent).</summary>
public sealed record EngineKeyBindingInfo(
    KeyBindingAction Action,
    WpfKey Key,
    bool Ctrl = false,
    bool Alt = false,
    bool Shift = false,
    bool IsKeyUp = false,
    bool IsEnabled = true)
{
    public string FormatChord() => AppShortcutMap.FormatChord(Key, Ctrl, Alt, Shift);
}

/// <summary>One persisted engine key binding (plain strings so hand-edited JSON cannot break start-up).</summary>
public sealed class EngineKeyBindingPref
{
    public string Action { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool IsKeyUp { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Engine key-binding mapping and persistence helpers. The engine owns the default chord list
/// (<see cref="KeysConfig.LoadDefault"/>); the Avalonia app only persists an explicit replacement list and applies it
/// to the live engine config. WPF's PlayerConfig and the engine source are never modified.
/// </summary>
public static class EngineKeyBindings
{
    static IReadOnlyList<EngineKeyBindingInfo>? defaults;

    /// <summary>The engine's own default bindings, mapped to the editor model (custom actions excluded).</summary>
    public static IReadOnlyList<EngineKeyBindingInfo> Defaults()
    {
        if (defaults != null)
            return defaults;

        KeysConfig config = new();
        config.LoadDefault();
        defaults = config.Keys
            .Where(binding => binding.Action != KeyBindingAction.Custom)
            .Select(binding => new EngineKeyBindingInfo(
                binding.Action, binding.Key, binding.Ctrl, binding.Alt, binding.Shift, binding.IsKeyUp, binding.IsEnabled))
            .ToArray();
        return defaults;
    }

    /// <summary>The enum's Description attribute (same text the WPF editor shows).</summary>
    public static string Describe(KeyBindingAction action)
    {
        MemberInfo? member = typeof(KeyBindingAction).GetMember(action.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? action.ToString();
    }

    /// <summary>Drops unknown/custom actions, unparsable/None keys and modifier-only keys; keeps order and duplicates.</summary>
    public static List<EngineKeyBindingPref> NormalizePrefs(IEnumerable<EngineKeyBindingPref>? raw)
    {
        List<EngineKeyBindingPref> normalized = [];
        if (raw == null)
            return normalized;

        foreach (EngineKeyBindingPref pref in raw)
        {
            if (pref == null
                || !Enum.TryParse(pref.Action, false, out KeyBindingAction action)
                || action == KeyBindingAction.Custom
                || !Enum.TryParse(pref.Key, false, out WpfKey key)
                || key == WpfKey.None
                || key is WpfKey.LeftCtrl or WpfKey.RightCtrl or WpfKey.LeftAlt or WpfKey.RightAlt
                    or WpfKey.LeftShift or WpfKey.RightShift or WpfKey.LWin or WpfKey.RWin)
                continue;

            normalized.Add(new EngineKeyBindingPref
            {
                Action = action.ToString(),
                Key = key.ToString(),
                Ctrl = pref.Ctrl,
                Alt = pref.Alt,
                Shift = pref.Shift,
                IsKeyUp = pref.IsKeyUp,
                IsEnabled = pref.IsEnabled,
            });
        }

        return normalized;
    }

    public static IReadOnlyList<EngineKeyBindingInfo> FromPrefs(IEnumerable<EngineKeyBindingPref>? prefs)
    {
        List<EngineKeyBindingInfo> bindings = [];
        foreach (EngineKeyBindingPref pref in NormalizePrefs(prefs))
        {
            if (!Enum.TryParse(pref.Action, false, out KeyBindingAction action)
                || !Enum.TryParse(pref.Key, false, out WpfKey key))
                continue;
            bindings.Add(new EngineKeyBindingInfo(action, key, pref.Ctrl, pref.Alt, pref.Shift, pref.IsKeyUp, pref.IsEnabled));
        }
        return bindings;
    }

    public static List<EngineKeyBindingPref> ToPrefs(IEnumerable<EngineKeyBindingInfo> bindings)
        => bindings.Select(binding => new EngineKeyBindingPref
        {
            Action = binding.Action.ToString(),
            Key = binding.Key.ToString(),
            Ctrl = binding.Ctrl,
            Alt = binding.Alt,
            Shift = binding.Shift,
            IsKeyUp = binding.IsKeyUp,
            IsEnabled = binding.IsEnabled,
        }).ToList();

    /// <summary>True when the list is exactly the engine default list (order and flags), i.e. no customization remains.</summary>
    public static bool SameAsDefaults(IReadOnlyList<EngineKeyBindingPref> prefs)
    {
        IReadOnlyList<EngineKeyBindingInfo> expected = Defaults();
        if (prefs.Count != expected.Count)
            return false;

        for (int i = 0; i < expected.Count; i++)
        {
            EngineKeyBindingInfo binding = expected[i];
            EngineKeyBindingPref pref = prefs[i];
            if (pref.Action != binding.Action.ToString()
                || pref.Key != binding.Key.ToString()
                || pref.Ctrl != binding.Ctrl
                || pref.Alt != binding.Alt
                || pref.Shift != binding.Shift
                || pref.IsKeyUp != binding.IsKeyUp
                || pref.IsEnabled != binding.IsEnabled)
                return false;
        }

        return true;
    }
}
