using Avalonia.Headless.XUnit;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class SearchExportHostTests
{
    [Fact]
    public void Search_flags_roundtrip_and_legacy_preferences_default_off()
    {
        using TempDir dir = new();
        PrefsStore store = new(Path.Combine(dir.Path, "prefs.json"));
        store.Save(new AppPrefs { SidebarSearchMatchCase = true, SidebarSearchWholeWord = true, SidebarSearchRegex = true });
        AppPrefs loaded = store.Load();
        Assert.True(loaded.SidebarSearchMatchCase);
        Assert.True(loaded.SidebarSearchWholeWord);
        Assert.True(loaded.SidebarSearchRegex);
        File.WriteAllText(store.Path, "{\"Volume\":42}");
        AppPrefs legacy = store.Load();
        Assert.False(legacy.SidebarSearchMatchCase);
        Assert.False(legacy.SidebarSearchWholeWord);
        Assert.False(legacy.SidebarSearchRegex);
        Assert.Equal(42, legacy.Volume);
    }

    [AvaloniaFact]
    public void Startup_restores_search_flags_without_saving_or_restoring_transient_query()
    {
        AppPrefs prefs = new() { SidebarSearchMatchCase = true, SidebarSearchWholeWord = true, SidebarSearchRegex = true };
        int saves = 0;
        using MainWindowViewModel main = new(new FakePlaybackController(), prefs, _ => saves++, new FakeTranslator(), new FakeShell());
        Assert.True(main.Sidebar.MatchCase);
        Assert.True(main.Sidebar.WholeWord);
        Assert.True(main.Sidebar.UseRegex);
        Assert.Empty(main.Sidebar.FilterText);
        Assert.Equal(0, saves);
    }

    [AvaloniaFact]
    public void Search_changes_persist_each_flag_and_settings_save_merges_current_search_and_volume()
    {
        using TempDir dir = new();
        PrefsStore store = new(Path.Combine(dir.Path, "prefs.json"));
        AppPrefs prefs = new() { RecentFiles = ["/media/keep.mkv"], LastFolder = "/media", SidebarWidth = 420 };
        FakePlaybackController player = new();
        int saves = 0;
        using MainWindowViewModel main = new(player, prefs, p => { store.Save(p); saves++; }, new FakeTranslator(), new FakeShell());
        SettingsViewModel draft = main.CreateSettings();
        main.Sidebar.MatchCase = true;
        Assert.True(store.Load().SidebarSearchMatchCase);
        Assert.Equal(1, saves);
        main.Sidebar.WholeWord = true;
        Assert.True(store.Load().SidebarSearchWholeWord);
        Assert.Equal(2, saves);
        main.Sidebar.UseRegex = true;
        Assert.True(store.Load().SidebarSearchRegex);
        Assert.Equal(3, saves);
        main.Sidebar.FilterText = "transient query";
        main.Sidebar.MatchCase = true;
        Assert.Equal(3, saves);
        main.Volume = 47;
        draft.Theme = "Light";
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error);
        AppPrefs persisted = store.Load();
        Assert.True(persisted.SidebarSearchMatchCase);
        Assert.True(persisted.SidebarSearchWholeWord);
        Assert.True(persisted.SidebarSearchRegex);
        Assert.Equal(47, persisted.Volume);
        Assert.Equal("Light", persisted.Theme);
        Assert.Equal(420, persisted.SidebarWidth);
        Assert.Equal("/media", persisted.LastFolder);
        Assert.Equal(new[] { "/media/keep.mkv" }, persisted.RecentFiles);
        using MainWindowViewModel reloaded = new(new FakePlaybackController(), persisted, _ => { }, new FakeTranslator(), new FakeShell());
        Assert.True(reloaded.Sidebar.MatchCase);
        Assert.True(reloaded.Sidebar.WholeWord);
        Assert.True(reloaded.Sidebar.UseRegex);
        Assert.Empty(reloaded.Sidebar.FilterText);
    }

    [AvaloniaFact]
    public void Disposed_host_does_not_keep_saving_sidebar_changes()
    {
        int saves = 0;
        MainWindowViewModel main = new(new FakePlaybackController(), new AppPrefs(), _ => saves++, new FakeTranslator(), new FakeShell());
        main.Sidebar.MatchCase = true;
        Assert.Equal(1, saves);
        main.Dispose();
        main.Sidebar.WholeWord = true;
        Assert.Equal(1, saves);
    }

    [AvaloniaFact]
    public void Export_without_source_is_a_safe_noop()
    {
        ExportShell shell = new();
        using MainWindowViewModel main = new(new FakePlaybackController(), new AppPrefs(), _ => { }, new FakeTranslator(), shell);
        main.ShowSubtitlesExportCommand.Execute(null);
        Assert.Null(main.SubtitlesExport);
        Assert.Empty(shell.Shown);
    }

    [AvaloniaFact]
    public async Task Export_command_reuses_session_and_closed_callback_only_clears_its_own_model()
    {
        ExportShell shell = new();
        using MainWindowViewModel main = new(new FakePlaybackController(), new AppPrefs(), _ => { }, new FakeTranslator(), shell)
        { SubtitleExportSource = new ExportSource() };
        main.ShowSubtitlesExportCommand.Execute(null);
        SubtitlesExportViewModel first = Assert.IsType<SubtitlesExportViewModel>(main.SubtitlesExport);
        first.SelectedSlot = 1;
        first.SelectedText = SubtitleExportText.TranslatedWithFallback;
        main.ShowSubtitlesExportCommand.Execute(null);
        Assert.Same(first, main.SubtitlesExport);
        Assert.Equal(2, shell.Shown.Count);
        Assert.All(shell.Shown, model => Assert.Same(first, model));
        Assert.Equal(1, main.SubtitlesExport!.SelectedSlot);
        await first.DisposeAsync();
        main.SubtitlesExportClosed(first);
        Assert.Null(main.SubtitlesExport);
        main.ShowSubtitlesExportCommand.Execute(null);
        SubtitlesExportViewModel second = Assert.IsType<SubtitlesExportViewModel>(main.SubtitlesExport);
        Assert.NotSame(first, second);
        Assert.Equal(0, second.SelectedSlot);
        Assert.Equal(SubtitleExportText.Original, second.SelectedText);
        main.SubtitlesExportClosed(first);
        Assert.Same(second, main.SubtitlesExport);
        await main.DrainSubtitlesExportAsync();
    }

    [AvaloniaFact]
    public async Task Shutdown_drain_waits_for_pending_picker_and_cancels_late_output()
    {
        using TempDir dir = new();
        string output = Path.Combine(dir.Path, "late.srt");
        ExportShell shell = new();
        using MainWindowViewModel main = new(new FakePlaybackController(), new AppPrefs(), _ => { }, new FakeTranslator(), shell)
        { SubtitleExportSource = new ExportSource() };
        main.ShowSubtitlesExportCommand.Execute(null);
        SubtitlesExportViewModel model = Assert.IsType<SubtitlesExportViewModel>(main.SubtitlesExport);
        TaskCompletionSource<string?> picker = new();
        bool pickerCalled = false;
        model.PickExportPath = _ => { pickerCalled = true; return picker.Task; };
        Task export = model.ExportAsync();
        Assert.True(pickerCalled);
        Task drain = main.DrainSubtitlesExportAsync();
        Assert.False(drain.IsCompleted);
        picker.SetResult(output);
        await drain;
        await export;
        Assert.False(File.Exists(output));
        Assert.False(model.Busy);
        await model.ExportAsync();
        Assert.False(File.Exists(output));
    }

    sealed class ExportSource : ISubtitleExportSource
    {
        public SubtitleExportSnapshot Capture(int slot, SubtitleExportText text)
            => new([new SubtitleExportLine(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello")], "fixture", null, false);
    }

    sealed class ExportShell : IAppShell
    {
        public List<SubtitlesExportViewModel> Shown { get; } = [];
        public bool IsFullScreen { get; set; }
        public Task<string?> PickMediaFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public Task<string?> PickSubtitlesFileAsync(string? initialFolder) => Task.FromResult<string?>(null);
        public void ShowSubtitlesExport(SubtitlesExportViewModel model) => Shown.Add(model);
        public void ShowCheatSheet() { }
        public void SetClipboardText(string text) { }
        public void ApplyTheme(string theme) { }
        public void Exit() { }
    }
}