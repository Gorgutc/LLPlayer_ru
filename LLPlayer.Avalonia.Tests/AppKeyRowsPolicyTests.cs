using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public class AppKeyRowsPolicyTests
{
    [Fact]
    public void Null_invalid_only_and_empty_have_distinct_effective_ownership()
    {
        Dictionary<string, string> legacy = new() { [AppShortcutMap.ToggleSidebar] = "Ctrl+Shift+B" };
        AppShortcutMap absent = new(new Dictionary<string, Action>(), legacy);
        Assert.NotNull(absent.Find(WpfKey.B, true, false, true));
        Assert.Equal(2, absent.Shortcuts.Count(row => row.ActionName == AppShortcutMap.OpenWindowCheatSheet));
        AppShortcut[] legacyRows = [.. absent.Shortcuts.Where(row => row.ActionName is not "OpenCommandPalette" and not "ToggleDebugOverlay")];
        Assert.Equal(new[]
        {
            "SubsPositionUp", "SubsPositionDown", "SubsSizeIncrease", "SubsSizeDecrease", "SubsPrimaryTextCopy",
            "ActivateSubsSearch", "ToggleSidebar", "OpenWindowCheatSheet", "OpenWindowCheatSheet", "OpenSubtitles", "ToggleTheme",
        }, legacyRows.Select(row => row.ActionName));
        Assert.All(legacyRows, row => { Assert.False(row.IsKeyUp); Assert.True(row.IsEnabled); });
        Assert.Equal(new[] { "OpenCommandPalette", "ToggleDebugOverlay" }, absent.Shortcuts.Where(row => row.IsKeyUp).Select(row => row.ActionName));
        AppShortcut palette = Assert.Single(absent.Shortcuts, row => row.ActionName == "OpenCommandPalette");
        AppShortcut debug = Assert.Single(absent.Shortcuts, row => row.ActionName == "ToggleDebugOverlay");
        Assert.Equal((WpfKey.K, true, false, false, true, true), (palette.Key, palette.Ctrl, palette.Alt, palette.Shift, palette.IsKeyUp, palette.IsEnabled));
        Assert.Equal((WpfKey.D, true, false, true, true, true), (debug.Key, debug.Ctrl, debug.Alt, debug.Shift, debug.IsKeyUp, debug.IsEnabled));
        Assert.Same(palette, absent.Find(WpfKey.K, true, false, false));
        Assert.Same(debug, absent.Find(WpfKey.D, true, false, true));

        AppShortcutMap malformed = new(new Dictionary<string, Action>(), legacy,
            [new() { ActionName = "RunScript", Key = "F6" }]);
        Assert.NotNull(malformed.Find(WpfKey.B, true, false, true));
        AppShortcutMap empty = new(new Dictionary<string, Action>(), legacy, []);
        Assert.Empty(empty.Shortcuts);
        AppShortcutMap disabled = new(new Dictionary<string, Action>(), legacy,
            [new() { ActionName = "ToggleSidebar", Key = "F6", IsEnabled = false }]);
        Assert.Single(disabled.Shortcuts);
        Assert.Null(disabled.Find(WpfKey.B, true, false, true));
        Assert.Null(disabled.Find(WpfKey.F6, false, false, false));
    }

    [Theory]
    [InlineData("RunScript", "F6")]
    [InlineData("ToggleSidebar", "45")]
    [InlineData("ToggleSidebar", "A,B")]
    [InlineData("ToggleSidebar", "None")]
    [InlineData("ToggleSidebar", "LeftCtrl")]
    [InlineData("ToggleSidebar", "f6")]
    [InlineData("OpenWindowDownloader", "F6")]
    public void Invalid_names_cannot_create_explicit_executable_rows(string action, string key)
        => Assert.Null(AppKeyBindings.NormalizePrefs([new() { ActionName = action, Key = key }]));

    [Fact]
    public void Normalization_keeps_order_duplicates_flags_and_detaches_mutable_rows()
    {
        List<AppKeyBindingPref> input =
        [
            new() { ActionName = "ToggleSidebar", Key = "F7", Ctrl = true, IsKeyUp = true },
            new() { ActionName = "Unknown", Key = "F8" },
            new() { ActionName = "ToggleSidebar", Key = "F7", IsEnabled = false },
        ];
        List<AppKeyBindingPref> result = AppKeyBindings.NormalizePrefs(input)!;
        Assert.Equal(2, result.Count);
        Assert.True(result[0].Ctrl);
        Assert.True(result[0].IsKeyUp);
        Assert.False(result[1].IsEnabled);
        result[0].Key = "F6";
        Assert.Equal("F7", input[0].Key);
        Assert.Equal("F7", result[1].Key);
    }

    [Fact]
    public void Duplicate_phase_differences_are_rejected_and_disabled_rows_do_not_reserve_chords()
    {
        List<AppKeyBindingPref> rows =
        [
            new() { ActionName = "ToggleSidebar", Key = "F6" },
            new() { ActionName = "ToggleTheme", Key = "F6", IsKeyUp = true },
        ];
        Assert.False(new AppShortcutMap(new Dictionary<string, Action>(), explicitRows: rows).TryValidateEffective(out _));
        rows[1].IsEnabled = false;
        AppShortcutMap valid = new(new Dictionary<string, Action>(), explicitRows: rows);
        Assert.True(valid.TryValidateEffective(out _));
        Assert.Single(valid.BuildCheatSheet([]));
        Assert.False(EngineKeyBindings.TryValidate([new(KeyBindingAction.ToggleMute, WpfKey.F6, IsKeyUp: true)], valid.Shortcuts, out _));
        rows[0].IsEnabled = false;
        Assert.True(EngineKeyBindings.TryValidate([new(KeyBindingAction.ToggleMute, WpfKey.F6)],
            new AppShortcutMap(new Dictionary<string, Action>(), explicitRows: rows).Shortcuts, out _));
    }

    [Fact]
    public void Catalog_exposes_unbound_AB_and_implemented_windows_without_adding_defaults()
    {
        Assert.True(ShortcutActionCatalog.TryGetEngine("ABLoopSetStart", out ShortcutActionDescriptor start));
        Assert.Equal(KeyBindingAction.ABLoopSetStart, start.EngineAction);
        Assert.DoesNotContain(EngineKeyBindings.Defaults(), row => row.Action == KeyBindingAction.ABLoopSetStart);
        Assert.False(ShortcutActionCatalog.TryGetEngine("Custom", out _));
        Assert.True(ShortcutActionCatalog.TryGetApp("OpenWindowSettings", out _));
        Assert.True(ShortcutActionCatalog.TryGetApp("OpenWindowWordManager", out _));
        Assert.True(ShortcutActionCatalog.TryGetApp("OpenWindowSubsExporter", out _));
        Assert.DoesNotContain(AppShortcutMap.Defaults, row => row.ActionName == "OpenWindowSettings");
    }

    [Fact]
    public void Release_dispatch_owns_repeat_wrong_up_and_modifier_first_release()
    {
        int calls = 0;
        AppShortcutMap map = Map(() => calls++, release: true, ctrl: true);
        AppShortcutDispatcher dispatch = new();
        Assert.True(dispatch.HandleKeyDown(map, WpfKey.F6, true, false, false));
        Assert.True(dispatch.HandleKeyDown(map, WpfKey.F6, true, false, false));
        Assert.Equal(0, calls);
        Assert.False(dispatch.HandleKeyUp(WpfKey.LeftCtrl));
        Assert.False(dispatch.HandleKeyUp(WpfKey.F7));
        Assert.Equal(0, calls);
        Assert.True(dispatch.HandleKeyUp(WpfKey.F6));
        Assert.Equal(1, calls);
        Assert.False(dispatch.HandleKeyUp(WpfKey.F6));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Pending_release_uses_handler_snapshot_and_cancellation_never_resolves_new_map()
    {
        int oldCalls = 0, newCalls = 0;
        AppShortcutMap first = Map(() => oldCalls++, release: true);
        AppShortcutMap second = Map(() => newCalls++, release: true);
        AppShortcutDispatcher dispatch = new();
        dispatch.HandleKeyDown(first, WpfKey.F6, false, false, false);
        dispatch.HandleKeyDown(second, WpfKey.F6, false, false, false);
        dispatch.HandleKeyUp(WpfKey.F6);
        Assert.Equal(1, oldCalls);
        Assert.Equal(0, newCalls);
        dispatch.HandleKeyDown(first, WpfKey.F6, false, false, false);
        dispatch.CancelPending();
        Assert.True(dispatch.HandleKeyDown(second, WpfKey.F6, false, false, false));
        Assert.True(dispatch.HandleKeyUp(WpfKey.F6));
        Assert.Equal(1, oldCalls);
        Assert.Equal(0, newCalls);
        dispatch.HandleKeyDown(second, WpfKey.F6, false, false, false);
        dispatch.HandleKeyUp(WpfKey.F6);
        Assert.Equal(1, newCalls);
    }

    [Fact]
    public void Immediate_dispatch_retains_eligible_repeat_and_owns_its_release()
    {
        int calls = 0;
        AppShortcutMap map = Map(() => calls++);
        AppShortcutDispatcher dispatch = new();
        dispatch.HandleKeyDown(map, WpfKey.F6, false, false, false);
        dispatch.HandleKeyDown(map, WpfKey.F6, false, false, false);
        Assert.Equal(2, calls);
        dispatch.HandleKeyDown(map, WpfKey.F6, true, false, false);
        Assert.Equal(2, calls);
        Assert.True(dispatch.HandleKeyUp(WpfKey.F6));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Another_trigger_after_monotonic_guard_cancels_lost_release_without_sleeping()
    {
        int calls = 0;
        MonotonicClock time = new();
        AppShortcutMap map = Map(() => calls++, release: true);
        AppShortcutDispatcher dispatch = new(time);
        dispatch.HandleKeyDown(map, WpfKey.F6, false, false, false);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.False(dispatch.HandleKeyDown(map, WpfKey.F7, false, false, false));
        Assert.True(dispatch.HandleKeyUp(WpfKey.F6));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Immediate_repeat_survives_an_unrelated_trigger_after_the_release_guard_interval()
    {
        int calls = 0;
        MonotonicClock time = new();
        AppShortcutMap map = Map(() => calls++);
        AppShortcutDispatcher dispatch = new(time);
        Assert.True(dispatch.HandleKeyDown(map, WpfKey.F6, false, false, false));
        Assert.Equal(1, calls);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.False(dispatch.HandleKeyDown(map, WpfKey.F7, false, false, false));
        Assert.True(dispatch.HandleKeyDown(map, WpfKey.F6, false, false, false));
        Assert.Equal(2, calls);
        Assert.True(dispatch.HandleKeyUp(WpfKey.F6));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Long_held_release_row_keeps_same_key_repeats_armed_until_its_matching_up()
    {
        int calls = 0;
        MonotonicClock time = new();
        AppShortcutMap map = Map(() => calls++, release: true);
        AppShortcutDispatcher dispatch = new(time);
        Assert.True(dispatch.HandleKeyDown(map, WpfKey.F6, false, false, false));
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.True(dispatch.HandleKeyDown(map, WpfKey.F6, false, false, false));
        Assert.Equal(0, calls);
        Assert.True(dispatch.HandleKeyUp(WpfKey.F6));
        Assert.Equal(1, calls);
        Assert.False(dispatch.HandleKeyUp(WpfKey.F6));
        Assert.Equal(1, calls);
    }

    static AppShortcutMap Map(Action action, bool release = false, bool ctrl = false) => new(
        new Dictionary<string, Action> { ["ToggleSidebar"] = action }, explicitRows:
        [new() { ActionName = "ToggleSidebar", Key = "F6", Ctrl = ctrl, IsKeyUp = release }]);

    sealed class MonotonicClock : TimeProvider
    {
        long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }
}
