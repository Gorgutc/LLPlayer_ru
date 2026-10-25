using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AwesomeAssertions;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using AvKey = Avalonia.Input.Key;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public class HotkeyValidationRepairTests
{
    public static TheoryData<string> InvalidStoredChords => new()
    {
        "45",                         // numeric value of the defined B key
        "Ctrl+45",
        "Ctrl+00045",
        "0",
        "Ctrl+1",                     // a defined key value is still not a key name
        "Ctrl+999999",                // undefined enum value
        "Ctrl+-1",
        "Ctrl+2147483647",
        "Ctrl+B,B",                   // Enum.TryParse also accepts comma lists for non-flags enums
        "Ctrl+B,C",
        "Ctrl+None,B",
        "Ctrl+A,B",                   // 44 | 45 happens to be the defined B key
        "Ctrl+Escape,Return",
        "Ctrl+NotAKey",
    };

    [Theory]
    [InlineData(KeyModifiers.Meta)]
    [InlineData(KeyModifiers.Meta | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)]
    [InlineData((KeyModifiers)16)]
    [InlineData((KeyModifiers)16 | KeyModifiers.Control)]
    [InlineData((KeyModifiers)int.MinValue | KeyModifiers.Shift)]
    public void Capture_formatter_rejects_any_unsupported_modifier_bits(KeyModifiers modifiers)
    {
        ChordCaptureButton.FromKey(AvKey.B, modifiers).Should().BeNull(
            "an unsupported modifier must not be silently removed from the captured chord");
    }

    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Meta)]
    [InlineData(RawInputModifiers.Meta | RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Meta | RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Shift)]
    public void Unsupported_binding_keeps_the_previous_chord_and_capture_active(RawInputModifiers modifiers)
    {
        ChordCaptureButton button = new() { Chord = "Ctrl+Shift+F1" };
        Window window = new() { Width = 320, Height = 120, Content = button };
        window.Show();
        try
        {
            button.BeginCapture();
            window.KeyPress(AvKey.B, modifiers, PhysicalKey.B, "b");
            window.KeyRelease(AvKey.B, modifiers, PhysicalKey.B, "b");
            Dispatcher.UIThread.RunJobs();

            button.Chord.Should().Be("Ctrl+Shift+F1");
            button.IsCapturing.Should().BeTrue("the user can try a supported chord without restarting capture");

            window.KeyPress(AvKey.C, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.C, "c");
            window.KeyRelease(AvKey.C, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.C, "c");
            Dispatcher.UIThread.RunJobs();
            button.Chord.Should().Be("Ctrl+Alt+C");
            button.IsCapturing.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Meta)]
    public void Escape_still_cancels_capture_with_modifiers(KeyModifiers modifiers)
    {
        ChordCaptureButton button = new() { Chord = "Ctrl+Shift+F1" };
        Window window = new() { Width = 320, Height = 120, Content = button };
        window.Show();
        try
        {
            button.BeginCapture();
            button.RaiseEvent(KeyPress(button, AvKey.Escape, modifiers));

            button.Chord.Should().Be("Ctrl+Shift+F1");
            button.IsCapturing.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [MemberData(nameof(InvalidStoredChords))]
    public void Parser_rejects_numeric_comma_list_and_unknown_key_tokens(string chord)
    {
        AppShortcutMap.TryParseChord(chord, out _, out _, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("control+alt+shift+f12", WpfKey.F12, true, true, true)]
    [InlineData(" Ctrl + b ", WpfKey.B, true, false, false)]
    [InlineData("Esc", WpfKey.Escape, false, false, false)]
    [InlineData("Enter", WpfKey.Return, false, false, false)]
    [InlineData("Return", WpfKey.Return, false, false, false)]
    [InlineData("Space", WpfKey.Space, false, false, false)]
    [InlineData("PgUp", WpfKey.PageUp, false, false, false)]
    [InlineData("PageUp", WpfKey.PageUp, false, false, false)]
    [InlineData("Prior", WpfKey.PageUp, false, false, false)]
    [InlineData("PgDn", WpfKey.PageDown, false, false, false)]
    [InlineData("PageDown", WpfKey.PageDown, false, false, false)]
    [InlineData("Next", WpfKey.PageDown, false, false, false)]
    [InlineData("Del", WpfKey.Delete, false, false, false)]
    [InlineData("Ins", WpfKey.Insert, false, false, false)]
    [InlineData("←", WpfKey.Left, false, false, false)]
    [InlineData("→", WpfKey.Right, false, false, false)]
    [InlineData("↑", WpfKey.Up, false, false, false)]
    [InlineData("↓", WpfKey.Down, false, false, false)]
    [InlineData("Capital", WpfKey.CapsLock, false, false, false)]
    [InlineData("CapsLock", WpfKey.CapsLock, false, false, false)]
    [InlineData("Oem2", WpfKey.OemQuestion, false, false, false)]
    [InlineData("OemQuestion", WpfKey.OemQuestion, false, false, false)]
    [InlineData("D1", WpfKey.D1, false, false, false)]
    [InlineData("D0", WpfKey.D0, false, false, false)]
    [InlineData("NumPad1", WpfKey.NumPad1, false, false, false)]
    public void Parser_preserves_named_keys_enum_aliases_and_display_aliases(
        string chord, WpfKey expectedKey, bool expectedCtrl, bool expectedAlt, bool expectedShift)
    {
        AppShortcutMap.TryParseChord(chord, out WpfKey key, out bool ctrl, out bool alt, out bool shift)
            .Should().BeTrue();
        key.Should().Be(expectedKey);
        ctrl.Should().Be(expectedCtrl);
        alt.Should().Be(expectedAlt);
        shift.Should().Be(expectedShift);
    }

    [Theory]
    [MemberData(nameof(InvalidStoredChords))]
    public void Invalid_persisted_override_is_dropped_and_all_default_chords_execute(string invalidChord)
    {
        using TempDir temp = new();
        string path = Path.Combine(temp.Path, "preferences.json");
        string raw = JsonSerializer.Serialize(new
        {
            Volume = 42,
            AppShortcutOverrides = new Dictionary<string, string>
            {
                [AppShortcutMap.OpenWindowCheatSheet] = invalidChord,
                [AppShortcutMap.ToggleTheme] = "Ctrl+Alt+T",
            },
        });
        File.WriteAllText(path, raw);
        PrefsStore store = new(path);

        AppPrefs loaded = store.Load();

        loaded.AppShortcutOverrides.Should().NotContainKey(AppShortcutMap.OpenWindowCheatSheet);
        loaded.AppShortcutOverrides[AppShortcutMap.ToggleTheme].Should().Be("Ctrl+Alt+T");
        loaded.Volume.Should().Be(42, "a bad chord must not cause whole-file defaults");
        store.LoadWarning.Should().BeNull();
        File.ReadAllText(path).Should().Be(raw, "loading must not rewrite the persisted file");

        int opens = 0;
        AppShortcutMap map = new(new Dictionary<string, Action>
        {
            [AppShortcutMap.OpenWindowCheatSheet] = () => opens++,
        }, loaded.AppShortcutOverrides);
        map.Shortcuts.Count(s => s.ActionName == AppShortcutMap.OpenWindowCheatSheet).Should().Be(2);
        map.TryExecute(WpfKey.F1, false, false, false).Should().BeTrue();
        map.TryExecute(WpfKey.OemQuestion, false, false, true).Should().BeTrue();
        opens.Should().Be(2);
    }

    static KeyEventArgs KeyPress(ChordCaptureButton button, AvKey key, KeyModifiers modifiers) => new()
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Source = button,
        Key = key,
        KeyModifiers = modifiers,
    };
}
