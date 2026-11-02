using Avalonia.Headless.XUnit;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public class PlayerUiIdleTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Classic_idle_changes_only_fullscreen_playing_controls_and_cursor(bool fullscreen, bool playing)
    {
        var time = new ManualTimeProvider(); var (main, player, _, _, _) = TestVm.Create(time); using var owned = main;
        player.IsOpened = true; player.IsPlaying = playing; main.OnFullScreenChanged(fullscreen);
        time.Advance(TimeSpan.FromMilliseconds(2499)); main.UpdateIdle(); Assert.True(main.ControlsVisible); Assert.False(main.CursorHidden);
        time.Advance(TimeSpan.FromMilliseconds(1)); main.UpdateIdle();
        Assert.Equal(!(fullscreen && playing), main.ControlsVisible); Assert.Equal(fullscreen && playing, main.CursorHidden);
        main.NotifyActivity(); Assert.True(main.ControlsVisible); Assert.False(main.CursorHidden);
    }

    [AvaloniaFact]
    public void Zero_timeout_means_immediate_idle_under_the_existing_fullscreen_playing_conditions()
    {
        var (main, player, _, _, _) = TestVm.Create(prefs: new AppPrefs { ActivityTimeoutMs = 0 }.Normalize()); using var owned = main;
        player.IsOpened = player.IsPlaying = true; main.OnFullScreenChanged(true);
        Assert.False(main.ControlsVisible); Assert.True(main.CursorHidden);
        main.BeginSeekDrag(); Assert.True(main.ControlsVisible); Assert.False(main.CursorHidden);
        main.EndSeekDrag(); main.UpdateIdle(); Assert.False(main.ControlsVisible); Assert.True(main.CursorHidden);
        player.IsPlaying = false; main.UpdateIdle(); Assert.True(main.ControlsVisible); Assert.False(main.CursorHidden);
    }

    [AvaloniaFact]
    public void Seeking_and_a_word_popup_keep_classic_transport_accessible_after_timeout()
    {
        var time = new ManualTimeProvider(); var (main, player, _, _, _) = TestVm.Create(time); using var owned = main;
        player.IsOpened = player.IsPlaying = true; player.Duration = TimeSpan.FromMinutes(1).Ticks; main.OnFullScreenChanged(true);
        main.BeginSeekDrag(); time.Advance(TimeSpan.FromMinutes(1)); main.UpdateIdle();
        Assert.True(main.ControlsVisible); Assert.False(main.CursorHidden);
        main.EndSeekDrag(); time.Advance(TimeSpan.FromMinutes(1)); main.UpdateIdle(); Assert.False(main.ControlsVisible);
        main.OnWordClicked("Hello", "Hello world", 0, 100, 100);
        Assert.True(main.WordPopup.IsOpen); Assert.True(main.ControlsVisible); Assert.False(main.CursorHidden);
        main.WordPopup.Close();
        // Word selection owns its existing pause; resume explicitly to test the playing idle boundary again.
        player.Play(); time.Advance(TimeSpan.FromMinutes(1)); main.UpdateIdle(); Assert.False(main.ControlsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hover_only_starts_hidden_and_pointer_focus_or_popup_each_preserve_transport_access(bool fullscreen)
    {
        var time = new ManualTimeProvider();
        var (main, player, _, _, _) = TestVm.Create(time, prefs: new AppPrefs { SeekBarShowOnlyMouseOver = true }.Normalize()); using var owned = main;
        player.IsOpened = player.IsPlaying = true; main.OnFullScreenChanged(fullscreen);
        Assert.False(main.ControlsVisible);
        time.Advance(TimeSpan.FromMinutes(1)); main.UpdateIdle();
        main.SetTransportPointerOver(true); Assert.True(main.ControlsVisible); Assert.False(main.CursorHidden);
        main.SetTransportFocusWithin(true); main.SetTransportPointerOver(false); Assert.True(main.ControlsVisible);
        main.SetTransportPopupOpen(true); main.SetTransportFocusWithin(false); Assert.True(main.ControlsVisible);
        time.Advance(TimeSpan.FromMinutes(1)); main.UpdateIdle(); Assert.True(main.ControlsVisible);
        main.SetTransportPopupOpen(false); Assert.False(main.ControlsVisible); Assert.Equal(fullscreen, main.CursorHidden);
    }

    [AvaloniaFact]
    public void Hover_only_ignores_unrelated_pointer_activity_but_keyboard_reveals_for_configured_timeout()
    {
        var time = new ManualTimeProvider();
        var (main, player, _, _, _) = TestVm.Create(time, prefs: new AppPrefs { SeekBarShowOnlyMouseOver = true, ActivityTimeoutMs = 4000 }.Normalize()); using var owned = main;
        player.IsOpened = player.IsPlaying = true; main.OnFullScreenChanged(true);
        main.NotifyActivity(); Assert.False(main.ControlsVisible);
        main.NotifyKeyboardActivity(); Assert.True(main.ControlsVisible); Assert.False(main.CursorHidden);
        time.Advance(TimeSpan.FromMilliseconds(3999)); main.UpdateIdle(); Assert.True(main.ControlsVisible);
        time.Advance(TimeSpan.FromMilliseconds(1)); main.UpdateIdle(); Assert.False(main.ControlsVisible); Assert.True(main.CursorHidden);
        main.NotifyKeyboardActivity(); Assert.True(main.ControlsVisible); Assert.False(main.CursorHidden);
    }

    [AvaloniaFact]
    public void Hover_only_seek_capture_and_word_popup_keep_controls_visible_without_pointer_hover()
    {
        var time = new ManualTimeProvider();
        var (main, player, _, _, _) = TestVm.Create(time, prefs: new AppPrefs { SeekBarShowOnlyMouseOver = true }.Normalize()); using var owned = main;
        player.IsOpened = player.IsPlaying = true; player.Duration = TimeSpan.FromMinutes(1).Ticks; main.OnFullScreenChanged(true);
        Assert.False(main.ControlsVisible);
        main.BeginSeekDrag(); time.Advance(TimeSpan.FromMinutes(1)); main.UpdateIdle(); Assert.True(main.ControlsVisible);
        main.EndSeekDrag(); main.UpdateIdle(); Assert.False(main.ControlsVisible);
        main.OnWordClicked("Hello", "Hello world", 0, 100, 100); Assert.True(main.WordPopup.IsOpen); Assert.True(main.ControlsVisible);
        main.WordPopup.Close(); main.UpdateIdle(); Assert.False(main.ControlsVisible);
    }

    [AvaloniaFact]
    public void Successful_save_recomputes_current_hover_policy_and_idle_timeout_immediately()
    {
        var time = new ManualTimeProvider(); var (main, player, _, _, saved) = TestVm.Create(time); using var owned = main;
        player.IsOpened = player.IsPlaying = true; main.OnFullScreenChanged(true);
        time.Advance(TimeSpan.FromMilliseconds(3000)); main.UpdateIdle(); Assert.False(main.ControlsVisible);
        var draft = main.CreateSettings(); draft.ActivityTimeoutMsText = "5000"; draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error); Assert.Single(saved); Assert.True(main.ControlsVisible);
        var hover = main.CreateSettings(); hover.SeekBarShowOnlyMouseOver = true; hover.SaveCommand.Execute(null);
        Assert.Null(hover.Error); Assert.Equal(2, saved.Count); Assert.False(main.ControlsVisible);
        main.SetTransportFocusWithin(true); Assert.True(main.ControlsVisible);
        var classic = main.CreateSettings(); classic.SeekBarShowOnlyMouseOver = false; classic.ActivityTimeoutMsText = "0"; classic.SaveCommand.Execute(null);
        Assert.Null(classic.Error); Assert.Equal(3, saved.Count); Assert.False(main.ControlsVisible); Assert.True(main.CursorHidden);
    }
}
