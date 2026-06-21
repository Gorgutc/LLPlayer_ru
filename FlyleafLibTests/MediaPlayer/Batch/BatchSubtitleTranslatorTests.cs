using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer;

public class BatchSubtitleTranslatorTests
{
    [Fact]
    public void CreateSubtitlesConfig_ShouldSnapshotMutableSettingsAndKeepLiveConfigUnchanged()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.WhisperConfig.Language = "ja";
        config.Subtitles.WhisperConfig.Translate = true;
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;
        config.Subtitles.TranslateTargetLanguage = TargetLanguage.EnglishAmerican;
        config.Subtitles.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.OneByOne;
        config.Subtitles.TranslateServiceSettings[TranslateServiceType.Ollama] =
            new OllamaTranslateSettings
            {
                Endpoint = "http://127.0.0.1:11434",
                Model = "llama3"
            };

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.Should().NotBeSameAs(config.Subtitles);
        snapshot.WhisperConfig.Should().NotBeSameAs(config.Subtitles.WhisperConfig);
        snapshot.WhisperConfig.Translate.Should().BeFalse();
        config.Subtitles.WhisperConfig.Translate.Should().BeTrue();
        snapshot.TranslateTargetLanguage.Should().Be(TargetLanguage.Russian);
        config.Subtitles.TranslateTargetLanguage.Should().Be(TargetLanguage.EnglishAmerican);
        snapshot.TranslateChatConfig.Should().NotBeSameAs(config.Subtitles.TranslateChatConfig);
        snapshot.TranslateServiceSettings[TranslateServiceType.Ollama]
            .Should()
            .NotBeSameAs(config.Subtitles.TranslateServiceSettings[TranslateServiceType.Ollama]);

        snapshot.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.KeepContext;
        ((OllamaTranslateSettings)snapshot.TranslateServiceSettings[TranslateServiceType.Ollama]).Model = "changed";

        config.Subtitles.TranslateChatConfig.TranslateMethod.Should().Be(ChatTranslateMethod.OneByOne);
        ((OllamaTranslateSettings)config.Subtitles.TranslateServiceSettings[TranslateServiceType.Ollama])
            .Model
            .Should()
            .Be("llama3");
    }

    [Fact]
    public void Create_ShouldSnapshotAudioLanguagesAndRemoveFasterWhisperTaskArgument()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        config.Audio.Languages = [Language.Get("ja")];
        config.Subtitles.FasterWhisperConfig.ExtraArguments =
            "--device cpu --task translate --vad_filter True --task=translate";

        Config snapshot = BatchSubtitleConfigSnapshot.Create(config);

        snapshot.Audio.Languages.Should().NotBeSameAs(config.Audio.Languages);
        snapshot.Audio.Languages.Select(l => l.ISO6391).Should().Equal("ja");
        snapshot.Subtitles.FasterWhisperConfig.ExtraArguments.Should().Be("--device cpu --vad_filter True");

        snapshot.Audio.Languages.Clear();

        config.Audio.Languages.Select(l => l.ISO6391).Should().Equal("ja");
    }

    [Fact]
    public async Task TranslateAsync_ShouldForceSequentialRequestsForLlmKeepContext()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;
        config.Subtitles.TranslateMaxConcurrency = 4;
        config.Subtitles.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.KeepContext;

        var service = new ConcurrentCountingTranslateService(TranslateServiceType.Ollama);
        BatchSubtitleTranslator translator = new(config.Subtitles, () => service);

        List<SubtitleData> subtitles =
        [
            CreateSub("one"),
            CreateSub("two"),
            CreateSub("three")
        ];

        await translator.TranslateAsync(subtitles, Language.English, CancellationToken.None);

        service.MaxConcurrent.Should().Be(1);
        subtitles.Select(s => s.TranslatedText).Should().Equal("ru:one", "ru:two", "ru:three");
    }

    [Fact]
    public async Task TranslateAsync_ShouldRejectUnknownSourceLanguage()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        BatchSubtitleTranslator translator = new(config.Subtitles, () => new ConcurrentCountingTranslateService(TranslateServiceType.GoogleV1));

        Func<Task> act = async () => await translator.TranslateAsync(
            [CreateSub("text")],
            Language.Unknown,
            CancellationToken.None);

        await act.Should().ThrowAsync<TranslationConfigException>()
            .WithMessage("*unknown*");
    }

    private static SubtitleData CreateSub(string text) => new()
    {
        Text = text,
        StartTime = TimeSpan.Zero,
        EndTime = TimeSpan.FromSeconds(1)
    };

    private sealed class ConcurrentCountingTranslateService(TranslateServiceType serviceType) : ITranslateService
    {
        private int _current;
        public int MaxConcurrent { get; private set; }
        public TranslateServiceType ServiceType { get; } = serviceType;

        public void Initialize(Language src, TargetLanguage target) { }

        public async Task<string> TranslateAsync(string text, CancellationToken token)
        {
            int current = Interlocked.Increment(ref _current);
            MaxConcurrent = Math.Max(MaxConcurrent, current);
            try
            {
                await Task.Delay(20, token);
                return "ru:" + text;
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        public void Dispose() { }
    }
}
