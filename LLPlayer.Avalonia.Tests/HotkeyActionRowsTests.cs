using System.Reflection;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Behavioral entry points for the additive action-row editor and portable app-row preferences.</summary>
public class HotkeyActionRowsTests
{
    [AvaloniaFact]
    public void Editor_adds_an_unbound_engine_action_and_saves_its_selected_action()
    {
        var (main, _, _, _, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        draft.SelectedSection = 2;
        var window = new SettingsWindow(draft);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            int originalCount = draft.EngineKeyRows.Count;
            Button? add = window.FindControl<Button>("AddEngineKeyButton");
            Assert.NotNull(add);
            add.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Point addPoint = add.TranslatePoint(new Point(12, 12), window)!.Value;
            window.MouseDown(addPoint, MouseButton.Left);
            window.MouseUp(addPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(originalCount + 1, draft.EngineKeyRows.Count);

            EngineKeyEditRow added = draft.EngineKeyRows[^1];
            Reassign(added, KeyBindingAction.ABLoopSetStart);
            added.Chord = "F6";
            draft.SaveCommand.Execute(null);

            Assert.Null(draft.Error);
            AppPrefs persisted = Assert.Single(saved);
            Assert.Contains(persisted.EngineKeyBindings, row => row.Action == "ABLoopSetStart" && row.Key == "F6");
            Assert.Contains(main.CreateSettings().EngineKeyRows,
                row => row.Action == KeyBindingAction.ABLoopSetStart && row.Chord == "F6");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Reassigning_an_existing_engine_row_retires_its_previous_action()
    {
        var (main, _, _, _, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        EngineKeyEditRow row = draft.EngineKeyRows.First(item => item.Action == KeyBindingAction.ToggleFullScreen);
        Reassign(row, KeyBindingAction.ABLoopSetStart);
        row.Chord = "F6";
        draft.SaveCommand.Execute(null);

        Assert.Null(draft.Error);
        AppPrefs persisted = Assert.Single(saved);
        Assert.Contains(persisted.EngineKeyBindings, item => item.Action == "ABLoopSetStart" && item.Key == "F6");
        Assert.DoesNotContain(persisted.EngineKeyBindings, item => item.Action == "ToggleFullScreen");
    }

    [AvaloniaFact]
    public void Cloned_engine_row_is_independent_and_enabled_duplicate_blocks_save()
    {
        var (main, _, _, _, saved) = TestVm.Create();
        using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        EngineKeyEditRow original = draft.EngineKeyRows.First(item => item.Action == KeyBindingAction.ToggleFullScreen);
        bool originalRelease = original.IsKeyUp;
        int originalCount = draft.EngineKeyRows.Count;
        PropertyInfo? cloneProperty = original.GetType().GetProperty("CloneCommand");
        Assert.NotNull(cloneProperty);
        ICommand cloneCommand = Assert.IsAssignableFrom<ICommand>(cloneProperty.GetValue(original));
        cloneCommand.Execute(null);
        Assert.Equal(originalCount + 1, draft.EngineKeyRows.Count);
        EngineKeyEditRow clone = Assert.Single(draft.EngineKeyRows, item => !ReferenceEquals(item, original)
            && item.Action == original.Action && item.Chord == original.Chord);

        draft.SaveCommand.Execute(null);
        Assert.NotNull(draft.Error);
        Assert.Empty(saved);

        clone.Chord = "F6";
        clone.IsKeyUp = !originalRelease;
        clone.IsEnabled = false;
        Reassign(clone, KeyBindingAction.ABLoopSetStart);
        Assert.Equal(KeyBindingAction.ToggleFullScreen, original.Action);
        Assert.Equal("F", original.Chord);
        Assert.True(original.IsEnabled);
        Assert.Equal(originalRelease, original.IsKeyUp);
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        Assert.Single(saved);
    }

    [AvaloniaFact]
    public void Explicit_release_row_waits_for_matching_up_and_consumes_repeats()
    {
        AppPrefs prefs = ReadPrefs("""
            { "AppKeyBindings": [
                { "ActionName": "ToggleSidebar", "Key": "F6", "Ctrl": true, "IsKeyUp": true }
            ] }
            """);
        using var main = CreateMain(prefs);
        Assert.False(main.SidebarVisible);
        Assert.True(main.HandleKeyDown(WpfKey.F6, true, false, false, false));
        Assert.True(main.HandleKeyDown(WpfKey.F6, true, false, false, false));
        Assert.False(main.SidebarVisible);
        main.HandleKeyUp(WpfKey.F7, false);
        Assert.False(main.SidebarVisible);
        Assert.True(main.HandleKeyUp(WpfKey.F6, true));
        Assert.True(main.SidebarVisible);
        main.HandleKeyUp(WpfKey.F6, false);
        Assert.True(main.SidebarVisible);
    }

    [AvaloniaFact]
    public void Explicit_rows_allow_multiple_chords_for_the_same_action()
    {
        AppPrefs prefs = ReadPrefs("""
            { "AppKeyBindings": [
                { "ActionName": "ToggleSidebar", "Key": "F6" },
                { "ActionName": "ToggleSidebar", "Key": "F7" }
            ] }
            """);
        using var main = CreateMain(prefs);
        main.HandleKeyDown(WpfKey.F6, false, false, false, false);
        Assert.True(main.SidebarVisible);
        main.HandleKeyUp(WpfKey.F6, false);
        main.HandleKeyDown(WpfKey.F7, false, false, false, false);
        Assert.False(main.SidebarVisible);
    }

    [AvaloniaFact]
    public void Disabled_explicit_row_does_not_resurrect_legacy_default_chords()
    {
        AppPrefs prefs = ReadPrefs("""
            { "AppKeyBindings": [
                { "ActionName": "ToggleSidebar", "Key": "F6", "IsEnabled": false }
            ] }
            """);
        using var main = CreateMain(prefs);
        main.HandleKeyDown(WpfKey.F6, false, false, false, false);
        Assert.False(main.SidebarVisible);
        main.HandleKeyDown(WpfKey.B, true, false, false, false);
        Assert.False(main.SidebarVisible);
    }

    [AvaloniaFact]
    public void Explicit_empty_rows_remove_app_bindings_instead_of_restoring_defaults()
    {
        using var main = CreateMain(ReadPrefs("{ \"AppKeyBindings\": [] }"));
        main.HandleKeyDown(WpfKey.B, true, false, false, false);
        Assert.False(main.SidebarVisible);
        Assert.Empty(main.Shortcuts.Shortcuts);
    }

    static void Reassign(EngineKeyEditRow row, KeyBindingAction action)
    {
        // Reflect the existing row API so the pre-feature test compiles and fails on missing editing behavior.
        PropertyInfo property = typeof(EngineKeyEditRow).GetProperty(nameof(EngineKeyEditRow.Action))!;
        Assert.True(property.SetMethod?.IsPublic == true, "the engine editor must support action reassignment");
        property.SetValue(row, action);
    }

    static AppPrefs ReadPrefs(string json) => JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();

    static MainWindowViewModel CreateMain(AppPrefs prefs)
        => new(new FakePlaybackController(), prefs, _ => { }, new FakeTranslator(), new FakeShell());
}
