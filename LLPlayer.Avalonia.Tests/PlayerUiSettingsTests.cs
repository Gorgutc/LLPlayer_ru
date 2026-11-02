using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Headless.XUnit;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class PlayerUiSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Volume\":42,\"Theme\":\"Light\",\"RecentFiles\":[\"legacy.mp4\"]}")]
    public void Missing_keys_keep_the_approved_existing_player_defaults(string json)
    {
        var prefs = Read(json);
        AssertDefaults(prefs);
        if (json.Contains("42", StringComparison.Ordinal))
        {
            Assert.Equal(42, prefs.Volume); Assert.Equal("Light", prefs.Theme);
            Assert.Equal(["legacy.mp4"], prefs.RecentFiles);
        }
    }

    [Fact]
    public void Normalization_clamps_times_and_preserves_unrelated_fields()
    {
        var prefs = Read("{\"ActivityTimeoutMs\":-1,\"SeekBarFadeInTimeMs\":-1,\"SeekBarFadeOutTimeMs\":10001,\"Volume\":37,\"ShowWaveform\":true}");
        Assert.Equal(0, prefs.ActivityTimeoutMs); Assert.Equal(0, prefs.SeekBarFadeInTimeMs);
        Assert.Equal(10000, prefs.SeekBarFadeOutTimeMs); Assert.Equal(37, prefs.Volume); Assert.True(prefs.ShowWaveform);
        var high = Read("{\"ActivityTimeoutMs\":600001,\"SeekBarFadeInTimeMs\":10001,\"SeekBarFadeOutTimeMs\":-1}");
        Assert.Equal(600000, high.ActivityTimeoutMs); Assert.Equal(10000, high.SeekBarFadeInTimeMs); Assert.Equal(0, high.SeekBarFadeOutTimeMs);
    }

    [Theory]
    [InlineData(1, 1, 0, 8, 4, 1)]
    [InlineData(100, 100, 100, 80, 80, 40)]
    [InlineData(10, 70, 30, 10, 10, 10)]
    [InlineData(15.5, 8.5, 3.5, 15.5, 8.5, 3.5)]
    public void Normalization_bounds_geometry_and_repairs_track_thumb_bar_order(double height, double thumb, double track, double expectedHeight, double expectedThumb, double expectedTrack)
    {
        var prefs = new AppPrefs { SeekBarHeight = height, SeekBarThumbHeight = thumb, SeekBarTrackHeight = track }.Normalize();
        Assert.Equal(expectedHeight, prefs.SeekBarHeight); Assert.Equal(expectedThumb, prefs.SeekBarThumbHeight); Assert.Equal(expectedTrack, prefs.SeekBarTrackHeight);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Nonfinite_geometry_restores_finite_defaults(string value)
    {
        var options = new JsonSerializerOptions(PrefsStore.JsonOptions) { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var prefs = JsonSerializer.Deserialize<AppPrefs>("{\"SeekBarHeight\":\"" + value + "\",\"SeekBarThumbHeight\":\"" + value + "\",\"SeekBarTrackHeight\":\"" + value + "\"}", options)!.Normalize();
        Assert.Equal(20, prefs.SeekBarHeight); Assert.Equal(12, prefs.SeekBarThumbHeight); Assert.Equal(4, prefs.SeekBarTrackHeight);
    }

    [AvaloniaFact]
    public void Save_writes_the_complete_detached_candidate_before_live_apply_and_merges_fresh_preferences()
    {
        var prefs = new AppPrefs().Normalize(); var player = new FakePlaybackController();
        MainWindowViewModel? live = null; AppPrefs? saved = null; int writes = 0;
        using var main = new MainWindowViewModel(player, prefs, candidate =>
        {
            writes++; AssertDefaults(prefs);
            Assert.Equal(2500, live!.ActivityTimeoutMs); Assert.False(live.AlwaysOnTop);
            Assert.NotSame(prefs, candidate); Assert.NotSame(prefs.RecentFiles, candidate.RecentFiles);
            saved = Read(JsonSerializer.Serialize(candidate, PrefsStore.JsonOptions));
            AssertCustom(saved);
        }, new FakeTranslator(), new FakeShell());
        live = main;
        var draft = main.CreateSettings(); SetCustom(draft);
        AssertDefaults(prefs); Assert.Equal(2500, main.ActivityTimeoutMs);
        prefs.Volume = 77; prefs.Mute = true; prefs.LastFolder = "/fresh"; prefs.RecentFiles.Add("fresh.mp4");
        prefs.WordTranslationService = "Off"; prefs.ShowWaveform = true;
        int closes = 0; draft.CloseRequested += () => closes++;
        draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error); Assert.Equal(1, writes); Assert.Equal(1, closes); Assert.NotNull(saved);
        AssertCustom(prefs); Assert.Equal(4200, main.ActivityTimeoutMs); Assert.True(main.AlwaysOnTop);
        Assert.Equal(77, saved.Volume); Assert.True(saved.Mute); Assert.Equal("/fresh", saved.LastFolder);
        Assert.Equal(["fresh.mp4"], saved.RecentFiles); Assert.Equal("Off", saved.WordTranslationService);
        Assert.True(saved.ShowWaveform);
        Assert.Equal(77, prefs.Volume); Assert.True(prefs.ShowWaveform);
    }

    [AvaloniaFact]
    public void Failed_write_leaves_player_preferences_and_live_state_unchanged_then_retry_applies_once()
    {
        var prefs = new AppPrefs().Normalize(); bool fail = true; int writes = 0;
        using var main = new MainWindowViewModel(new FakePlaybackController(), prefs, _ =>
        {
            writes++; if (fail) throw new IOException("player-ui-disk-full");
        }, new FakeTranslator(), new FakeShell());
        string before = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        var draft = main.CreateSettings(); SetCustom(draft);
        int closes = 0; draft.CloseRequested += () => closes++;
        draft.SaveCommand.Execute(null);
        Assert.Contains("player-ui-disk-full", draft.Error!); Assert.Equal(0, closes); Assert.Equal(1, writes);
        Assert.Equal(before, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
        Assert.Equal(2500, main.ActivityTimeoutMs); Assert.False(main.AlwaysOnTop); Assert.True(main.MouseSingleClickToPlay);
        fail = false; draft.SaveCommand.Execute(null);
        Assert.Null(draft.Error); Assert.Equal(1, closes); Assert.Equal(2, writes); AssertCustom(prefs);
        Assert.Equal(4200, main.ActivityTimeoutMs); Assert.True(main.AlwaysOnTop); Assert.False(main.MouseSingleClickToPlay);
    }

    [AvaloniaFact]
    public void Close_discards_every_player_ui_field_without_persistence()
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); SetCustom(draft); int closes = 0; draft.CloseRequested += () => closes++;
        draft.CloseCommand.Execute(null);
        Assert.Equal(1, closes); Assert.Empty(saved); AssertDefaults(prefs);
        var fresh = main.CreateSettings();
        Assert.Equal("2500", fresh.ActivityTimeoutMsText); Assert.Equal("200", fresh.SeekBarFadeInTimeMsText);
        Assert.Equal("200", fresh.SeekBarFadeOutTimeMsText); Assert.Equal("20", fresh.SeekBarHeightText);
        Assert.Equal("12", fresh.SeekBarThumbHeightText); Assert.Equal("4", fresh.SeekBarTrackHeightText);
        Assert.False(fresh.SeekBarShowOnlyMouseOver); Assert.False(fresh.AlwaysOnTop);
        Assert.True(fresh.MouseSingleClickToPlay); Assert.True(fresh.MouseDoubleClickToFullScreen); Assert.True(fresh.MouseWheelToVolumeUpDown);
    }

    [AvaloniaFact]
    public void Saved_player_ui_settings_restore_after_a_real_store_reload_and_new_viewmodel()
    {
        using TempDir dir = new(); var store = new PrefsStore(Path.Combine(dir.Path, "prefs.json"));
        using (var main = new MainWindowViewModel(new FakePlaybackController(), new AppPrefs().Normalize(), store.Save, new FakeTranslator(), new FakeShell()))
        {
            var draft = main.CreateSettings(); SetCustom(draft); draft.SaveCommand.Execute(null); Assert.Null(draft.Error);
        }
        var loaded = store.Load(); AssertCustom(loaded);
        using var restarted = new MainWindowViewModel(new FakePlaybackController(), loaded, store.Save, new FakeTranslator(), new FakeShell());
        Assert.Equal(4200, restarted.ActivityTimeoutMs); Assert.True(restarted.AlwaysOnTop);
        var fresh = restarted.CreateSettings();
        Assert.Equal("4200", fresh.ActivityTimeoutMsText); Assert.Equal("75", fresh.SeekBarFadeInTimeMsText);
        Assert.Equal("350", fresh.SeekBarFadeOutTimeMsText); Assert.Equal("30", fresh.SeekBarHeightText);
        Assert.Equal("18", fresh.SeekBarThumbHeightText); Assert.Equal("6", fresh.SeekBarTrackHeightText);
        Assert.True(fresh.SeekBarShowOnlyMouseOver); Assert.True(fresh.AlwaysOnTop);
        Assert.False(fresh.MouseSingleClickToPlay); Assert.False(fresh.MouseDoubleClickToFullScreen); Assert.False(fresh.MouseWheelToVolumeUpDown);
    }

    [AvaloniaTheory]
    [InlineData("ActivityTimeoutMsText", "")]
    [InlineData("ActivityTimeoutMsText", "word")]
    [InlineData("ActivityTimeoutMsText", "-1")]
    [InlineData("ActivityTimeoutMsText", "600001")]
    [InlineData("ActivityTimeoutMsText", "1.5")]
    [InlineData("SeekBarFadeInTimeMsText", "")]
    [InlineData("SeekBarFadeInTimeMsText", "NaN")]
    [InlineData("SeekBarFadeInTimeMsText", "-1")]
    [InlineData("SeekBarFadeInTimeMsText", "10001")]
    [InlineData("SeekBarFadeOutTimeMsText", "")]
    [InlineData("SeekBarFadeOutTimeMsText", "Infinity")]
    [InlineData("SeekBarFadeOutTimeMsText", "-1")]
    [InlineData("SeekBarFadeOutTimeMsText", "10001")]
    [InlineData("SeekBarHeightText", "")]
    [InlineData("SeekBarHeightText", "NaN")]
    [InlineData("SeekBarHeightText", "7")]
    [InlineData("SeekBarHeightText", "81")]
    [InlineData("SeekBarThumbHeightText", "")]
    [InlineData("SeekBarThumbHeightText", "Infinity")]
    [InlineData("SeekBarThumbHeightText", "3")]
    [InlineData("SeekBarThumbHeightText", "81")]
    [InlineData("SeekBarTrackHeightText", "")]
    [InlineData("SeekBarTrackHeightText", "NaN")]
    [InlineData("SeekBarTrackHeightText", "0")]
    [InlineData("SeekBarTrackHeightText", "41")]
    public void Bad_raw_numeric_input_is_rejected_instead_of_silently_reusing_a_previous_valid_value(string property, string text)
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        string before = JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions);
        var draft = main.CreateSettings();
        typeof(SettingsViewModel).GetProperty(property)!.SetValue(draft, text);
        int closes = 0; draft.CloseRequested += () => closes++;
        draft.SaveCommand.Execute(null);
        Assert.NotNull(draft.Error); Assert.Equal(0, closes); Assert.Empty(saved);
        Assert.Equal(text, typeof(SettingsViewModel).GetProperty(property)!.GetValue(draft));
        Assert.Equal(before, JsonSerializer.Serialize(prefs, PrefsStore.JsonOptions));
        Assert.Equal(2500, main.ActivityTimeoutMs); Assert.False(main.AlwaysOnTop);
    }

    [AvaloniaTheory]
    [InlineData("8", "12", "4")]
    [InlineData("20", "4", "5")]
    public void Finite_but_inconsistent_geometry_is_rejected_atomically(string height, string thumb, string track)
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); draft.SeekBarHeightText = height; draft.SeekBarThumbHeightText = thumb; draft.SeekBarTrackHeightText = track;
        draft.AlwaysOnTop = true; draft.SaveCommand.Execute(null);
        Assert.NotNull(draft.Error); Assert.Empty(saved); AssertDefaults(prefs); Assert.False(main.AlwaysOnTop);
    }

    [AvaloniaTheory]
    [InlineData("0", "0", "0", "8", "4", "1")]
    [InlineData("600000", "10000", "10000", "80", "80", "40")]
    public void Exact_numeric_boundaries_save_and_zero_raw_text_is_seeded_on_reopen(string idle, string fadeIn, string fadeOut, string height, string thumb, string track)
    {
        var (main, _, _, prefs, saved) = TestVm.Create(); using var owned = main;
        var draft = main.CreateSettings(); draft.ActivityTimeoutMsText = idle; draft.SeekBarFadeInTimeMsText = fadeIn; draft.SeekBarFadeOutTimeMsText = fadeOut;
        draft.SeekBarHeightText = height; draft.SeekBarThumbHeightText = thumb; draft.SeekBarTrackHeightText = track;
        draft.SaveCommand.Execute(null); Assert.Null(draft.Error); Assert.Single(saved);
        Assert.Equal(int.Parse(idle, CultureInfo.InvariantCulture), prefs.ActivityTimeoutMs);
        var fresh = main.CreateSettings();
        Assert.Equal(idle, fresh.ActivityTimeoutMsText); Assert.Equal(fadeIn, fresh.SeekBarFadeInTimeMsText); Assert.Equal(fadeOut, fresh.SeekBarFadeOutTimeMsText);
        Assert.Equal(height, fresh.SeekBarHeightText); Assert.Equal(thumb, fresh.SeekBarThumbHeightText); Assert.Equal(track, fresh.SeekBarTrackHeightText);
    }

    static AppPrefs Read(string json) => JsonSerializer.Deserialize<AppPrefs>(json, PrefsStore.JsonOptions)!.Normalize();
    static void SetCustom(SettingsViewModel draft)
    {
        draft.ActivityTimeoutMsText = "4200"; draft.SeekBarFadeInTimeMsText = "75"; draft.SeekBarFadeOutTimeMsText = "350";
        draft.SeekBarHeightText = "30"; draft.SeekBarThumbHeightText = "18"; draft.SeekBarTrackHeightText = "6";
        draft.SeekBarShowOnlyMouseOver = true; draft.AlwaysOnTop = true;
        draft.MouseSingleClickToPlay = false; draft.MouseDoubleClickToFullScreen = false; draft.MouseWheelToVolumeUpDown = false;
    }
    static void AssertDefaults(AppPrefs prefs)
    {
        Assert.Equal(2500, prefs.ActivityTimeoutMs); Assert.Equal(200, prefs.SeekBarFadeInTimeMs); Assert.Equal(200, prefs.SeekBarFadeOutTimeMs);
        Assert.Equal(20, prefs.SeekBarHeight); Assert.Equal(12, prefs.SeekBarThumbHeight); Assert.Equal(4, prefs.SeekBarTrackHeight);
        Assert.False(prefs.SeekBarShowOnlyMouseOver); Assert.False(prefs.AlwaysOnTop);
        Assert.True(prefs.MouseSingleClickToPlay); Assert.True(prefs.MouseDoubleClickToFullScreen); Assert.True(prefs.MouseWheelToVolumeUpDown);
    }
    static void AssertCustom(AppPrefs prefs)
    {
        Assert.Equal(4200, prefs.ActivityTimeoutMs); Assert.Equal(75, prefs.SeekBarFadeInTimeMs); Assert.Equal(350, prefs.SeekBarFadeOutTimeMs);
        Assert.Equal(30, prefs.SeekBarHeight); Assert.Equal(18, prefs.SeekBarThumbHeight); Assert.Equal(6, prefs.SeekBarTrackHeight);
        Assert.True(prefs.SeekBarShowOnlyMouseOver); Assert.True(prefs.AlwaysOnTop);
        Assert.False(prefs.MouseSingleClickToPlay); Assert.False(prefs.MouseDoubleClickToFullScreen); Assert.False(prefs.MouseWheelToVolumeUpDown);
    }
}
