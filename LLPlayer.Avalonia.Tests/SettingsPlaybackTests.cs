using System.Globalization;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FlyleafLib;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class SettingsPlaybackTests
{
    [Fact]
    public void Absent_settings_preserve_existing_engine_and_remembered_volume_defaults()
    {
        using var temp = new TempDir();
        var store = new PlaybackSettingsStore(Path.Combine(temp.Path, "playback.json"));
        PlaybackSettings settings = store.Load();
        Assert.False(store.HasSavedSettings); Assert.Null(store.LoadWarning);
        Assert.True(settings.AudioEnabled); Assert.True(settings.AudioFiltersEnabled);
        Assert.Equal(150, settings.AudioVolumeMax); Assert.Equal(75, settings.AudioVolumeDefault);
        Assert.Equal(500, settings.MinBufferDurationMs); Assert.Equal(30000, settings.BufferDurationMs);
        Assert.Equal(5, settings.MaxAudioFrames); Assert.Equal(4, settings.MaxVideoFrames);
        Assert.Equal(300000, settings.OpenTimeoutMs); Assert.Equal(10000, settings.ReadTimeoutMs);
        Assert.True(settings.SearchLocal); Assert.True(settings.ResegmentSubtitles);
        Assert.True(settings.SubtitleExportUtf8Bom); Assert.Equal(100, new AppPrefs().Volume);
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public void Store_roundtrips_complete_owned_settings_and_keeps_session_values_out_of_json()
    {
        using var temp = new TempDir(); var store = new PlaybackSettingsStore(Path.Combine(temp.Path, "playback.json"));
        var settings = new PlaybackSettings { AudioLanguages = ["ja", "en"], SubtitleLanguages = ["ru", "en"],
            AudioVolumeMax = 300, AudioVolumeDefault = 180, AspectRatio = "Custom", AspectRatioCustom = "4:3",
            DeInterlace = DeInterlace.TopField, Brightness = 20, Hue = -60, SeekAccurate = true,
            SearchLocalPaths = "subs;../captions", LanguageFallbackPrimary = "ru", LanguageFallbackSecondarySame = false,
            LanguageFallbackSecondary = "ja", SubtitleExportUtf8Bom = false, SubtitleMaxCueDurationSec = 8.5,
            ASRDenoise = true, ASRPerSegmentLanguage = true, WordTimestamps = false, VadCueSnapping = false,
            FixAllCaps = false, LogOutput = "logs/player.log", LogCachedLines = 31 };
        store.Save(settings);
        Assert.True(store.HasSavedSettings); PlaybackSettings loaded = store.Load();
        Assert.Equal(JsonSerializer.Serialize(settings, PlaybackSettingsStore.JsonOptions), JsonSerializer.Serialize(loaded, PlaybackSettingsStore.JsonOptions));
        string json = File.ReadAllText(store.Path);
        Assert.DoesNotContain("Delay", json); Assert.DoesNotContain("Device", json); Assert.DoesNotContain("ZoomPercent", json);
    }

    [Fact]
    public void Corrupt_or_invalid_settings_are_preserved_and_an_explicit_valid_save_recovers()
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "playback.json");
        File.WriteAllText(path, "{\"AudioVolumeMax\":0}"); var store = new PlaybackSettingsStore(path);
        Assert.Equal(150, store.Load().AudioVolumeMax); Assert.NotNull(store.LoadWarning);
        Assert.False(store.HasSavedSettings); Assert.Equal("{\"AudioVolumeMax\":0}", File.ReadAllText(path));
        Assert.Throws<ArgumentException>(() => store.Save(new PlaybackSettings { MaxVideoFrames = 0 }));
        Assert.Equal("{\"AudioVolumeMax\":0}", File.ReadAllText(path));
        store.Save(new PlaybackSettings()); Assert.Null(store.LoadWarning); Assert.True(store.HasSavedSettings);
    }

    [Fact]
    public void Merge_only_applies_edited_fields_and_clones_language_lists()
    {
        var baseline = new PlaybackSettings { AudioLanguages = ["en"], SubtitleLanguages = ["en"] };
        var draft = baseline.Clone(); draft.Brightness = 14; draft.AudioLanguages.Add("ja");
        var fresh = baseline.Clone(); fresh.AudioVolumeDefault = 92; fresh.SearchLocalPaths = "fresh"; fresh.Saturation = 12;
        PlaybackSettings merged = draft.MergeEdits(baseline, fresh);
        Assert.Equal(14, merged.Brightness); Assert.Equal(92, merged.AudioVolumeDefault);
        Assert.Equal("fresh", merged.SearchLocalPaths); Assert.Equal(12, merged.Saturation);
        Assert.Equal(["en", "ja"], merged.AudioLanguages); merged.AudioLanguages.Add("ru");
        Assert.Equal(["en", "ja"], draft.AudioLanguages); Assert.Equal(["en"], fresh.AudioLanguages);
    }

    [Fact]
    public void Secondary_failure_restores_the_original_raw_playback_bytes_and_saved_state()
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "playback.json");
        byte[] original = new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(
            "{\r\n // preserve owner formatting\r\n \"AudioVolumeDefault\": 50,\r\n}\r\n")).ToArray();
        File.WriteAllBytes(path, original); var store = new PlaybackSettingsStore(path); store.Load();
        Assert.True(store.HasSavedSettings); int callbacks = 0;
        Assert.Throws<IOException>(() => store.Save(new PlaybackSettings { AudioVolumeDefault = 90 }, () =>
        {
            callbacks++; Assert.Contains("90", File.ReadAllText(path)); throw new IOException("secondary write failure");
        }));
        Assert.Equal(1, callbacks); Assert.Equal(original, File.ReadAllBytes(path)); Assert.True(store.HasSavedSettings);
    }

    [Fact]
    public void Secondary_failure_on_first_save_removes_only_its_candidate_and_preserves_foreign_replacements()
    {
        using var temp = new TempDir(); string path = Path.Combine(temp.Path, "playback.json"); var store = new PlaybackSettingsStore(path);
        Assert.Throws<IOException>(() => store.Save(new(), () => throw new IOException("secondary failed")));
        Assert.False(File.Exists(path)); Assert.False(store.HasSavedSettings);
        const string replacement = "{\"AudioVolumeDefault\":88}";
        Assert.Throws<IOException>(() => store.Save(new(), () =>
        { File.WriteAllText(path, replacement); throw new IOException("secondary failed after external edit"); }));
        Assert.Equal(replacement, File.ReadAllText(path)); Assert.False(store.HasSavedSettings);
    }

    [Fact]
    public void Secondary_commit_runs_only_after_successful_primary_commit_and_success_marks_the_store_saved()
    {
        using var temp = new TempDir(); int callbacks = 0;
        var failing = new PlaybackSettingsStore(temp.Path);
        Exception failure = Assert.ThrowsAny<Exception>(() => failing.Save(new(), () => callbacks++));
        Assert.True(failure is IOException or UnauthorizedAccessException); Assert.Equal(0, callbacks);
        var store = new PlaybackSettingsStore(Path.Combine(temp.Path, "playback.json"));
        store.Save(new() { AudioVolumeDefault = 92 }, () => { callbacks++; Assert.True(File.Exists(store.Path)); });
        Assert.Equal(1, callbacks); Assert.True(store.HasSavedSettings); Assert.Equal(92, store.Load().AudioVolumeDefault);
    }

    [Theory]
    [InlineData("player.log")]
    [InlineData(":console")]
    [InlineData(":debug")]
    [InlineData(":custom")]
    [InlineData("")]
    public void Supported_engine_log_outputs_validate_without_starting_logging(string output)
        => new PlaybackSettings { LogOutput = output }.Validate();

    [Fact]
    public void Unknown_engine_log_token_is_rejected_before_startup()
        => Assert.Throws<ArgumentException>(() => new PlaybackSettings { LogOutput = ":unknown" }.Validate());

    [Fact]
    public void Wpf_zero_cue_duration_controls_disable_their_policy_and_zero_log_cache_flushes_immediately()
    {
        var settings = new PlaybackSettings { SubtitleMaxCueDurationSec = 0, SubtitleMinCueDurationSec = 0, LogCachedLines = 0 };
        settings.Validate();
        Config config = new(true); settings.ApplyTo(config, includeAudio: false);
        Assert.Equal(0, config.Subtitles.SubtitleMaxCueDurationSec);
        Assert.Equal(0, config.Subtitles.SubtitleMinCueDurationSec);
    }

    [Fact]
    public void Applying_policy_uses_engine_fields_and_preserves_unowned_configuration()
    {
        Config config = new(true); config.Player.AutoPlay = false; config.Video.Zoom = 120;
        var settings = new PlaybackSettings { MinBufferDurationMs = 700, BufferDurationMs = 45000,
            MaxAudioFrames = 9, MaxVideoFrames = 11, SeekAccurate = true, SeekAccurateFixMarginMs = 23,
            AspectRatio = "Custom", AspectRatioCustom = "4:3", SDRDisplayNits = 300,
            SubtitleLanguages = ["ja", "en"], LanguageFallbackPrimary = "ru", LanguageFallbackSecondary = "ja",
            LanguageFallbackSecondarySame = false, ResegmentSubtitles = false, SubtitleMaxCharsPerLine = 52,
            ASRDenoise = true, ASRPerSegmentLanguage = true, FixAllCaps = false, Brightness = 25 };
        settings.ApplyTo(config, includeAudio: false);
        Assert.Equal(TimeSpan.FromMilliseconds(700).Ticks, config.Player.MinBufferDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(45000).Ticks, config.Demuxer.BufferDuration);
        Assert.Equal(9, config.Decoder.MaxAudioFrames); Assert.Equal(11, config.Decoder.MaxVideoFrames);
        Assert.True(config.Player.SeekAccurate); Assert.Equal(230000, config.Player.SeekAccurateFixMargin);
        Assert.Equal(new AspectRatio(4, 3), config.Video.AspectRatioCustom); Assert.Equal(300, config.Video.SDRDisplayNits);
        Assert.Equal("ru", config.Subtitles.LanguageFallbackPrimary.ISO6391);
        Assert.Equal("ja", config.Subtitles.LanguageFallbackSecondary.ISO6391);
        Assert.False(config.Subtitles.ResegmentSubtitles); Assert.Equal(52, config.Subtitles.SubtitleMaxCharsPerLine);
        Assert.True(config.Subtitles.ASRDenoise); Assert.True(config.Subtitles.ASRPerSegmentLanguage);
        Assert.False(config.Subtitles.FixAllCaps); Assert.False(config.Player.AutoPlay); Assert.Equal(120, config.Video.Zoom);
    }

    [AvaloniaTheory]
    [InlineData("AudioVolumeMax", "0")]
    [InlineData("MaxVideoFrames", "1.5")]
    [InlineData("OpenTimeoutMs", "9223372036854775807")]
    [InlineData("SubtitleMaxCueDurationSec", "NaN")]
    public async Task Raw_invalid_numeric_input_is_retained_and_never_saved(string key, string value)
    {
        int saves = 0; var model = new PlaybackSettingsViewModel(new(), new(), (_, _, _, _) => { saves++; return Task.CompletedTask; });
        model.FindNumber(key).Text = value;
        await model.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(model.Error); Assert.Equal(value, model.FindNumber(key).Text); Assert.Equal(0, saves);
    }

    [AvaloniaFact]
    public async Task Detached_save_failure_keeps_draft_open_and_default_volume_is_applied_only_when_edited()
    {
        int saves = 0, closes = 0; bool fail = true; bool? applyVolume = null;
        var initial = new PlaybackSettings { AudioVolumeDefault = 75 };
        var model = new PlaybackSettingsViewModel(initial, new(), (edited, baseline, runtime, volume) =>
        {
            saves++; applyVolume = volume; Assert.NotSame(initial, edited); Assert.Equal(75, baseline.AudioVolumeDefault);
            Assert.Equal(1200000, runtime.AudioDelayTicks);
            if (fail) throw new IOException("test disk failure"); return Task.CompletedTask;
        });
        model.CloseRequested += () => closes++;
        model.FindNumber("AudioDelayMs").Text = "120";
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, closes); Assert.NotNull(model.Error); Assert.False(model.Busy); Assert.False(applyVolume);
        model.FindNumber("AudioVolumeDefault").Text = "90"; fail = false;
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Null(model.Error); Assert.Equal(1, closes); Assert.Equal(2, saves); Assert.True(applyVolume);
        Assert.Equal(75, initial.AudioVolumeDefault);
    }

    [AvaloniaFact]
    public async Task Local_decimal_input_and_narrow_resets_preserve_all_unrelated_settings()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            PlaybackSettings? saved = null;
            var initial = new PlaybackSettings { Brightness = 20, Contrast = -10, AudioVolumeDefault = 61, SearchLocalPaths = "custom", LogCachedLines = 31 };
            var model = new PlaybackSettingsViewModel(initial, new(), (candidate, _, _, _) => { saved = candidate; return Task.CompletedTask; });
            model.FindNumber("SubtitleMaxCueDurationSec").Text = "8,5";
            model.ResetFiltersCommand.Execute(null);
            model.ResetSearchLocalPathsCommand.Execute(null);
            await model.SaveCommand.ExecuteAsync(null);
            Assert.Null(model.Error); Assert.NotNull(saved); Assert.Equal(8.5, saved.SubtitleMaxCueDurationSec);
            Assert.Equal(0, saved.Brightness); Assert.Equal(0, saved.Contrast);
            Assert.Equal(61, saved.AudioVolumeDefault); Assert.Equal(31, saved.LogCachedLines);
            Assert.Equal(Config.SubtitlesConfig.DefaultSearchLocalPaths, saved.SearchLocalPaths);
            Assert.Equal(20, initial.Brightness); Assert.Equal("custom", initial.SearchLocalPaths);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [AvaloniaFact]
    public async Task Ordered_language_editors_and_close_keep_the_original_snapshot_detached()
    {
        var initial = new PlaybackSettings { AudioLanguages = ["en", "ja"] }; int saves = 0;
        var model = new PlaybackSettingsViewModel(initial, new(), (_, _, _, _) => { saves++; return Task.CompletedTask; });
        model.AudioLanguages.Selected = model.AudioLanguages.SelectedLanguages[1];
        model.AudioLanguages.MoveUpCommand.Execute(null);
        Assert.Equal("ja", model.AudioLanguages.SelectedLanguages[0].Code);
        Assert.Equal(["en", "ja"], initial.AudioLanguages);
        model.CloseCommand.Execute(null); Assert.Equal(0, saves);
        await Task.CompletedTask;
    }
}
