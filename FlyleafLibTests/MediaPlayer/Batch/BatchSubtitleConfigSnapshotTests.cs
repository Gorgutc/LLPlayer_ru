using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation;

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
}
