using System.Text.Json;
using Avalonia.Headless.XUnit;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class SidebarSlotPrefsTests
{
    [Fact]
    public void Legacy_preferences_default_to_primary_and_secondary_roundtrips()
    {
        using TempDir dir = new();
        PrefsStore store = new(Path.Combine(dir.Path, "prefs.json"));
        File.WriteAllText(store.Path, "{\"Volume\":42}");
        AppPrefs legacy = store.Load();
        Assert.False(legacy.SidebarShowSecondary);
        Assert.Equal(42, legacy.Volume);
        legacy.SidebarShowSecondary = true;
        store.Save(legacy);
        AppPrefs restored = store.Load();
        Assert.True(restored.SidebarShowSecondary);
        Assert.Equal(42, restored.Volume);
    }

    [AvaloniaFact]
    public void Startup_restores_secondary_without_writing_and_selector_persists_only_its_value()
    {
        AppPrefs prefs = new() { SidebarShowSecondary = true, Volume = 42, SidebarSearchWholeWord = true };
        List<AppPrefs> saved = [];
        using MainWindowViewModel main = new(new FakePlaybackController(), prefs,
            candidate => saved.Add(JsonSerializer.Deserialize<AppPrefs>(JsonSerializer.Serialize(candidate))!),
            new FakeTranslator(), new FakeShell());
        Assert.Equal(1, main.Sidebar.SelectedSlot);
        Assert.True(main.Sidebar.WholeWord);
        Assert.Empty(saved);
        main.Sidebar.SelectedSlot = 0;
        Assert.False(Assert.Single(saved).SidebarShowSecondary);
        Assert.Equal(42, saved[0].Volume);
        Assert.True(saved[0].SidebarSearchWholeWord);
    }

    [AvaloniaFact]
    public void Settings_draft_save_preserves_later_toolbar_selection_and_fresh_unrelated_values()
    {
        AppPrefs prefs = new();
        AppPrefs? saved = null;
        using MainWindowViewModel main = new(new FakePlaybackController(), prefs,
            candidate => saved = JsonSerializer.Deserialize<AppPrefs>(JsonSerializer.Serialize(candidate))!,
            new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        main.Sidebar.SelectedSlot = 1;
        prefs.Volume = 42;
        prefs.RecentFiles.Add("new.mp4");
        draft.SidebarFontSizeText = "20";
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        Assert.True(saved!.SidebarShowSecondary);
        Assert.True(prefs.SidebarShowSecondary);
        Assert.Equal(1, main.Sidebar.SelectedSlot);
        Assert.Equal(42, saved.Volume);
        Assert.Equal(new[] { "new.mp4" }, saved.RecentFiles);
        Assert.Equal(20, main.SidebarFontSize);
    }

    [AvaloniaFact]
    public void Selector_write_failure_keeps_live_selection_and_reports_existing_save_error()
    {
        AppPrefs prefs = new();
        using MainWindowViewModel main = new(new FakePlaybackController(), prefs,
            _ => throw new IOException("Preferences unavailable"), new FakeTranslator(), new FakeShell());
        main.Sidebar.SelectedSlot = 1;
        Assert.Equal(1, main.Sidebar.SelectedSlot);
        Assert.True(prefs.SidebarShowSecondary);
        Assert.Contains(main.Toasts, toast => toast.Title == "Could not save preferences" && toast.Message == "Preferences unavailable");
    }
}
