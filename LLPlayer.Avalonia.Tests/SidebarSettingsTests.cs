using System.Text.Json;
using AwesomeAssertions;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Sidebar settings: persisted schema, detached draft validation and save-before-live ordering.</summary>
public class SidebarSettingsTests
{
    [Fact]
    public void Prefs_sidebar_appearance_defaults_match_the_approved_wpf_values()
    {
        AppPrefs prefs = new AppPrefs().Normalize();
        prefs.SidebarLeft.Should().BeFalse();
        prefs.SidebarTextMask.Should().BeFalse();
        prefs.SidebarShowOriginalText.Should().BeFalse();
        prefs.SidebarSubPadding.Should().Be(5);
        prefs.SidebarFontFamily.Should().Be("Segoe UI");
        prefs.SidebarFontSize.Should().Be(16);
        prefs.SidebarFontWeight.Should().Be("Normal");
        AppPrefs.MinSidebarSubPadding.Should().Be(0);
        AppPrefs.MaxSidebarSubPadding.Should().Be(48);
        AppPrefs.MinSidebarFontSize.Should().Be(8);
        AppPrefs.MaxSidebarFontSize.Should().Be(72);
    }

    [Fact]
    public void Normalize_clamps_and_validates_new_sidebar_values_without_touching_existing_prefs()
    {
        AppPrefs prefs = new()
        {
            Volume = 42, SidebarVisible = true, SidebarWidth = 420,
            SidebarSubPadding = 500, SidebarFontSize = 500, SidebarFontFamily = "Segoe UI/../../evil",
            SidebarFontWeight = "Blackened",
        };
        prefs.Normalize();
        prefs.SidebarSubPadding.Should().Be(AppPrefs.MaxSidebarSubPadding);
        prefs.SidebarFontSize.Should().Be(AppPrefs.MaxSidebarFontSize);
        prefs.SidebarFontFamily.Should().Be(AppPrefs.DefaultSidebarFontFamily);
        prefs.SidebarFontWeight.Should().Be(AppPrefs.DefaultSidebarFontWeight);
        prefs.Volume.Should().Be(42);
        prefs.SidebarVisible.Should().BeTrue();
        prefs.SidebarWidth.Should().Be(420);

        AppPrefs low = new() { SidebarSubPadding = -3, SidebarFontSize = 1 };
        low.Normalize();
        low.SidebarSubPadding.Should().Be(AppPrefs.MinSidebarSubPadding);
        low.SidebarFontSize.Should().Be(AppPrefs.MinSidebarFontSize);

        AppPrefs notFinite = new() { SidebarFontSize = double.NaN };
        notFinite.Normalize();
        notFinite.SidebarFontSize.Should().Be(AppPrefs.DefaultSidebarFontSize);
    }

    [Fact]
    public void Prefs_store_roundtrips_new_sidebar_fields_and_absent_keys_keep_defaults()
    {
        using TempDir dir = new();
        PrefsStore store = new(Path.Combine(dir.Path, "prefs.json"));
        AppPrefs prefs = new()
        {
            SidebarLeft = true, SidebarTextMask = true, SidebarShowOriginalText = true,
            SidebarSubPadding = 12, SidebarFontFamily = "Noto Sans", SidebarFontSize = 22, SidebarFontWeight = "Bold",
            ShowWaveform = true, Volume = 33,
        };
        store.Save(prefs);
        AppPrefs loaded = store.Load();
        loaded.SidebarLeft.Should().BeTrue();
        loaded.SidebarTextMask.Should().BeTrue();
        loaded.SidebarShowOriginalText.Should().BeTrue();
        loaded.SidebarSubPadding.Should().Be(12);
        loaded.SidebarFontFamily.Should().Be("Noto Sans");
        loaded.SidebarFontSize.Should().Be(22);
        loaded.SidebarFontWeight.Should().Be("Bold");
        loaded.ShowWaveform.Should().BeTrue("unrelated preferences must survive");
        loaded.Volume.Should().Be(33);

        File.WriteAllText(store.Path, "{\"Volume\": 12}");
        AppPrefs legacy = store.Load();
        legacy.Volume.Should().Be(12);
        legacy.SidebarLeft.Should().BeFalse();
        legacy.SidebarSubPadding.Should().Be(5);
        legacy.SidebarFontFamily.Should().Be("Segoe UI");
        legacy.SidebarFontSize.Should().Be(16);
        legacy.SidebarFontWeight.Should().Be("Normal");
    }

    [Fact]
    public void Draft_loads_new_sidebar_fields_and_Save_persists_them_live_after_write()
    {
        var player = new FakePlaybackController(); var shell = new FakeShell(); var prefs = new AppPrefs();
        AppPrefs? saved = null; int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate =>
        {
            writes++;
            AppPrefs decoded = JsonSerializer.Deserialize<AppPrefs>(JsonSerializer.Serialize(candidate))!;
            saved = decoded;
        }, new FakeTranslator(), shell);
        prefs.SidebarFontSize = 16;

        SettingsViewModel draft = main.CreateSettings();
        draft.SidebarLeft.Should().BeFalse();
        draft.SidebarSubPaddingText.Should().Be("5");
        draft.SidebarFontSizeText.Should().Be("16");
        draft.SidebarFontFamily.Should().Be("Segoe UI");
        draft.SidebarFontWeight.Should().Be("Normal");

        draft.SidebarLeft = true; draft.SidebarTextMask = true; draft.SidebarShowOriginalText = true;
        draft.SidebarSubPaddingText = "12"; draft.SidebarFontSizeText = "22";
        draft.SidebarFontFamily = "Noto Sans"; draft.SidebarFontWeight = "Bold";
        draft.SaveCommand.Execute(null);

        Assert.Null(draft.Error); Assert.Equal(1, writes);
        saved!.SidebarLeft.Should().BeTrue(); saved.SidebarTextMask.Should().BeTrue(); saved.SidebarShowOriginalText.Should().BeTrue();
        saved.SidebarSubPadding.Should().Be(12); saved.SidebarFontSize.Should().Be(22);
        saved.SidebarFontFamily.Should().Be("Noto Sans"); saved.SidebarFontWeight.Should().Be("Bold");
        main.SidebarLeft.Should().BeTrue(); main.SidebarTextMask.Should().BeTrue(); main.SidebarShowOriginalText.Should().BeTrue();
        main.SidebarSubPadding.Should().Be(12); main.SidebarFontSize.Should().Be(22);
        main.SidebarFontFamily.Should().Be("Noto Sans"); main.SidebarFontWeight.Should().Be("Bold");
        main.SidebarRowPadding.Should().Be(new global::Avalonia.Thickness(12));
        main.SidebarListFontSize.Should().Be(22);
        main.SidebarListFontWeight.Should().Be(global::Avalonia.Media.FontWeight.Bold);
    }

    [Theory]
    [InlineData("-1", "16", null)]   // padding below 0
    [InlineData("49", "16", null)]   // padding above 48
    [InlineData("5", "7", null)]     // font below 8
    [InlineData("5", "73", null)]    // font above 72
    [InlineData("5", "NaN", null)]   // non-finite text
    [InlineData("5", "16", "Black")] // unsupported weight
    [InlineData("5", "16", "Segoe UI/evil")] // path-like family
    public void Invalid_sidebar_draft_values_are_rejected_without_persisting(string padding, string size, string? weight)
    {
        var (main, _, _, _, saved) = TestVm.Create(); using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        draft.SidebarSubPaddingText = padding;
        draft.SidebarFontSizeText = size;
        if (weight != null) draft.SidebarFontWeight = weight;
        draft.SaveCommand.Execute(null);
        Assert.NotNull(draft.Error); Assert.Empty(saved);
        Assert.False(main.SidebarLeft);
        Assert.Equal(5, main.SidebarSubPadding);
        Assert.Equal(16, main.SidebarFontSize);
    }

    [Theory]
    [InlineData("0", "8")]
    [InlineData("48", "72")]
    public void Boundary_sidebar_values_are_valid(string padding, string size)
    {
        var (main, _, _, _, saved) = TestVm.Create(); using var owned = main;
        SettingsViewModel draft = main.CreateSettings();
        draft.SidebarSubPaddingText = padding; draft.SidebarFontSizeText = size;
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error); Assert.Single(saved);
        main.SidebarSubPadding.Should().Be(int.Parse(padding));
        main.SidebarFontSize.Should().Be(double.Parse(size));
    }

    [Fact]
    public void Write_failure_keeps_sidebar_live_state_and_prefs_then_retry_applies()
    {
        var prefs = new AppPrefs(); var player = new FakePlaybackController(); var shell = new FakeShell(); bool fail = true;
        using var main = new MainWindowViewModel(player, prefs, _ => { if (fail) throw new IOException("disk full"); }, new FakeTranslator(), shell);
        string before = JsonSerializer.Serialize(prefs);
        SettingsViewModel draft = main.CreateSettings();
        draft.SidebarLeft = true; draft.SidebarTextMask = true; draft.SidebarFontSizeText = "30"; draft.SidebarFontFamily = "Noto Sans";
        int closes = 0; draft.CloseRequested += () => closes++;
        draft.SaveCommand.Execute(null);
        Assert.Contains("disk full", draft.Error!); Assert.Equal(0, closes); Assert.Equal(before, JsonSerializer.Serialize(prefs));
        main.SidebarLeft.Should().BeFalse(); main.SidebarTextMask.Should().BeFalse();
        main.SidebarFontSize.Should().Be(16); main.SidebarFontFamily.Should().Be("Segoe UI");
        fail = false; draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error); Assert.Equal(1, closes);
        main.SidebarLeft.Should().BeTrue(); main.SidebarTextMask.Should().BeTrue();
        main.SidebarFontSize.Should().Be(30); main.SidebarFontFamily.Should().Be("Noto Sans");
    }
}
