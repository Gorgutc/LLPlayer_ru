using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>The shared desktop sidebar selects its visible subtitle slot and synchronizes a cue without seeking.</summary>
public class SidebarSlotUiTests
{
    [AvaloniaFact]
    public void Slot_selector_changes_rendered_rows_and_reapplies_the_existing_filter()
    {
        using var fixture = new SidebarWindow();
        ComboBox? selector = fixture.Window.FindControl<ComboBox>("SidebarSlot");
        Assert.NotNull(selector);
        Assert.Equal(0, selector.SelectedIndex);
        Assert.Equal(new[] { "Primary", "Secondary" }, selector.Items.OfType<ComboBoxItem>().Select(item => item.Content));
        Assert.All(fixture.RowTexts(), text => Assert.Equal(0, text.Slot));

        TextBox filter = fixture.Window.FindControl<TextBox>("FilterBox")!;
        filter.Text = "Secondary";
        Pump(fixture.Window);
        Assert.Empty(fixture.Vm.Sidebar.Cues);

        selector.SelectedIndex = 1;
        Pump(fixture.Window);

        Assert.Equal("Secondary", filter.Text);
        Assert.Equal(new[] { "Secondary first", "Secondary second" }, fixture.Vm.Sidebar.Cues.Select(cue => cue.DisplayText));
        Assert.All(fixture.RowTexts(), text => Assert.Equal(1, text.Slot));
        Assert.Equal(2, fixture.RowTexts().Length);
        Assert.Empty(fixture.Player.Seeks);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Cue_sync_button_is_bound_to_its_row_and_click_does_not_seek_or_toggle_playback(int slot)
    {
        using var fixture = new SidebarWindow();
        fixture.Window.FindControl<ComboBox>("SidebarSlot")!.SelectedIndex = slot;
        Pump(fixture.Window);
        ListBox list = fixture.Window.FindControl<ListBox>("CueList")!;
        ListBoxItem row = list.GetRealizedContainers().OfType<ListBoxItem>().First();
        Button? sync = row.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => button.Name == "CueSyncButton");
        Assert.NotNull(sync);
        Assert.Equal("Adjust subtitle track delay to current time", AutomationProperties.GetName(sync));
        Assert.NotNull(sync.Command);
        Assert.Same(row.DataContext, sync.CommandParameter);

        Click(fixture.Window, sync);

        var synchronized = Assert.Single(fixture.Player.SynchronizedCues);
        Assert.Equal(slot, synchronized.Slot);
        Assert.Same(Assert.IsType<CueItemViewModel>(row.DataContext).Cue, synchronized.Cue);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.Equal(0, fixture.Player.PauseCalls);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Slot_selector_fits_the_minimum_sidebar_and_owns_navigation_keys(bool light)
    {
        using var fixture = new SidebarWindow(width: 640, sidebarWidth: AppPrefs.MinSidebarWidth, light: light);
        ComboBox? selector = fixture.Window.FindControl<ComboBox>("SidebarSlot");
        Assert.NotNull(selector);
        Border sidebar = fixture.Window.FindControl<Border>("Sidebar")!;
        Point origin = selector.TranslatePoint(default, sidebar)!.Value;
        Assert.True(selector.IsEffectivelyVisible && selector.Bounds.Width > 0 && selector.Bounds.Height > 0);
        Assert.True(origin.X >= 11 && origin.X + selector.Bounds.Width <= sidebar.Bounds.Width - 11);
        Assert.Equal("Sidebar subtitle track", AutomationProperties.GetName(selector));
        foreach (SubtitleText text in fixture.RowTexts())
        {
            Assert.True(text.Bounds.Width > 40, "The sync action must leave usable subtitle text space.");
            Button sync = text.GetVisualAncestors().OfType<Grid>().First().Children.OfType<Button>()
                .Single(button => button.Name == "CueSyncButton");
            Point syncOrigin = sync.TranslatePoint(default, sidebar)!.Value;
            Assert.True(sync.IsEffectivelyVisible && sync.Bounds.Width > 0 && sync.Bounds.Height > 0);
            Assert.True(syncOrigin.X >= 0 && syncOrigin.X + sync.Bounds.Width <= sidebar.Bounds.Width);
        }
        Assert.True(selector.Focus(NavigationMethod.Tab));

        fixture.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        fixture.Window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Pump(fixture.Window);

        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Secondary_row_word_click_pauses_and_looks_up_the_secondary_slot()
    {
        using var fixture = new SidebarWindow();
        fixture.Window.FindControl<ComboBox>("SidebarSlot")!.SelectedIndex = 1;
        Pump(fixture.Window);
        SubtitleText text = Assert.Single(fixture.RowTexts(), row => row.Text == "Secondary first");

        ClickWord(fixture.Window, text, "Secondary");

        Assert.Equal(1, fixture.Player.PauseCalls);
        Assert.Equal(("Secondary", 1), Assert.Single(fixture.Translator.Calls));
        Assert.True(fixture.Vm.WordPopup.IsOpen);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Empty(fixture.Player.SynchronizedCues);
    }

    [AvaloniaFact]
    public void Focused_sync_button_owns_space_without_dispatching_a_playback_shortcut()
    {
        using var fixture = new SidebarWindow();
        fixture.Window.FindControl<ComboBox>("SidebarSlot")!.SelectedIndex = 1;
        Pump(fixture.Window);
        Button sync = fixture.Window.GetVisualDescendants().OfType<Button>().First(button => button.Name == "CueSyncButton");
        Assert.True(sync.Focus(NavigationMethod.Tab));

        fixture.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.Same(sync, fixture.Window.FocusManager!.GetFocusedElement());
        Assert.Empty(fixture.Player.SynchronizedCues);
        Assert.Empty(fixture.Player.KeysDown);
        fixture.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Pump(fixture.Window);

        Assert.Equal(1, Assert.Single(fixture.Player.SynchronizedCues).Slot);
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.Equal(0, fixture.Player.PauseCalls);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sync_press_cannot_transfer_to_another_live_row_when_its_button_is_rebound(bool keyboard)
    {
        using var fixture = new SidebarWindow();
        CueItemViewModel pressedRow = fixture.Vm.Sidebar.Cues[0];
        CueItemViewModel replacement = fixture.Vm.Sidebar.Cues[1];
        Assert.True(fixture.Vm.Sidebar.IsCurrentRow(pressedRow));
        Assert.True(fixture.Vm.Sidebar.IsCurrentRow(replacement));
        Button sync = Assert.Single(fixture.Window.GetVisualDescendants().OfType<Button>(),
            button => button.Name == "CueSyncButton" && ReferenceEquals(button.CommandParameter, pressedRow));
        Point point = sync.TranslatePoint(new Point(sync.Bounds.Width / 2, sync.Bounds.Height / 2), fixture.Window)!.Value;
        if (keyboard)
        {
            Assert.True(sync.Focus(NavigationMethod.Tab));
            fixture.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        }
        else
        {
            fixture.Window.MouseMove(point);
            fixture.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(fixture.Player.SynchronizedCues);

        // A virtualized button can be reused while its pointer/key press is still in flight. The replacement
        // is deliberately another valid live row: release-time membership alone cannot protect this gesture.
        sync.DataContext = replacement;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(replacement, sync.CommandParameter);
        Assert.True(fixture.Vm.Sidebar.IsCurrentRow(replacement));
        if (keyboard)
            fixture.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        else
            fixture.Window.MouseUp(point, MouseButton.Left);
        Pump(fixture.Window);

        Assert.Empty(fixture.Player.SynchronizedCues);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.Equal(0, fixture.Player.PauseCalls);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Sync_press_is_rejected_after_selected_slot_or_cue_source_changes(bool keyboard, bool replaceSource)
    {
        using var fixture = new SidebarWindow();
        CueItemViewModel pressedRow = fixture.Vm.Sidebar.Cues[0];
        Button sync = FindSync(fixture, pressedRow);
        Point point = BeginSyncPress(fixture, sync, keyboard);
        if (replaceSource)
        {
            fixture.Player.ReplaceCueSource(0);
            fixture.Player.RaiseCuesChanged(0);
        }
        else
            fixture.Window.FindControl<ComboBox>("SidebarSlot")!.SelectedIndex = 1;
        Pump(fixture.Window);
        Assert.False(fixture.Vm.Sidebar.IsCurrentRow(pressedRow));
        CueItemViewModel replacement = fixture.Vm.Sidebar.Cues[0];
        Assert.True(fixture.Vm.Sidebar.IsCurrentRow(replacement));
        sync.DataContext = replacement;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(replacement, sync.CommandParameter);

        EndSyncPress(fixture, point, keyboard);

        Assert.Empty(fixture.Player.SynchronizedCues);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.Equal(0, fixture.Player.PauseCalls);
    }

    [AvaloniaFact]
    public void Sync_press_keeps_the_same_live_cue_when_only_translation_display_updates()
    {
        using var fixture = new SidebarWindow();
        CueItemViewModel row = fixture.Vm.Sidebar.Cues[0];
        Button sync = FindSync(fixture, row);
        Point point = BeginSyncPress(fixture, sync, keyboard: false);
        fixture.Player.Cues[0][0] = fixture.Player.Cues[0][0] with
        {
            Text = "Translated primary", IsTranslated = true, OriginalText = "Primary first"
        };
        fixture.Player.RaiseCuesChanged(0);
        Pump(fixture.Window);
        Assert.Same(row, fixture.Vm.Sidebar.Cues[0]);
        Assert.True(fixture.Vm.Sidebar.IsCurrentRow(row));

        EndSyncPress(fixture, point, keyboard: false);

        Assert.Same(row.Cue, Assert.Single(fixture.Player.SynchronizedCues).Cue);
        Assert.Empty(fixture.Player.Seeks);
    }

    [AvaloniaFact]
    public void Automation_can_invoke_sync_for_a_current_row_without_a_physical_press()
    {
        using var fixture = new SidebarWindow();
        CueItemViewModel row = fixture.Vm.Sidebar.Cues[0];
        Button sync = FindSync(fixture, row);
        IInvokeProvider invoke = Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(sync));

        invoke.Invoke();
        Pump(fixture.Window);

        Assert.Same(row.Cue, Assert.Single(fixture.Player.SynchronizedCues).Cue);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Canceled_sync_pointer_press_does_not_block_later_automation_for_a_rebound_live_row()
    {
        using var fixture = new SidebarWindow();
        CueItemViewModel pressedRow = fixture.Vm.Sidebar.Cues[0];
        CueItemViewModel replacement = fixture.Vm.Sidebar.Cues[1];
        Button sync = FindSync(fixture, pressedRow);
        BeginSyncPress(fixture, sync, keyboard: false);
        Point outside = new(2, 2);
        fixture.Window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
        fixture.Window.MouseUp(outside, MouseButton.Left);
        Pump(fixture.Window);
        Assert.Empty(fixture.Player.SynchronizedCues);

        sync.DataContext = replacement;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(replacement, sync.CommandParameter);
        Assert.True(fixture.Vm.Sidebar.IsCurrentRow(replacement));
        IInvokeProvider invoke = Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(sync));

        invoke.Invoke();
        Pump(fixture.Window);

        Assert.Same(replacement.Cue, Assert.Single(fixture.Player.SynchronizedCues).Cue);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.Equal(0, fixture.Player.PauseCalls);
    }

    [AvaloniaFact]
    public void Rejected_automation_during_a_held_sync_press_cannot_reassign_its_pointer_release()
    {
        using var fixture = new SidebarWindow();
        CueItemViewModel pressedRow = fixture.Vm.Sidebar.Cues[0];
        CueItemViewModel replacement = fixture.Vm.Sidebar.Cues[1];
        Button sync = FindSync(fixture, pressedRow);
        Point point = BeginSyncPress(fixture, sync, keyboard: false);
        sync.DataContext = replacement;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(replacement, sync.CommandParameter);
        Assert.True(fixture.Vm.Sidebar.IsCurrentRow(replacement));
        IInvokeProvider invoke = Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(sync));

        invoke.Invoke();
        Pump(fixture.Window);
        Assert.Empty(fixture.Player.SynchronizedCues);

        // Rejecting the unrelated activation must leave the still-held pointer's original owner in place.
        EndSyncPress(fixture, point, keyboard: false);

        Assert.Empty(fixture.Player.SynchronizedCues);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.Equal(0, fixture.Player.PauseCalls);
    }

    [AvaloniaFact]
    public void Secondary_mouse_button_during_a_held_sync_press_cannot_reassign_its_left_release()
    {
        using var fixture = new SidebarWindow();
        CueItemViewModel pressedRow = fixture.Vm.Sidebar.Cues[0];
        CueItemViewModel replacement = fixture.Vm.Sidebar.Cues[1];
        Button sync = FindSync(fixture, pressedRow);
        List<(PointerUpdateKind Kind, bool Left, bool Right)> presses = [];
        List<(PointerUpdateKind Kind, bool Left, bool Right)> moves = [];
        List<(PointerUpdateKind Kind, bool Left, bool Right)> releases = [];
        fixture.Window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            PointerPointProperties properties = e.GetCurrentPoint(fixture.Window).Properties;
            presses.Add((properties.PointerUpdateKind, properties.IsLeftButtonPressed, properties.IsRightButtonPressed));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        fixture.Window.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            PointerPointProperties properties = e.GetCurrentPoint(fixture.Window).Properties;
            moves.Add((properties.PointerUpdateKind, properties.IsLeftButtonPressed, properties.IsRightButtonPressed));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        fixture.Window.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            PointerPointProperties properties = e.GetCurrentPoint(fixture.Window).Properties;
            releases.Add((properties.PointerUpdateKind, properties.IsLeftButtonPressed, properties.IsRightButtonPressed));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Point point = BeginSyncPress(fixture, sync, keyboard: false);
        sync.DataContext = replacement;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(replacement, sync.CommandParameter);
        Assert.True(fixture.Vm.Sidebar.IsCurrentRow(replacement));

        fixture.Window.MouseDown(point, MouseButton.Right, RawInputModifiers.LeftMouseButton | RawInputModifiers.RightMouseButton);
        Dispatcher.UIThread.RunJobs();
        fixture.Window.MouseUp(point, MouseButton.Right, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        fixture.Window.MouseUp(point, MouseButton.Left);
        Pump(fixture.Window);

        // Avalonia MouseDevice routes additional down/intermediate up transitions through PointerMoved. Only
        // the first down and last up raise press/release, so this real chord must retain the original owner.
        Assert.Equal((PointerUpdateKind.LeftButtonPressed, true, false), Assert.Single(presses));
        Assert.Contains(moves, move => move.Kind == PointerUpdateKind.RightButtonPressed && move.Left && move.Right);
        Assert.Contains(moves, move => move.Kind == PointerUpdateKind.RightButtonReleased && move.Left && !move.Right);
        Assert.Equal((PointerUpdateKind.LeftButtonReleased, false, false), Assert.Single(releases));
        Assert.Empty(fixture.Player.SynchronizedCues);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.ToggleCalls);
        Assert.Equal(0, fixture.Player.PauseCalls);
    }

    [AvaloniaFact]
    public void Repeated_Enter_while_held_keeps_the_original_sync_owner_until_key_release()
    {
        using var fixture = new SidebarWindow();
        CueItemViewModel pressedRow = fixture.Vm.Sidebar.Cues[0];
        CueItemViewModel replacement = fixture.Vm.Sidebar.Cues[1];
        Button sync = FindSync(fixture, pressedRow);
        Assert.True(sync.Focus(NavigationMethod.Tab));
        fixture.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump(fixture.Window);
        Assert.Same(pressedRow.Cue, Assert.Single(fixture.Player.SynchronizedCues).Cue);

        sync.DataContext = replacement;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(replacement, sync.CommandParameter);
        fixture.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump(fixture.Window);
        Assert.Same(pressedRow.Cue, Assert.Single(fixture.Player.SynchronizedCues).Cue);
        fixture.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump(fixture.Window);

        // A fresh press after the real release is a new, valid action for the replacement row.
        fixture.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        fixture.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump(fixture.Window);
        Assert.Equal(2, fixture.Player.SynchronizedCues.Count);
        Assert.Same(replacement.Cue, fixture.Player.SynchronizedCues[1].Cue);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    [AvaloniaFact]
    public void Space_release_after_focus_transfer_to_sync_does_not_invoke_it()
    {
        using var fixture = new SidebarWindow();
        TextBox filter = fixture.Window.FindControl<TextBox>("FilterBox")!;
        Assert.True(filter.Focus(NavigationMethod.Tab));
        fixture.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Button sync = FindSync(fixture, fixture.Vm.Sidebar.Cues[0]);
        Assert.True(sync.Focus(NavigationMethod.Tab));

        fixture.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Pump(fixture.Window);

        Assert.Empty(fixture.Player.SynchronizedCues);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
        Assert.Equal(0, fixture.Player.ToggleCalls);
    }

    static Button FindSync(SidebarWindow fixture, CueItemViewModel row)
        => Assert.Single(fixture.Window.GetVisualDescendants().OfType<Button>(),
            button => button.Name == "CueSyncButton" && ReferenceEquals(button.CommandParameter, row));

    static Point BeginSyncPress(SidebarWindow fixture, Button sync, bool keyboard)
    {
        Point point = sync.TranslatePoint(new Point(sync.Bounds.Width / 2, sync.Bounds.Height / 2), fixture.Window)!.Value;
        if (keyboard)
        {
            Assert.True(sync.Focus(NavigationMethod.Tab));
            fixture.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        }
        else
        {
            fixture.Window.MouseMove(point);
            fixture.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(fixture.Player.SynchronizedCues);
        return point;
    }

    static void EndSyncPress(SidebarWindow fixture, Point point, bool keyboard)
    {
        if (keyboard)
            fixture.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        else
            fixture.Window.MouseUp(point, MouseButton.Left);
        Pump(fixture.Window);
    }

    [AvaloniaFact]
    public void Still_realized_word_row_from_replaced_source_cannot_pause_or_start_lookup()
    {
        using var fixture = new SidebarWindow();
        SubtitleText text = Assert.Single(fixture.RowTexts(), row => row.Text == "Primary first");
        // Simulate the source changing before its coalesced cue notification reaches the view. The old text is
        // still hit-testable, so both press and release must be rejected at the host's live-source guard.
        fixture.Player.ReplaceCueSource(0);

        ClickWord(fixture.Window, text, "Primary");

        Assert.Equal(0, fixture.Player.PauseCalls);
        Assert.True(fixture.Player.IsPlaying);
        Assert.Empty(fixture.Translator.Calls);
        Assert.False(fixture.Vm.WordPopup.IsOpen);
        Assert.Empty(fixture.Player.Seeks);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pending_word_release_is_rejected_after_slot_switch_or_filter_removes_its_row(bool switchSlot)
    {
        using var fixture = new SidebarWindow();
        SubtitleText oldText = Assert.Single(fixture.RowTexts(), row => row.Text == "Primary first");
        if (switchSlot)
            fixture.Window.FindControl<ComboBox>("SidebarSlot")!.SelectedIndex = 1;
        else
            fixture.Window.FindControl<TextBox>("FilterBox")!.Text = "second";
        Pump(fixture.Window);
        Assert.Equal("SidebarCueText", oldText.Name);
        Assert.True(oldText.DataContext is not CueItemViewModel current || !fixture.Vm.Sidebar.IsCurrentRow(current));

        oldText.RaiseEvent(new WordClickedEventArgs(SubtitleText.WordClickedEvent, "Primary", "Primary first", 0, default));
        Pump(fixture.Window);

        Assert.Empty(fixture.Translator.Calls);
        Assert.False(fixture.Vm.WordPopup.IsOpen);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.PauseCalls);
    }

    [AvaloniaFact]
    public void Sidebar_word_event_with_detached_row_context_cannot_fall_through_to_overlay_actions()
    {
        using var fixture = new SidebarWindow();
        SubtitleText text = Assert.Single(fixture.RowTexts(), row => row.Text == "Primary first");
        Assert.Equal("SidebarCueText", text.Name);
        text.DataContext = null;

        text.RaiseEvent(new WordClickedEventArgs(SubtitleText.WordClickedEvent, "Primary", "Primary first", 0, default));
        Pump(fixture.Window);

        Assert.Empty(fixture.Translator.Calls);
        Assert.False(fixture.Vm.WordPopup.IsOpen);
        Assert.Empty(fixture.Player.Seeks);
        Assert.Equal(0, fixture.Player.PauseCalls);
    }

    [AvaloniaFact]
    public void Recycled_control_cannot_finish_a_press_owned_by_the_previous_cue_source()
    {
        using var fixture = new SidebarWindow();
        SubtitleText text = Assert.Single(fixture.RowTexts(), row => row.Text == "Primary first");
        CueItemViewModel pressedRow = Assert.IsType<CueItemViewModel>(text.DataContext);
        Point point = WordPoint(fixture.Window, text, "Primary");
        fixture.Window.MouseMove(point);
        fixture.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, fixture.Player.PauseCalls);

        fixture.Player.ReplaceCueSource(0);
        fixture.Player.RaiseCuesChanged(0);
        Pump(fixture.Window);
        CueItemViewModel replacement = fixture.Vm.Sidebar.Cues[0];
        Assert.NotSame(pressedRow, replacement);
        // Reproduce a recycled control's new DataContext with unchanged visible text, then replay the queued
        // release. A current-row check alone would admit the old gesture as a lookup on the replacement source.
        text.DataContext = replacement;
        text.RaiseEvent(new WordClickedEventArgs(SubtitleText.WordClickedEvent, "Primary", "Primary first", 0, default));
        Pump(fixture.Window);

        Assert.Empty(fixture.Translator.Calls);
        Assert.False(fixture.Vm.WordPopup.IsOpen);
        Assert.Equal(1, fixture.Player.PauseCalls);
        Assert.Empty(fixture.Player.Seeks);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Timestamp_tap_keeps_the_normal_selected_slot_seek_path(int slot)
    {
        using var fixture = new SidebarWindow();
        fixture.Window.FindControl<ComboBox>("SidebarSlot")!.SelectedIndex = slot;
        Pump(fixture.Window);
        ListBox list = fixture.Window.FindControl<ListBox>("CueList")!;
        ListBoxItem row = list.GetRealizedContainers().OfType<ListBoxItem>().First();
        TextBlock time = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), block => block.Classes.Contains("mono"));

        Click(fixture.Window, time);

        var seek = Assert.Single(fixture.Player.SubtitleSeeks);
        Assert.Equal(slot, seek.Slot);
        Assert.Equal(Assert.IsType<CueItemViewModel>(row.DataContext).Start, seek.Position);
        Assert.Empty(fixture.Player.SynchronizedCues);
        Assert.Empty(fixture.Translator.Calls);
    }

    static void ClickWord(MainWindow window, SubtitleText text, string word)
    {
        Point target = WordPoint(window, text, word);
        window.MouseMove(target);
        window.MouseDown(target, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(target, MouseButton.Left);
        Pump(window);
    }

    static Point WordPoint(MainWindow window, SubtitleText text, string word)
    {
        Point? target = null;
        for (double y = 1; target == null && y < text.Bounds.Height - 1; y++)
            for (double x = 1; target == null && x < text.Bounds.Width - 1; x++)
            {
                Point local = new(x, y);
                if (!text.TryGetWordAt(local, out string found) || found != word)
                    continue;
                Point translated = text.TranslatePoint(local, window)!.Value;
                if (ReferenceEquals(window.InputHitTest(translated), text))
                    target = translated;
            }
        Assert.NotNull(target);
        return target.Value;
    }

    static void Click(MainWindow window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump(window);
    }

    static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
    }

    sealed class SidebarWindow : IDisposable
    {
        public MainWindow Window { get; }
        public MainWindowViewModel Vm { get; }
        public FakePlaybackController Player { get; } = new() { IsOpened = true, IsPlaying = true };
        public FakeTranslator Translator { get; } = new();

        public SidebarWindow(double width = 1100, double sidebarWidth = AppPrefs.DefaultSidebarWidth, bool light = false)
        {
            Application.Current!.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            Player.Cues[0].AddRange([
                new CueInfo(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Primary first"),
                new CueInfo(1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "Primary second")]);
            Player.Cues[1].AddRange([
                new CueInfo(0, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "Secondary first"),
                new CueInfo(1, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(8), "Secondary second")]);
            Window = new MainWindow(new KeyMapper(), null) { Width = width, Height = 700 };
            Vm = new MainWindowViewModel(Player,
                new AppPrefs { SidebarVisible = true, SidebarWidth = sidebarWidth, Theme = light ? "Light" : "Dark" }.Normalize(), _ => { }, Translator, Window);
            Window.Attach(Vm, renderer: null);
            Player.RaiseCuesChanged(0);
            Player.RaiseCuesChanged(1);
            Window.Show();
            Pump(Window);
        }

        public SubtitleText[] RowTexts() => Window.GetVisualDescendants().OfType<SubtitleText>()
            .Where(text => text.DataContext is CueItemViewModel).ToArray();

        public void Dispose()
        {
            if (Window.IsVisible)
                Window.Close();
            Vm.Dispose();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}
