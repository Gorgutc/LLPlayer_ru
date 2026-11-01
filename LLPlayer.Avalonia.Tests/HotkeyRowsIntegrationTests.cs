using System.Text.Json;
using Avalonia;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.Core.Services;
using FlyleafLib.MediaPlayer;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public class HotkeyRowsIntegrationTests
{
    [AvaloniaFact]
    public void Saving_a_raw_legacy_profile_with_null_Word_menu_normalizes_before_live_application()
    {
        AppPrefs prefs = new();
        Assert.Null(prefs.WordMenuActions);
        using MainWindowViewModel main = new(new FakePlaybackController(), prefs, _ => { }, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        draft.AddAppKeyCommand.Execute(null);
        draft.ShortcutRows[^1].Chord = "F6";
        Exception? failure = Record.Exception(() => draft.SaveCommand.Execute(null));
        Assert.Null(failure);
        Assert.Null(draft.Error);
        Assert.NotNull(prefs.WordMenuActions);
        Assert.Equal("Copy", prefs.WordMenuActions[0].Title);
        Assert.NotNull(main.Shortcuts.Find(WpfKey.F6, false, false, false));
    }

    [AvaloniaFact]
    public void Legacy_unrelated_save_keeps_null_rows_and_continuous_size_position_repeats()
    {
        var (main, _, _, prefs, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        Assert.Null(Assert.Single(saved).AppKeyBindings);
        Assert.Null(prefs.AppKeyBindings);
        double size = main.SubtitleFontSize;
        double offset = main.SubtitleBottomOffset;
        for (int i = 0; i < 3; i++) main.HandleKeyDown(WpfKey.Right, false, false, true, false);
        Assert.Equal(size + 6, main.SubtitleFontSize);
        main.HandleKeyUp(WpfKey.Right, true);
        Assert.Equal(size + 6, main.SubtitleFontSize);
        for (int i = 0; i < 3; i++) main.HandleKeyDown(WpfKey.Up, false, false, true, false);
        Assert.Equal(offset + 24, main.SubtitleBottomOffset);
        main.HandleKeyUp(WpfKey.Up, false);
        Assert.Equal(offset + 24, main.SubtitleBottomOffset);
    }

    [AvaloniaFact]
    public void Explicit_release_size_action_executes_once_after_three_downs_and_focus_change()
    {
        AppPrefs prefs = new() { AppKeyBindings = [new() { ActionName = "SubsSizeIncrease", Key = "F6", IsKeyUp = true }] };
        var (main, player, _, _, _) = TestVm.Create(prefs: prefs.Normalize());
        using var owned = main;
        double size = main.SubtitleFontSize;
        for (int i = 0; i < 3; i++) main.HandleKeyDown(WpfKey.F6, false, false, false, false);
        Assert.Equal(size, main.SubtitleFontSize);
        Assert.True(main.HandleKeyUp(WpfKey.F6, true));
        Assert.Equal(size + 2, main.SubtitleFontSize);
        main.HandleKeyUp(WpfKey.F6, true);
        Assert.Equal(size + 2, main.SubtitleFontSize);
        Assert.Empty(player.KeysDown);
        Assert.Empty(player.KeysUp);
    }

    [AvaloniaFact]
    public void HotkeyOnlySave_preserves_fresh_concurrent_Word_and_Anki_values()
    {
        var (main, _, _, prefs, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        draft.ShortcutRows.First(row => row.ActionName == "ToggleSidebar").Chord = "F6";
        prefs.WordCopyOnSelected = true;
        prefs.WordLastSearchOnSelected = true;
        prefs.WordLastSearchModifier = WordSearchModifier.Alt;
        prefs.SubsAutoTextCopy = true;
        prefs.SubsAutoTextCopyTarget = SubtitleAutoCopyTarget.All;
        prefs.WordMenuActions = [new() { Kind = WordMenuActionKind.Clipboard, Title = "Concurrent copy" }];
        prefs.AnkiDeck = "Concurrent deck";
        prefs.WordClickAction = "ClipboardAll";
        prefs.Volume = 37;
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        AppPrefs persisted = Assert.Single(saved);
        Assert.True(persisted.WordCopyOnSelected);
        Assert.True(persisted.WordLastSearchOnSelected);
        Assert.Equal(WordSearchModifier.Alt, persisted.WordLastSearchModifier);
        Assert.True(persisted.SubsAutoTextCopy);
        Assert.Equal(SubtitleAutoCopyTarget.All, persisted.SubsAutoTextCopyTarget);
        Assert.Equal("Concurrent copy", Assert.Single(persisted.WordMenuActions!).Title);
        Assert.Equal("Concurrent deck", persisted.AnkiDeck);
        Assert.Equal("ClipboardAll", persisted.WordClickAction);
        Assert.Equal(37, persisted.Volume);
    }

    [AvaloniaFact]
    public void Explicit_Word_Anki_edits_commit_while_unchanged_fields_keep_fresh_values()
    {
        var (main, _, _, prefs, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        draft.WordCopyOnSelected = true;
        draft.AnkiDeck = "Edited deck";
        draft.WordMenuActions[0].Title = "Edited copy";
        prefs.WordLastSearchOnSelected = true;
        prefs.AnkiDeck = "Concurrent deck";
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        AppPrefs persisted = Assert.Single(saved);
        Assert.True(persisted.WordCopyOnSelected);
        Assert.True(persisted.WordLastSearchOnSelected);
        Assert.Equal("Edited deck", persisted.AnkiDeck);
        Assert.Equal("Edited copy", persisted.WordMenuActions![0].Title);
    }

    [AvaloniaFact]
    public void Explicit_Word_reset_to_initial_defaults_overrides_fresh_menu_only_after_Save()
    {
        var (main, _, _, prefs, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        prefs.WordMenuActions = [new() { Kind = WordMenuActionKind.Clipboard, Title = "Concurrent menu" }];
        prefs.AnkiDeck = "Concurrent deck";
        prefs.WordCopyOnSelected = true;
        draft.ResetWordActionsCommand.Execute(null);
        Assert.Empty(saved);
        Assert.Equal("Concurrent menu", Assert.Single(prefs.WordMenuActions).Title);
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        AppPrefs persisted = Assert.Single(saved);
        Assert.Equal("Copy", persisted.WordMenuActions![0].Title);
        Assert.Equal("Concurrent deck", persisted.AnkiDeck);
        Assert.True(persisted.WordCopyOnSelected);
    }

    [AvaloniaFact]
    public void Failed_save_keeps_pending_release_and_retry_replaces_the_effective_map()
    {
        bool fail = true;
        List<AppPrefs> saved = [];
        AppPrefs prefs = new() { AppKeyBindings = [new() { ActionName = "ToggleSidebar", Key = "F6", IsKeyUp = true }] };
        FakePlaybackController player = new();
        using MainWindowViewModel main = new(player, prefs.Normalize(), candidate =>
        {
            if (fail) throw new IOException("Synthetic write refusal");
            saved.Add(candidate);
        }, new FakeTranslator(), new FakeShell());
        main.HandleKeyDown(WpfKey.F6, false, false, false, false);
        SettingsViewModel draft = main.CreateSettings();
        draft.ShortcutRows[0].Chord = "F7";
        draft.SaveCommand.Execute(null);
        Assert.NotNull(draft.Error);
        Assert.Empty(saved);
        Assert.NotNull(main.Shortcuts.Find(WpfKey.F6, false, false, false));
        main.HandleKeyUp(WpfKey.F6, false);
        Assert.True(main.SidebarVisible);
        fail = false;
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        Assert.Single(saved);
        Assert.Null(main.Shortcuts.Find(WpfKey.F6, false, false, false));
        Assert.NotNull(main.Shortcuts.Find(WpfKey.F7, false, false, false));
    }

    [AvaloniaFact]
    public void Explicit_rows_roundtrip_flags_order_and_detached_clones_through_disk()
    {
        using TempDir temp = new();
        PrefsStore store = new(Path.Combine(temp.Path, "prefs.json"));
        AppPrefs prefs = new() { AppKeyBindings =
        [
            new() { ActionName = "ToggleSidebar", Key = "F7", IsEnabled = false, IsKeyUp = true },
            new() { ActionName = "ToggleSidebar", Key = "F6", Ctrl = true },
        ] };
        store.Save(prefs);
        AppPrefs loaded = store.Load();
        Assert.Equal(new[] { "F7", "F6" }, loaded.AppKeyBindings!.Select(row => row.Key));
        Assert.False(loaded.AppKeyBindings![0].IsEnabled);
        Assert.True(loaded.AppKeyBindings![0].IsKeyUp);
        Assert.True(loaded.AppKeyBindings![1].Ctrl);
        loaded.AppKeyBindings![0].Key = "F8";
        Assert.Equal("F7", prefs.AppKeyBindings[0].Key);
        Assert.Equal("F7", store.Load().AppKeyBindings![0].Key);
        store.Save(new() { AppKeyBindings = [] });
        Assert.Empty(store.Load().AppKeyBindings!);
        store.Save(new());
        Assert.DoesNotContain("AppKeyBindings", File.ReadAllText(store.Path));
        Assert.Null(store.Load().AppKeyBindings);
    }

    [AvaloniaFact]
    public void Startup_reports_duplicate_explicit_app_chords_even_across_release_phase()
    {
        AppPrefs prefs = new() { AppKeyBindings =
        [new() { ActionName = "ToggleSidebar", Key = "F6" }, new() { ActionName = "ToggleTheme", Key = "F6", IsKeyUp = true }] };
        var (main, _, _, _, _) = TestVm.Create(prefs: prefs.Normalize());
        using var owned = main;
        Assert.Contains(main.Toasts, toast => toast.Title == "Key binding conflict");
    }

    [AvaloniaFact]
    public void Window_routes_engine_up_after_text_focus_and_suppresses_input_origin_up_after_focus_leaves()
    {
        using WindowFixture fixture = new(new AppPrefs { SidebarVisible = true });
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
        fixture.Filter.Focus();
        fixture.Window.KeyRelease(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
        Assert.Contains(WpfKey.F8, fixture.Player.KeysDown);
        Assert.Contains(WpfKey.F8, fixture.Player.KeysUp);
        fixture.Player.KeysDown.Clear(); fixture.Player.KeysUp.Clear();
        fixture.Window.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null);
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyRelease(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null);
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
    }

    [AvaloniaFact]
    public void F1_opens_owned_sheet_owns_child_release_and_next_fresh_press_after_close()
    {
        using WindowFixture fixture = new(new AppPrefs());
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        Dispatcher.UIThread.RunJobs();
        CheatSheetWindow sheet = Assert.IsType<CheatSheetWindow>(Assert.Single(fixture.Window.OwnedWindows));
        sheet.KeyRelease(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        sheet.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        fixture.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<CheatSheetWindow>(Assert.Single(fixture.Window.OwnedWindows));
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
    }

    [AvaloniaTheory]
    [InlineData("OpenWindowSettings", typeof(SettingsWindow))]
    [InlineData("OpenWindowWordManager", typeof(WordManagerWindow))]
    [InlineData("OpenWindowSubsExporter", typeof(SubtitlesExportWindow))]
    public void Added_portable_window_actions_use_real_handlers_and_keep_next_press_after_child_close(string action, Type windowType)
    {
        using WindowFixture fixture = new(new AppPrefs { AppKeyBindings = [new() { ActionName = action, Key = "F6" }] });
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        Dispatcher.UIThread.RunJobs();
        Window first = Assert.Single(fixture.Window.OwnedWindows);
        Assert.Equal(windowType, first.GetType());
        first.KeyRelease(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        first.Close();
        Dispatcher.UIThread.RunJobs();
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        Dispatcher.UIThread.RunJobs();
        Window second = Assert.Single(fixture.Window.OwnedWindows);
        Assert.Equal(windowType, second.GetType());
        Assert.NotSame(first, second);
        Assert.Empty(fixture.Player.KeysDown);
        Assert.Empty(fixture.Player.KeysUp);
    }

    [AvaloniaFact]
    public void Opening_owned_surface_cancels_armed_release_and_consumes_its_late_child_up()
    {
        using WindowFixture fixture = new(new AppPrefs { AppKeyBindings = [new() { ActionName = "ToggleSidebar", Key = "F6", IsKeyUp = true }] });
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        fixture.Vm.ShowCheatSheet();
        Dispatcher.UIThread.RunJobs();
        Window child = Assert.Single(fixture.Window.OwnedWindows);
        child.KeyRelease(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        Assert.False(fixture.Vm.SidebarVisible);
        child.Close();
        Dispatcher.UIThread.RunJobs();
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        fixture.Window.KeyRelease(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        Assert.True(fixture.Vm.SidebarVisible);
        Assert.Empty(fixture.Player.KeysUp);
    }

    [AvaloniaFact]
    public void Capture_consumes_the_paired_release_after_success_and_escape_cancel()
    {
        ChordCaptureButton capture = new() { Chord = "F6" };
        Window window = new() { Content = capture, Width = 300, Height = 100 };
        int unhandledUps = 0;
        window.AddHandler(InputElement.KeyUpEvent, (_, _) => unhandledUps++);
        window.Show();
        try
        {
            capture.BeginCapture();
            window.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null);
            window.KeyRelease(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null);
            Assert.Equal("F7", capture.Chord);
            Assert.False(capture.IsCapturing);
            Assert.Equal(0, unhandledUps);
            capture.BeginCapture();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(capture.IsCapturing);
            Assert.Equal("F7", capture.Chord);
            Assert.Equal(0, unhandledUps);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void External_context_loss_does_not_swallow_the_first_fresh_app_press(bool onRelease)
    {
        using WindowFixture fixture = new(new AppPrefs
        {
            AppKeyBindings = [new() { ActionName = "SubsSizeIncrease", Key = "F6", IsKeyUp = onRelease }],
        });
        double initialSize = fixture.Vm.SubtitleFontSize;
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        double sizeBeforeLoss = initialSize + (onRelease ? 0 : 2);
        Assert.Equal(sizeBeforeLoss, fixture.Vm.SubtitleFontSize);
        Window otherApplication = new() { Width = 200, Height = 100 };
        try
        {
            otherApplication.Show();
            otherApplication.Activate();
            DeliverActivation(fixture.Window, false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(fixture.Window.IsActive);
            otherApplication.KeyRelease(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
            otherApplication.Close();
            fixture.Window.Activate();
            DeliverActivation(fixture.Window, true);
            fixture.Window.VideoSurface.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Window.IsActive);
            fixture.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
            fixture.Window.KeyRelease(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
            Assert.Equal(sizeBeforeLoss + 2, fixture.Vm.SubtitleFontSize);
            Assert.Empty(fixture.Player.KeysDown);
            Assert.Empty(fixture.Player.KeysUp);
        }
        finally { otherApplication.Close(); }
    }

    [AvaloniaFact]
    public void External_context_loss_clears_input_origin_suppression_for_the_first_fresh_native_press()
    {
        using WindowFixture fixture = new(new AppPrefs { SidebarVisible = true });
        fixture.Filter.Focus();
        fixture.Window.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
        Assert.Empty(fixture.Player.KeysDown);
        Window otherApplication = new() { Width = 200, Height = 100 };
        try
        {
            otherApplication.Show();
            otherApplication.Activate();
            DeliverActivation(fixture.Window, false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(fixture.Window.IsActive);
            otherApplication.KeyRelease(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
            otherApplication.Close();
            fixture.Window.Activate();
            DeliverActivation(fixture.Window, true);
            fixture.Window.VideoSurface.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Window.IsActive);
            fixture.Window.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
            fixture.Window.KeyRelease(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
            Assert.Equal(new[] { WpfKey.F8 }, fixture.Player.KeysDown);
            Assert.Equal(new[] { WpfKey.F8 }, fixture.Player.KeysUp);
        }
        finally { otherApplication.Close(); }
    }

    [AvaloniaFact]
    public void External_context_loss_keeps_unpaired_old_app_up_quiet_and_genuine_engine_up_owned()
    {
        using WindowFixture fixture = new(new AppPrefs { AppKeyBindings = [new() { ActionName = "ToggleSidebar", Key = "F6", IsKeyUp = true }] });
        fixture.Window.VideoSurface.Focus();
        fixture.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        fixture.Window.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
        DeliverActivation(fixture.Window, false);
        DeliverActivation(fixture.Window, true);
        fixture.Window.KeyRelease(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        Assert.False(fixture.Vm.SidebarVisible);
        Assert.Empty(fixture.Player.KeysUp);
        fixture.Window.KeyRelease(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
        Assert.Equal(new[] { WpfKey.F8 }, fixture.Player.KeysUp);
    }

    static void DeliverActivation(Window window, bool active)
    {
        // The headless backend does not arbitrate activation between top-level windows. Deliver the same
        // platform callbacks as the native backend, exercising IsActive and the actual Window events.
        // Avalonia 12.1.3 exposes these interface members internally; reflection stays confined to this test seam.
        PropertyInfo? property = typeof(global::Avalonia.Platform.IWindowBaseImpl).GetProperty(active ? "Activated" : "Deactivated",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        Action callback = Assert.IsType<Action>(property.GetValue(window.PlatformImpl));
        callback();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(active, window.IsActive);
    }

    sealed class WindowFixture : IDisposable
    {
        readonly TempDir temp = new();
        public MainWindow Window { get; }
        public MainWindowViewModel Vm { get; }
        public FakePlaybackController Player { get; } = new();
        public TextBox Filter => Window.FindControl<TextBox>("FilterBox")!;
        public WindowFixture(AppPrefs prefs)
        {
            Window = new(new KeyMapper(), null) { Width = 1000, Height = 650 };
            Vm = new(Player, prefs.Normalize(), _ => { }, new FakeTranslator(), Window)
            {
                WordLibrary = new WordLibrary(Path.Combine(temp.Path, "words.json")),
                SubtitleExportSource = new ExportSource(),
            };
            Window.Attach(Vm, null);
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }
        public void Dispose()
        {
            foreach (Window child in Window.OwnedWindows.ToArray()) child.Close();
            Window.Close(); Vm.Dispose(); temp.Dispose();
        }
    }
    sealed class ExportSource : ISubtitleExportSource
    {
        public SubtitleExportSnapshot Capture(int slot, SubtitleExportText text)
            => new([new SubtitleExportLine(TimeSpan.Zero, TimeSpan.FromSeconds(1), "fixture")], "fixture", null, false);
    }
}
