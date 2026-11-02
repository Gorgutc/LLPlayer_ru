using ICommand = System.Windows.Input.ICommand;
using Avalonia.Controls;
using Avalonia.Input;
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
        public object? Parameter { get; init; }
        public PlaylistViewModel? Playlist { get; init; }
        public object? CurrentToken { get; init; }
        public object? TargetToken { get; init; }
        public int Direction { get; init; }
        public bool Canceled { get; set; }
        public bool Retired { get; set; }
    }

    // Observe physical downs even outside these three buttons, so a held video key cannot become a button
    // press merely because focus moved before a repeat or release. Other controls retain their existing route.
    readonly Dictionary<Key, CommandButtonKeyPress> commandButtonKeys = [];

    Button? FocusedParityCommandButton()
    {
        if (FocusManager?.GetFocusedElement() is not Button button) return null;
        return ReferenceEquals(button, MediaTitleButton) || ReferenceEquals(button, PreviousPlaylistButton)
            || ReferenceEquals(button, NextPlaylistButton) ? button : null;
    }

    bool TryHandleCommandButtonKeyDown(KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return false;
        Button? button = FocusedParityCommandButton();
        if (commandButtonKeys.TryGetValue(e.Key, out CommandButtonKeyPress? held))
        {
            if (held.Button == null && !held.Retired && button == null) return false;
            e.Handled = true;
            return true;
        }
        if (button == null)
        {
            commandButtonKeys[e.Key] = new();
            return false;
        }

        int direction = ReferenceEquals(button, PreviousPlaylistButton) ? -1
            : ReferenceEquals(button, NextPlaylistButton) ? 1 : 0;
        PlaylistViewModel? playlist = direction == 0 ? null : vm?.Playlist;
        (PlaybackPlaylistItem? current, PlaybackPlaylistItem? target) = AdjacentPlaylistItems(playlist, direction);
        commandButtonKeys[e.Key] = new()
        {
            Button = button, Command = button.Command, Parameter = button.CommandParameter,
            Playlist = playlist, Direction = direction, CurrentToken = current?.Token, TargetToken = target?.Token,
            Canceled = e.KeyModifiers != KeyModifiers.None || button.Command == null
                || direction != 0 && (current == null || target == null),
        };
        button.LostFocus += OnParityCommandButtonLostFocus;
        vm?.SuppressInputKeyDown(KeyMapper.ToWpf(e.Key));
        e.Handled = true;
        return true;
    }

    bool TryHandleCommandButtonKeyUp(KeyEventArgs e, bool fromOwnedChild = false)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return false;
        // A child owns its fresh input. Only a physical press already observed in this parent may be retired
        // by the child's tunnel; remembered parent focus must not swallow the child's native button release.
        Button? focused = fromOwnedChild ? null : FocusedParityCommandButton();
        if (!commandButtonKeys.Remove(e.Key, out CommandButtonKeyPress? press))
        {
            if (fromOwnedChild || focused == null) return false;
            // Button's native Space release must never activate without this button's physical press.
            ConsumeParityCommandButtonRelease(e.Key);
            e.Handled = true;
            return true;
        }
        if (press.Button == null && focused == null && !press.Retired && !fromOwnedChild) return false;

        if (fromOwnedChild) press.Canceled = press.Retired = true;

        if (press.Button is { } owner) owner.LostFocus -= OnParityCommandButtonLostFocus;
        ConsumeParityCommandButtonRelease(e.Key);
        e.Handled = true;
        if (press.Canceled || press.Retired || press.Button == null || press.Command == null
            || !ReferenceEquals(press.Button, focused) || !press.Button.IsEffectivelyEnabled
            || !press.Button.IsEffectivelyVisible || e.KeyModifiers != KeyModifiers.None
            || !ReferenceEquals(press.Button.Command, press.Command)
            || !Equals(press.Button.CommandParameter, press.Parameter)) return true;

        if (press.Direction != 0)
        {
            if (!ReferenceEquals(press.Playlist, vm?.Playlist)) return true;
            (PlaybackPlaylistItem? current, PlaybackPlaylistItem? target) = AdjacentPlaylistItems(press.Playlist, press.Direction);
            if (current == null || target == null || !Equals(current.Token, press.CurrentToken)
                || !Equals(target.Token, press.TargetToken)) return true;
        }
        if (press.Command.CanExecute(press.Parameter)) press.Command.Execute(press.Parameter);
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
