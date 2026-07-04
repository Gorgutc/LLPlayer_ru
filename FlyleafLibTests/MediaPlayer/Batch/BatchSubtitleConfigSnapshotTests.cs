using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Dubbing;
using FlyleafLib.MediaPlayer.Translation;
using Whisper.net.LibraryLoader;

namespace FlyleafLib.MediaPlayer.Batch;

// BatchSubtitleConfigSnapshot deep-copies the live Config so a batch run is isolated from later UI edits.
// BatchSubtitleTranslatorTests already covers scalar-field completeness (a reflection guard that, by design,
// excludes nested config objects and COLLECTIONS) plus the WhisperCpp-model and language-fallback copies.
// This suite covers the complementary gap: that the collection / dictionary / nested-object members are
// independent deep copies (mutating the source after the snapshot must not bleed into it) and that the
// faster-whisper "--task" stripping behaves across argument forms.
public class BatchSubtitleConfigSnapshotTests
{
    private static Config NewTestConfig()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        // Seed the language list so the lazy LanguageFallback* getters don't call GetSystemLanguages()
        // (which NREs in headless tests).
        config.Subtitles.Languages = [Language.English];
        return config;
    }

    [Fact]
    public void CreateSubtitlesConfig_AsrPerSegmentLanguage_IsCopied()
    {
        Config config = NewTestConfig();
        config.Subtitles.ASRPerSegmentLanguage = true;

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        // The T-10 toggle must reach the headless batch snapshot — it gates the shared ASR services' language pin.
        snapshot.ASRPerSegmentLanguage.Should().BeTrue();
    }

    [Fact]
    public void CreateSubtitlesConfig_TesseractOcrRegions_AreAnIndependentCopy()
    {
        Config config = NewTestConfig();
        config.Subtitles.TesseractOcrRegions["eng"] = "rus";

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.TesseractOcrRegions.Should().NotBeSameAs(config.Subtitles.TesseractOcrRegions);
        snapshot.TesseractOcrRegions["eng"].Should().Be("rus");

        config.Subtitles.TesseractOcrRegions["deu"] = "fra";
        snapshot.TesseractOcrRegions.Should().NotContainKey("deu");
    }

    [Fact]
    public void CreateSubtitlesConfig_MsOcrRegions_AreAnIndependentCopy()
    {
        Config config = NewTestConfig();
        config.Subtitles.MsOcrRegions["eng"] = "en-US";

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.MsOcrRegions.Should().NotBeSameAs(config.Subtitles.MsOcrRegions);
        snapshot.MsOcrRegions["eng"].Should().Be("en-US");

        config.Subtitles.MsOcrRegions["fra"] = "fr-FR";
        snapshot.MsOcrRegions.Should().NotContainKey("fra");
    }

    [Fact]
    public void CreateSubtitlesConfig_SearchLocalOnInputType_IsAnIndependentCopy()
    {
        Config config = NewTestConfig();
        var before = config.Subtitles.SearchLocalOnInputType.ToList();

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.SearchLocalOnInputType.Should().NotBeSameAs(config.Subtitles.SearchLocalOnInputType);
        snapshot.SearchLocalOnInputType.Should().Equal(before);

        config.Subtitles.SearchLocalOnInputType.Clear();
        snapshot.SearchLocalOnInputType.Should().Equal(before);
    }

    [Fact]
    public void CreateSubtitlesConfig_SearchOnlineOnInputType_IsAnIndependentCopy()
    {
        Config config = NewTestConfig();
        var before = config.Subtitles.SearchOnlineOnInputType.ToList();

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.SearchOnlineOnInputType.Should().NotBeSameAs(config.Subtitles.SearchOnlineOnInputType);
        snapshot.SearchOnlineOnInputType.Should().Equal(before);

        config.Subtitles.SearchOnlineOnInputType.Clear();
        snapshot.SearchOnlineOnInputType.Should().Equal(before);
    }

    [Fact]
    public void CreateSubtitlesConfig_NestedConfigObjects_AreDistinctInstances()
    {
        Config config = NewTestConfig();

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.Should().NotBeSameAs(config.Subtitles);
        snapshot.WhisperConfig.Should().NotBeSameAs(config.Subtitles.WhisperConfig);
        snapshot.WhisperCppConfig.Should().NotBeSameAs(config.Subtitles.WhisperCppConfig);
        snapshot.FasterWhisperConfig.Should().NotBeSameAs(config.Subtitles.FasterWhisperConfig);
        snapshot.TranslateChatConfig.Should().NotBeSameAs(config.Subtitles.TranslateChatConfig);
    }

    [Theory]
    [InlineData("--task translate", "")]
    [InlineData("--device cpu --task=transcribe --vad_filter True", "--device cpu --vad_filter True")]
    [InlineData("--task   translate --beam_size 5", "--beam_size 5")]
    [InlineData("--language ru --beam_size 5", "--language ru --beam_size 5")]
    [InlineData("", "")]
    public void CreateSubtitlesConfig_StripsFasterWhisperTaskArgument(string extraArguments, string expected)
    {
        // The batch path forces transcription, so any user --task (e.g. translate) must be removed from the
        // faster-whisper extra arguments; the rest is preserved and whitespace is collapsed.
        Config config = NewTestConfig();
        config.Subtitles.FasterWhisperConfig.ExtraArguments = extraArguments;

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.FasterWhisperConfig.ExtraArguments.Should().Be(expected);
    }

    [Fact]
    public void CreateSubtitlesConfig_ForcesRussianTarget_AndDisablesWhisperTranslate()
    {
        Config config = NewTestConfig();
        config.Subtitles.TranslateTargetLanguage = TargetLanguage.EnglishAmerican;
        config.Subtitles.WhisperConfig.Translate = true;

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.TranslateTargetLanguage.Should().Be(TargetLanguage.Russian);
        snapshot.WhisperConfig.Translate.Should().BeFalse();
        // The live config is untouched.
        config.Subtitles.TranslateTargetLanguage.Should().Be(TargetLanguage.EnglishAmerican);
        config.Subtitles.WhisperConfig.Translate.Should().BeTrue();
    }

    [Fact]
    public void Create_PluginOptions_AreDeepCopied()
    {
        Config config = NewTestConfig();
        config.Plugins["unit-test-plugin"] = new Utils.ObservableDictionary<string, string>();
        config.Plugins["unit-test-plugin"]["key"] = "val";

        Config snapshot = BatchSubtitleConfigSnapshot.Create(config);

        snapshot.Plugins.Should().ContainKey("unit-test-plugin");
        snapshot.Plugins["unit-test-plugin"].Should().NotBeSameAs(config.Plugins["unit-test-plugin"]);
        snapshot.Plugins["unit-test-plugin"]["key"].Should().Be("val");

        config.Plugins["unit-test-plugin"]["key"] = "changed";
        snapshot.Plugins["unit-test-plugin"]["key"].Should().Be("val");
    }

    [Fact]
    public void Create_ProducesAnIndependentTopLevelSnapshot()
    {
        Config config = NewTestConfig();
        config.Audio.Languages = [Language.Get("ja")];

        Config snapshot = BatchSubtitleConfigSnapshot.Create(config);

        snapshot.Should().NotBeSameAs(config);
        snapshot.Audio.Should().NotBeSameAs(config.Audio);
        snapshot.Subtitles.Should().NotBeSameAs(config.Subtitles);
        snapshot.Audio.Languages.Should().NotBeSameAs(config.Audio.Languages);
        snapshot.Audio.Languages.Select(l => l.ISO6391).Should().Equal("ja");

        snapshot.Audio.Languages.Clear();
        config.Audio.Languages.Select(l => l.ISO6391).Should().Equal("ja");
    }

    [Fact]
    public void CreateSubtitlesConfig_ShouldSnapshotUserDubbingConfig()
    {
        // F-05-gap regression (RED-without-fix): before the fix the batch snapshot never copied the nested
        // DubbingConfig, so a batch/headless dub built off the snapshot silently used a fresh default
        // (ru-preset-1, empty custom ids, DuckingPercent 15, atempo 0.9–1.15). Dropping CloneDubbingConfig fails
        // every assertion below.
        Config config = NewTestConfig();
        DubbingConfig live = config.Subtitles.DubbingConfig;
        live.TtsServiceType = TtsServiceType.ElevenLabs;
        live.DefaultVoiceId = "ru-preset-2";
        live.CustomVoiceIds = ["studio-anna", "studio-boris"];
        live.DuckingPercent = 40;
        live.AtempoMin = 0.8;
        live.AtempoMax = 1.3;
        // HC-45: OutputFormat is normalized to the FLAC-only whitelist on set, so a hand-edited non-flac request
        // collapses to "flac" before the snapshot ever copies it. The 8 non-default fields above already guard the
        // deep copy; here we assert the snapshot carries the NORMALIZED value (no leaked "wav"). The normalization
        // itself is unit-tested in DubbingConfigTests.OutputFormat_*.
        live.OutputFormat = "wav";
        live.StressNormalization = false;
        live.Model = "cosyvoice2-custom";

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.DubbingConfig.Should().NotBeSameAs(live);
        snapshot.DubbingConfig.TtsServiceType.Should().Be(TtsServiceType.ElevenLabs);
        snapshot.DubbingConfig.DefaultVoiceId.Should().Be("ru-preset-2");
        snapshot.DubbingConfig.CustomVoiceIds.Should().Equal("studio-anna", "studio-boris");
        snapshot.DubbingConfig.DuckingPercent.Should().Be(40);
        snapshot.DubbingConfig.AtempoMin.Should().Be(0.8);
        snapshot.DubbingConfig.AtempoMax.Should().Be(1.3);
        snapshot.DubbingConfig.OutputFormat.Should().Be("flac");
        snapshot.DubbingConfig.StressNormalization.Should().BeFalse();
        snapshot.DubbingConfig.Model.Should().Be("cosyvoice2-custom");

        // The CustomVoiceIds list is an independent deep copy — a later live-config edit must not bleed into it.
        snapshot.DubbingConfig.CustomVoiceIds.Should().NotBeSameAs(live.CustomVoiceIds);
        live.CustomVoiceIds = ["studio-anna", "studio-boris", "studio-clara"];
        snapshot.DubbingConfig.CustomVoiceIds.Should().Equal("studio-anna", "studio-boris");
    }

    [Fact]
    public void CreateSubtitlesConfig_ShouldCopyEveryWritableDubbingConfigSetting()
    {
        // Completeness guard for the nested DubbingConfig (F-05-gap). The SubtitlesConfig scalar reflection guard
        // (BatchSubtitleTranslatorTests.CreateSubtitlesConfig_ShouldCopyEveryScalarSubtitlesConfigSetting)
        // explicitly SKIPS nested config objects, so without a dedicated guard a forgotten DubbingConfig field
        // would silently fall back to a default in the snapshot. Enumerates every public, settable, persisted
        // property on DubbingConfig, mutates it away from its default on the source, snapshots, then asserts the
        // snapshot carried the value over. If you add a new DubbingConfig setting, copy it in CloneDubbingConfig.
        Config config = NewTestConfig();

        Type type = typeof(DubbingConfig);
        DubbingConfig source = config.Subtitles.DubbingConfig;

        List<PropertyInfo> props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == type)
            .Where(p => p is { CanRead: true, CanWrite: true } && p.SetMethod!.IsPublic)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            // HC-45: OutputFormat's setter normalizes every value to the FLAC-only whitelist, so the
            // value-distinctness probe below collapses to "flac" — which also equals the snapshot's default.
            // A copy check is therefore unprovable here (source == snapshot == "flac" whether or not
            // CloneDubbingConfig copies it), so this gate cannot stay fail-closed for it. Exclude it — as the
            // transformed Translate/ExtraArguments fields are excluded in the sibling AssertSnapshotCopies*
            // gates — so the "delete a Clone* line → RED" invariant stays literally true for every field this
            // guard still enumerates. OutputFormat copy correctness is instead covered by
            // DubbingConfigTests.OutputFormat_* and CloneDubbingConfig directly.
            .Where(p => p.Name != nameof(DubbingConfig.OutputFormat))
            .ToList();

        props.Should().NotBeEmpty("the guard must actually enumerate DubbingConfig settings");

        foreach (PropertyInfo prop in props)
        {
            bool isList = prop.PropertyType == typeof(List<string>);
            (isList || IsScalarSettingType(prop.PropertyType)).Should().BeTrue(
                $"the DubbingConfig guard must know how to probe DubbingConfig.{prop.Name} (type {prop.PropertyType}) — extend it");

            object? mutated = isList
                ? new List<string> { "probe-voice-a", "probe-voice-b" }
                : NextDistinctValue(prop.PropertyType, prop.GetValue(source));
            prop.SetValue(source, mutated);
        }

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        foreach (PropertyInfo prop in props)
        {
            object? expected = prop.GetValue(source);
            object? actual = prop.GetValue(snapshot.DubbingConfig);

            if (prop.PropertyType == typeof(List<string>))
                ((List<string>)actual!).Should().Equal(
                    (List<string>)expected!, $"the batch snapshot must copy DubbingConfig.{prop.Name}");
            else
                actual.Should().Be(expected, $"the batch snapshot must copy DubbingConfig.{prop.Name}");
        }
    }

    // HC-39: generalize the DubbingConfig completeness-guard to every nested config the snapshot deep-copies.
    // The SubtitlesConfig scalar guard (BatchSubtitleTranslatorTests) deliberately skips nested objects, so a
    // forgotten field on any of these would silently fall back to a default in the snapshot (the #42/#43/F-05-gap
    // bug class). These are future-proofing guards — every field is copied today; deleting a line from the
    // matching Clone* method in BatchSubtitleConfigSnapshot turns the corresponding guard RED.

    [Fact]
    public void CreateSubtitlesConfig_ShouldCopyEveryWritableWhisperConfigSetting()
    {
        Config config = NewTestConfig();
        // Translate is intentionally forced false for batch (pinned by CreateSubtitlesConfig_ForcesRussianTarget_*).
        AssertSnapshotCopiesEveryWritableProperty(
            config, config.Subtitles.WhisperConfig, s => s.WhisperConfig, nameof(WhisperConfig.Translate));
    }

    [Fact]
    public void CreateSubtitlesConfig_ShouldCopyEveryWritableWhisperCppConfigSetting()
    {
        Config config = NewTestConfig();
        // Model is a nested WhisperCppModel — covered by the dedicated model guard below.
        AssertSnapshotCopiesEveryWritableProperty(
            config, config.Subtitles.WhisperCppConfig, s => s.WhisperCppConfig, nameof(WhisperCppConfig.Model));
    }

    [Fact]
    public void CreateSubtitlesConfig_ShouldCopyEveryWritableFasterWhisperConfigSetting()
    {
        Config config = NewTestConfig();
        // ExtraArguments is rewritten by RemoveFasterWhisperTaskArgument (pinned by CreateSubtitlesConfig_Strips*).
        AssertSnapshotCopiesEveryWritableProperty(
            config, config.Subtitles.FasterWhisperConfig, s => s.FasterWhisperConfig,
            nameof(FasterWhisperConfig.ExtraArguments));
    }

    [Fact]
    public void CreateSubtitlesConfig_ShouldCopyEveryWritableTranslateChatConfigSetting()
    {
        Config config = NewTestConfig();
        AssertSnapshotCopiesEveryWritableProperty(
            config, config.Subtitles.TranslateChatConfig, s => s.TranslateChatConfig);
    }

    [Fact]
    public void CreateSubtitlesConfig_ShouldCopyEveryWritableWhisperCppModelSetting()
    {
        Config config = NewTestConfig();
        config.Subtitles.WhisperCppConfig.Model = new WhisperCppModel(); // default Model is null
        AssertSnapshotCopiesEveryWritableProperty(
            config, config.Subtitles.WhisperCppConfig.Model, s => s.WhisperCppConfig.Model!);
    }

    private static void AssertSnapshotCopiesEveryWritableProperty<TConfig>(
        Config config, TConfig source, Func<Config.SubtitlesConfig, TConfig> snapshotSelector,
        params string[] exclude)
    {
        Type type = typeof(TConfig);
        List<PropertyInfo> props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == type)
            .Where(p => p is { CanRead: true, CanWrite: true } && p.SetMethod!.IsPublic)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .Where(p => !exclude.Contains(p.Name))
            .ToList();

        props.Should().NotBeEmpty($"the guard must actually enumerate {type.Name} settings");

        foreach (PropertyInfo prop in props)
            prop.SetValue(source, MutatedProbeValue(prop, prop.GetValue(source)));

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);
        TConfig target = snapshotSelector(snapshot);

        foreach (PropertyInfo prop in props)
        {
            object? expected = prop.GetValue(source);
            object? actual = prop.GetValue(target);
            string because = $"the batch snapshot must copy {type.Name}.{prop.Name}";

            if (prop.PropertyType == typeof(List<string>))
                ((List<string>)actual!).Should().Equal((List<string>)expected!, because);
            else if (prop.PropertyType == typeof(List<RuntimeLibrary>))
                ((List<RuntimeLibrary>)actual!).Should().Equal((List<RuntimeLibrary>)expected!, because);
            else
                actual.Should().Be(expected, because);
        }
    }

    private static object? MutatedProbeValue(PropertyInfo prop, object? current)
    {
        Type t = prop.PropertyType;
        if (t == typeof(List<string>))
            return new List<string> { "probe-a", "probe-b" };
        if (t == typeof(List<RuntimeLibrary>))
            return new List<RuntimeLibrary> { RuntimeLibrary.Cpu };

        IsScalarSettingType(t).Should().BeTrue(
            $"the nested-config guard must know how to probe {prop.DeclaringType!.Name}.{prop.Name} (type {t}) — extend it");
        return NextDistinctValue(t, current);
    }

    private static bool IsScalarSettingType(Type t)
    {
        Type u = Nullable.GetUnderlyingType(t) ?? t;
        return u.IsEnum
            || u == typeof(bool) || u == typeof(int) || u == typeof(long)
            || u == typeof(double) || u == typeof(float) || u == typeof(string);
    }

    private static object? NextDistinctValue(Type t, object? current)
    {
        Type u = Nullable.GetUnderlyingType(t) ?? t;

        if (u.IsEnum)
        {
            foreach (object v in Enum.GetValues(u))
                if (!v.Equals(current))
                    return v;
            return current;
        }

        if (u == typeof(bool)) return !(bool)(current ?? false);
        if (u == typeof(int)) return (int)(current ?? 0) + 1;
        if (u == typeof(long)) return (long)(current ?? 0L) + 1L;
        if (u == typeof(double)) return (double)(current ?? 0d) + 1d;
        if (u == typeof(float)) return (float)(current ?? 0f) + 1f;
        if (u == typeof(string)) return (current as string ?? "") + "_probe";

        return current;
    }
}
