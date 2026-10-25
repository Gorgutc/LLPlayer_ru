using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.Services;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Controls;

/// <summary>
/// Button that captures the next key chord and writes it into <see cref="Chord"/> as a canonical
/// <c>[Ctrl+][Alt+][Shift+]Key</c> string (the persistence format). Click it, press the chord; Esc, losing focus or
/// clicking again cancels capture.
/// </summary>
public sealed class ChordCaptureButton : Button
{
    public static readonly StyledProperty<string> ChordProperty =
        AvaloniaProperty.Register<ChordCaptureButton, string>(nameof(Chord), "");

    bool capturing;

    public ChordCaptureButton()
    {
        Click += (_, _) =>
        {
            if (capturing)
                StopCapture();
            else
                BeginCapture();
        };
        LostFocus += (_, _) => StopCapture();
    }

    public string Chord
    {
        get => GetValue(ChordProperty);
        set => SetValue(ChordProperty, value);
    }

    public bool IsCapturing => capturing;

    public void BeginCapture()
    {
        capturing = true;
        PseudoClasses.Set(":capturing", true);
        Opacity = 0.55;
        _ = Focus();
    }

    /// <summary>Chord string for a key press (null for modifier-only/unmapped keys). Shared with the tests.</summary>
    public static string? FromKey(Key key, KeyModifiers modifiers)
    {
        WpfKey wpf = KeyMapper.ToWpf(key);
        if (wpf is WpfKey.None or WpfKey.LeftCtrl or WpfKey.RightCtrl or WpfKey.LeftAlt or WpfKey.RightAlt
            or WpfKey.LeftShift or WpfKey.RightShift or WpfKey.LWin or WpfKey.RWin)
            return null;

        return AppShortcutMap.FormatChord(wpf,
            modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Alt),
            modifiers.HasFlag(KeyModifiers.Shift));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!capturing)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            StopCapture();
            return;
        }

        string? chord = FromKey(e.Key, e.KeyModifiers);
        if (chord is null)
            return;

        SetCurrentValue(ChordProperty, chord);
        StopCapture();
    }

    void StopCapture()
    {
        if (!capturing)
            return;
        capturing = false;
        PseudoClasses.Set(":capturing", false);
        Opacity = 1;
    }
}
