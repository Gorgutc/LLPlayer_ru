using ICommand = System.Windows.Input.ICommand;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Views;

public partial class MainWindow
{
    sealed class CommandButtonKeyPress
    {
        public Button? Button { get; init; }
        public ICommand? Command { get; init; }
        public FlyoutBase? Flyout { get; init; }
        public object? Parameter { get; init; }
        public PlaylistViewModel? Playlist { get; init; }
        public object? CurrentToken { get; init; }
        public object? TargetToken { get; init; }
        public int Direction { get; init; }
        public KeyModifiers? SearchShortcutModifiers { get; init; }
        public bool Canceled { get; set; }
        public bool Retired { get; set; }
    }

    // Observe physical downs even outside owned buttons, so a held video key cannot become a button
    // press merely because focus moved before a repeat or release. Other controls retain their existing route.
    readonly Dictionary<Key, CommandButtonKeyPress> commandButtonKeys = [];

    Button? FocusedParityCommandButton(Key key)
    {
        if (FocusManager?.GetFocusedElement() is not Button button) return null;
        bool searchToggle = key == Key.Space && (ReferenceEquals(button, MatchCaseButton)
            || ReferenceEquals(button, WholeWordButton) || ReferenceEquals(button, RegexButton));
        return searchToggle || button.Name is "CueVoiceButton" or "CancelAutomationJobButton"
            || ReferenceEquals(button, MediaTitleButton) || ReferenceEquals(button, PreviousPlaylistButton)
            || ReferenceEquals(button, NextPlaylistButton) || ReferenceEquals(button, AutomationJobsButton) ? button : null;
    }

    bool TryHandleCommandButtonKeyDown(KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return false;
        Button? button = FocusedParityCommandButton(e.Key);
        if (commandButtonKeys.TryGetValue(e.Key, out CommandButtonKeyPress? held))
        {
            if (held.SearchShortcutModifiers is { } modifiers)
            {
                // The original chord stays with the shortcut router. A changed chord during a repeat is no new press.
                if (!held.Retired && e.KeyModifiers == modifiers && button is null or ToggleButton)
                    DispatchWindowKeyDown(e, IsTextInputFocused());
            }
            else if (held.Button == null && !held.Retired && button == null) return false;
            e.Handled = true;
            return true;
        }
        if (button == null)
        {
            commandButtonKeys[e.Key] = new();
            return false;
        }

        bool nativeToggle = button is ToggleButton;
        if (nativeToggle && e.KeyModifiers != KeyModifiers.None)
        {
            commandButtonKeys[e.Key] = new() { SearchShortcutModifiers = e.KeyModifiers };
            DispatchWindowKeyDown(e, textFocused: false);
            // An unbound chord must not start a native Space press either.
            e.Handled = true;
            return true;
        }

        int direction = ReferenceEquals(button, PreviousPlaylistButton) ? -1
            : ReferenceEquals(button, NextPlaylistButton) ? 1 : 0;
        PlaylistViewModel? playlist = direction == 0 ? null : vm?.Playlist;
        (PlaybackPlaylistItem? current, PlaybackPlaylistItem? target) = AdjacentPlaylistItems(playlist, direction);
        commandButtonKeys[e.Key] = new()
        {
            Button = button, Command = button.Command, Flyout = button.Flyout, Parameter = button.CommandParameter,
            Playlist = playlist, Direction = direction, CurrentToken = current?.Token, TargetToken = target?.Token,
            Canceled = e.KeyModifiers != KeyModifiers.None || (!nativeToggle && button.Command == null && button.Flyout == null)
                || direction != 0 && (current == null || target == null),
        };
        button.LostFocus += OnParityCommandButtonLostFocus;
        vm?.SuppressInputKeyDown(KeyMapper.ToWpf(e.Key));
        // Skip playback routing and let the search toggle handle an unmodified press with its native button state.
        e.Handled = !nativeToggle;
        return true;
    }

    bool TryHandleCommandButtonKeyUp(KeyEventArgs e, bool fromOwnedChild = false)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return false;
        // A child owns its fresh input. Only a physical press already observed in this parent may be retired
        // by the child's tunnel; remembered parent focus must not swallow the child's native button release.
        Button? focused = fromOwnedChild ? null : FocusedParityCommandButton(e.Key);
        if (!commandButtonKeys.Remove(e.Key, out CommandButtonKeyPress? press))
        {
            if (fromOwnedChild || focused == null) return false;
            // Button's native Space release must never activate without this button's physical press.
            ConsumeParityCommandButtonRelease(e.Key);
            e.Handled = true;
            return true;
        }
        if (press.Button == null && press.SearchShortcutModifiers == null && focused == null && !press.Retired && !fromOwnedChild)
            return false;

        if (fromOwnedChild) press.Canceled = press.Retired = true;

        if (press.Button is { } owner) owner.LostFocus -= OnParityCommandButtonLostFocus;
        ConsumeParityCommandButtonRelease(e.Key);
        e.Handled = true;
        if (press.Canceled || press.Retired || press.Button == null
            || !ReferenceEquals(press.Button, focused) || !press.Button.IsEffectivelyEnabled
            || !press.Button.IsEffectivelyVisible) return true;

        if (press.Button is ToggleButton)
        {
            // Only a matching physical press may reach the ToggleButton's native Space release.
            e.Handled = false;
            return true;
        }

        if ((press.Command == null && press.Flyout == null) || e.KeyModifiers != KeyModifiers.None
            || !ReferenceEquals(press.Button.Command, press.Command)
            || !ReferenceEquals(press.Button.Flyout, press.Flyout)
            || !Equals(press.Button.CommandParameter, press.Parameter)) return true;

        if (press.Direction != 0)
        {
            if (!ReferenceEquals(press.Playlist, vm?.Playlist)) return true;
            (PlaybackPlaylistItem? current, PlaybackPlaylistItem? target) = AdjacentPlaylistItems(press.Playlist, press.Direction);
            if (current == null || target == null || !Equals(current.Token, press.CurrentToken)
                || !Equals(target.Token, press.TargetToken)) return true;
        }
        if (press.Command == null) press.Flyout!.ShowAt(press.Button);
        else if (press.Command.CanExecute(press.Parameter)) press.Command.Execute(press.Parameter);
        return true;
    }

    static (PlaybackPlaylistItem? Current, PlaybackPlaylistItem? Target) AdjacentPlaylistItems(PlaylistViewModel? playlist, int direction)
    {
        if (playlist == null || direction == 0) return (null, null);
        var rows = playlist.Items;
        for (int index = 0; index < rows.Count; index++)
        {
            if (!rows[index].IsCurrent) continue;
            int target = index + direction;
            return (rows[index], target >= 0 && target < rows.Count ? rows[target] : null);
        }
        return (null, null);
    }

    void ConsumeParityCommandButtonRelease(Key key)
    {
        searchKeysAwaitingRelease.Remove(key);
        if (cueSyncKeyboardPress?.Key == key) cueSyncKeyboardPress = null;
        vm?.HandleTrackedKeyUp(KeyMapper.ToWpf(key));
    }

    void OnParityCommandButtonLostFocus(object? sender, RoutedEventArgs e)
    {
        foreach (CommandButtonKeyPress press in commandButtonKeys.Values)
            if (ReferenceEquals(press.Button, sender)) press.Canceled = true;
    }

    void ClearCommandButtonKeys()
    {
        // Keep consume-only ownership until the actual up; activation cannot turn a held repeat into a new down.
        foreach (CommandButtonKeyPress press in commandButtonKeys.Values)
        {
            press.Canceled = press.Retired = true;
            if (press.Button is { } button) button.LostFocus -= OnParityCommandButtonLostFocus;
        }
    }
}
