using System.Text.Json;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class SettingsTests
{
    [Fact]
    public void Save_persists_all_six_fields_before_live_apply_and_preserves_fresh_unrelated_values()
    {
        var player = new FakePlaybackController(); var shell = new FakeShell(); var prefs = new AppPrefs();
        AppPrefs? saved = null; int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate =>
        {
            writes++; Assert.Equal("Dark", prefs.Theme); Assert.True(player.SubtitlesVisible);
            Assert.Empty(shell.Themes); Assert.NotSame(prefs, candidate); Assert.NotSame(prefs.RecentFiles, candidate.RecentFiles);
            saved = JsonSerializer.Deserialize<AppPrefs>(JsonSerializer.Serialize(candidate));
        }, new FakeTranslator(), shell);
        var draft = main.CreateSettings();
        draft.Theme = "Light"; draft.SubtitleFontSize = 42; draft.SecondaryFontScale = 1.2;
        draft.SubtitlesVisible = false; draft.SidebarVisible = true; draft.SidebarWidth = 460;
        Assert.Equal(34, main.SubtitleFontSize); Assert.False(main.SidebarVisible);
        prefs.Volume = 77; prefs.Mute = true; prefs.LastFolder = "/new"; prefs.RecentFiles.Add("new.mp4");
        prefs.WordTranslationService = "Off"; prefs.TranslateTargetLanguage = "Russian";
        int closes = 0; draft.CloseRequested += () => closes++;
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error); Assert.Equal(1, closes); Assert.Equal(1, writes);
        Assert.Equal("Light", saved!.Theme); Assert.Equal(42, saved.SubtitleFontSize); Assert.Equal(1.2, saved.SecondaryFontScale);
        Assert.False(saved.SubtitlesVisible); Assert.True(saved.SidebarVisible); Assert.Equal(460, saved.SidebarWidth);
        Assert.Equal(77, saved.Volume); Assert.True(saved.Mute); Assert.Equal("/new", saved.LastFolder);
        Assert.Equal(["new.mp4"], saved.RecentFiles); Assert.Equal("Off", saved.WordTranslationService); Assert.Equal("Russian", saved.TranslateTargetLanguage);
        Assert.False(player.SubtitlesVisible); Assert.True(main.SidebarVisible); Assert.Equal(460, main.SidebarWidth);
        Assert.Equal(42, main.SubtitleFontSize); Assert.Equal(50.4, main.SecondaryFontSize); Assert.Equal(["Light"], shell.Themes);
    }

    [Fact]
    public void Failure_keeps_entire_live_state_unchanged_and_draft_open_then_retry_succeeds()
    {
        var prefs = new AppPrefs(); var player = new FakePlaybackController(); var shell = new FakeShell(); bool fail = true;
        using var main = new MainWindowViewModel(player, prefs, _ => { if (fail) throw new IOException("disk full"); }, new FakeTranslator(), shell);
        string before = JsonSerializer.Serialize(prefs);
        var draft = main.CreateSettings(); draft.Theme = "Light"; draft.SubtitlesVisible = false;
        draft.SidebarVisible = true; draft.SidebarWidth = 500; draft.SubtitleFontSize = 60; draft.SecondaryFontScale = 1.5;
        int closes = 0; draft.CloseRequested += () => closes++;
        draft.SaveCommand.Execute(null);
        Assert.Contains("disk full", draft.Error!); Assert.Equal(0, closes); Assert.Equal(before, JsonSerializer.Serialize(prefs));
        Assert.True(main.IsDarkTheme); Assert.True(player.SubtitlesVisible); Assert.False(main.SidebarVisible);
        Assert.Equal(34, main.SubtitleFontSize); Assert.Equal(27.2, main.SecondaryFontSize); Assert.Empty(shell.Themes);
        fail = false; draft.SaveCommand.Execute(null); Assert.Null(draft.Error); Assert.Equal(1, closes);
    }

    [Fact]
    public void Scale_only_recomputes_secondary_font_and_Close_discards()
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); draft.SecondaryFontScale = 1.5; draft.SaveCommand.Execute(null);
        Assert.Equal(51, main.SecondaryFontSize); Assert.Single(saved);
        var cancel = main.CreateSettings(); cancel.Theme = "Light"; cancel.SubtitleFontSize = 90;
        cancel.CloseCommand.Execute(null);
        Assert.Equal("Dark", prefs.Theme); Assert.Equal(34, main.SubtitleFontSize); Assert.Single(saved);
    }

    [Theory]
    [InlineData(11, 0.8, 360)] [InlineData(97, 0.8, 360)]
    [InlineData(34, 0.39, 360)] [InlineData(34, 1.51, 360)]
    [InlineData(34, 0.8, 219)] [InlineData(34, 0.8, 901)]
    [InlineData(double.NaN, 0.8, 360)] [InlineData(34, double.PositiveInfinity, 360)]
    public void Out_of_range_drafts_do_not_persist(double font, double scale, double width)
    {
        var (main, _, _, _, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); draft.SubtitleFontSize = font; draft.SecondaryFontScale = scale; draft.SidebarWidth = width;
        draft.SaveCommand.Execute(null); Assert.NotNull(draft.Error); Assert.Empty(saved);
    }
    [Theory]
    [InlineData(12, 0.4, 220)] [InlineData(96, 1.5, 900)]
    public void Boundary_values_are_valid(double font, double scale, double width)
    {
        var (main, _, _, _, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); draft.SubtitleFontSize = font; draft.SecondaryFontScale = scale; draft.SidebarWidth = width;
        draft.SaveCommand.Execute(null); Assert.Null(draft.Error); Assert.Single(saved);
    }
}
