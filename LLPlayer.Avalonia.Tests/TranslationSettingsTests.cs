using FlyleafLib;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class TranslationSettingsTests
{
    [Theory]
    [InlineData(-1, 6, 6)]
    [InlineData(6, -1, 6)]
    [InlineData(6, 6, -1)]
    public void Negative_chat_context_cannot_be_saved_or_applied(int count, int before, int after)
    {
        using TempDir dir = new();
        var settings = new TranslationSettings();
        settings.Chat.SubtitleContextCount = count;
        settings.Chat.ContextWindowBefore = before;
        settings.Chat.ContextWindowAfter = after;
        var config = new Config(true);
        var original = config.Subtitles.TranslateChatConfig;
        Assert.Throws<ArgumentException>(() => new TranslationSettingsStore(Path.Combine(dir.Path, "translation.json")).Save(settings));
        Assert.Throws<ArgumentException>(() => settings.Apply(config.Subtitles));
        Assert.Same(original, config.Subtitles.TranslateChatConfig);
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Existing_files_default_sentence_off_and_independent_provider_choices_round_trip()
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "translation.json");
        File.WriteAllText(path, "{\"WordProvider\":\"Bing\"}");
        var store = new TranslationSettingsStore(path);
        var old = store.Load("GoogleV1", "Russian");
        Assert.Equal("Off", old.SentenceProvider); Assert.Equal("Bing", old.WordProvider);
        old.SentenceProvider = "OpenAILike"; old.TargetLanguage = TargetLanguage.Russian;
        old.Providers[TranslateServiceType.OpenAILike] = new OpenAILikeTranslateSettings { Endpoint = "https://example.invalid/subtitles" };
        store.Save(old);
        var loaded = store.Load("Off", null);
        Assert.Equal("Bing", loaded.WordProvider); Assert.Equal("OpenAILike", loaded.SentenceProvider);
        Config config = new(true); loaded.Apply(config.Subtitles);
        Assert.Equal(TranslateServiceType.Bing, config.Subtitles.TranslateWordServiceType);
        Assert.Equal(TranslateServiceType.OpenAILike, config.Subtitles.TranslateServiceType);
        Assert.False(config.Subtitles[0].EnabledTranslated); Assert.False(config.Subtitles[1].EnabledTranslated);
    }

    [Fact]
    public void All_concrete_settings_and_unexposed_parameters_round_trip_detached()
    {
        using TempDir dir = new();
        var store = new TranslationSettingsStore(Path.Combine(dir.Path, "LLPlayer.Translation.json"));
        var settings = new TranslationSettings { WordProvider = "OpenAILike", TargetLanguage = TargetLanguage.Russian };
        foreach (var type in Enum.GetValues<TranslateServiceType>()) settings.Providers[type] = type.DefaultSettings();
        var local = (OpenAILikeTranslateSettings)settings.Providers[TranslateServiceType.OpenAILike];
        local.ApiKey = "fake-secret"; local.Model = "fake-model"; local.ChatPath = "/custom/chat";
        local.Temperature = 0.37; local.TemperatureManual = true; local.MaxCompletionTokens = 1234;
        ((AzureTranslateSettings)settings.Providers[TranslateServiceType.Azure]).Region = "fake-region";
        ((GoogleV1TranslateSettings)settings.Providers[TranslateServiceType.GoogleV1]).Regions["en"] = "custom";
        settings.Chat.PromptOneByOne = "custom prompt survives";
        var copy = settings.Clone();
        ((OpenAILikeTranslateSettings)copy.Providers[TranslateServiceType.OpenAILike]).ApiKey = "fake-changed";
        Assert.Equal("fake-secret", local.ApiKey);
        store.Save(settings);
        var loaded = store.Load("Off", "EnglishAmerican");
        Assert.Equal("OpenAILike", loaded.WordProvider);
        Assert.Equal(TargetLanguage.Russian, loaded.TargetLanguage);
        foreach (var (type, value) in settings.Providers) Assert.Equal(value.GetType(), loaded.Providers[type].GetType());
        var roundTrip = (OpenAILikeTranslateSettings)loaded.Providers[TranslateServiceType.OpenAILike];
        Assert.Equal("fake-secret", roundTrip.ApiKey); Assert.Equal("fake-model", roundTrip.Model);
        Assert.Equal("/custom/chat", roundTrip.ChatPath); Assert.Equal(0.37, roundTrip.Temperature);
        Assert.True(roundTrip.TemperatureManual); Assert.Equal(1234, roundTrip.MaxCompletionTokens);
        Assert.Equal("custom prompt survives", loaded.Chat.PromptOneByOne);
        Assert.Equal("fake-region", ((AzureTranslateSettings)loaded.Providers[TranslateServiceType.Azure]).Region);
        Assert.Equal("custom", ((GoogleV1TranslateSettings)loaded.Providers[TranslateServiceType.GoogleV1]).Regions["en"]);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }
    [Fact]
    public void Legacy_seed_is_only_used_for_absent_file_and_never_autosaved()
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "LLPlayer.Translation.json");
        var store = new TranslationSettingsStore(path);
        var seeded = store.Load("Off", "Russian");
        Assert.Equal("Off", seeded.WordProvider); Assert.Equal(TargetLanguage.Russian, seeded.TargetLanguage);
        Assert.False(File.Exists(path));
        store.Save(new() { WordProvider = "Bing", TargetLanguage = TargetLanguage.EnglishAmerican });
        var loaded = store.Load("Off", "Russian");
        Assert.Equal("Bing", loaded.WordProvider); Assert.Equal(TargetLanguage.EnglishAmerican, loaded.TargetLanguage);
    }
    [Theory]
    [InlineData("not-json-fake-secret")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("{\"Providers\":{\"GoogleV1\":[]}}")]
    [InlineData("{\"Providers\":{\"GoogleV1\":123}}")]
    [InlineData("{\"Providers\":{\"GoogleV1\":{\"TypeName\":123}}}")]
    [InlineData("{\"WordProvider\":\"Unknown\"}")]
    [InlineData("{\"SentenceProvider\":\"Unknown\"}")]
    [InlineData("{\"Providers\":null}")]
    [InlineData("{\"Chat\":null}")]
    [InlineData("{\"Chat\":{\"TranslateMethod\":999}}")]
    [InlineData("{\"Chat\":{\"ContextRetainPolicy\":999}}")]
    [InlineData("{\"Providers\":{\"GoogleV1\":{\"TypeName\":\"GoogleV1TranslateSettings\",\"Regions\":null}}}")]
    [InlineData("{\"Providers\":{\"Bing\":{\"TypeName\":\"BingTranslateSettings\",\"Regions\":null}}}")]
    [InlineData("{\"Providers\":{\"GoogleV1\":{\"TypeName\":\"GoogleV1TranslateSettings\",\"Endpoint\":\"relative/fake-secret\"}}}")]
    [InlineData("{\"TargetLanguage\":99999}")]
    [InlineData("{\"Providers\":{\"Azure\":{\"TypeName\":\"GoogleV1TranslateSettings\"}}}")]
    [InlineData("{\"Providers\":{\"GoogleV1\":{\"TypeName\":\"GoogleV1TranslateSettings\",\"TimeoutMs\":0}}}")]
    public void Invalid_input_is_preserved_and_diagnostics_do_not_echo_input(string json)
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "LLPlayer.Translation.json");
        File.WriteAllText(path, json);
        var store = new TranslationSettingsStore(path);
        Assert.Equal("GoogleV1", store.Load("Off", "Russian").WordProvider);
        Assert.NotNull(store.LoadWarning); Assert.DoesNotContain("fake-secret", store.LoadWarning);
        Assert.Equal(json, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(dir.Path));
    }
    [Theory]
    [InlineData("off", "Off")]
    [InlineData("lmstudio", "LMStudio")]
    public void Legacy_provider_case_is_canonicalized(string legacy, string expected)
    {
        using TempDir dir = new();
        var store = new TranslationSettingsStore(Path.Combine(dir.Path, "translation.json"));
        var seeded = store.Load(legacy, "russian");
        Assert.Equal(expected, seeded.WordProvider);
        Assert.Equal(TargetLanguage.Russian, seeded.TargetLanguage);
    }
    [Fact]
    public void Directory_at_file_path_reports_warning_without_legacy_seed()
    {
        using TempDir dir = new();
        var store = new TranslationSettingsStore(dir.Path);
        Assert.Equal("GoogleV1", store.Load("Off", null).WordProvider);
        Assert.NotNull(store.LoadWarning);
    }
}
