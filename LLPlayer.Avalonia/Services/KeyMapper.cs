using Avalonia.Input;
using AvKey = Avalonia.Input.Key;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// Maps Avalonia keys to FlyleafLib's portable copy of WPF's <see cref="WpfKey"/> (the key bindings type) and tracks
/// the modifier state for <c>Player.KeyStateProvider</c>.
/// <para>
/// Avalonia's <see cref="AvKey"/> was derived from WPF's enum and keeps its values for every member WPF has (0..172);
/// members Avalonia added later (Fn* arrows, media remote keys, &gt;= 10000) have no WPF equivalent and map to
/// <see cref="WpfKey.None"/>. The tests assert the name/value identity for every shared member.
/// </para>
/// </summary>
public sealed class KeyMapper
{
    const int MaxWpfKey = (int)WpfKey.DeadCharProcessed;

    KeyModifiers modifiers;

    public static WpfKey ToWpf(AvKey key)
    {
        int value = (int)key;
        return value is >= 0 and <= MaxWpfKey ? (WpfKey)value : WpfKey.None;
    }

    /// <summary>Current modifiers (from the last key / pointer event the window routed here).</summary>
    public KeyModifiers Modifiers => modifiers;

    public void Update(KeyModifiers current) => modifiers = current;

    /// <summary>
    /// Implementation of <c>Player.KeyStateProvider</c>: FlyleafLib only asks for the Alt / Ctrl / Shift keys (left or
    /// right); Avalonia reports modifiers without side, so both sides answer the same.
    /// </summary>
    public bool IsKeyDown(WpfKey key) => key switch
    {
        WpfKey.LeftAlt or WpfKey.RightAlt => modifiers.HasFlag(KeyModifiers.Alt),
        WpfKey.LeftCtrl or WpfKey.RightCtrl => modifiers.HasFlag(KeyModifiers.Control),
        WpfKey.LeftShift or WpfKey.RightShift => modifiers.HasFlag(KeyModifiers.Shift),
        WpfKey.LWin or WpfKey.RWin => modifiers.HasFlag(KeyModifiers.Meta),
        _ => false,
    };

    public static string[] ChordParts(WpfKey key, bool ctrl, bool alt, bool shift)
        => AppShortcutMap.DisplayChordParts(key, ctrl, alt, shift);

    public static string KeyName(WpfKey key) => AppShortcutMap.DisplayKeyName(key);
}
