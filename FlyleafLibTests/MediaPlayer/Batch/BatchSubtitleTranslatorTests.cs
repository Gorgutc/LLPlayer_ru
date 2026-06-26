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
        config.Subtitles.TranslateChatConfig.PromptContextWindow = "custom context prompt";
        config.Subtitles.TranslateChatConfig.PromptGrammarCheck = "custom grammar prompt";
        config.Subtitles.TranslateChatConfig.ContextWindowBefore = 0;
        config.Subtitles.TranslateChatConfig.ContextWindowAfter = 2;
        config.Subtitles.TranslateChatConfig.GrammarCheckEnabled = false;
        config.Subtitles.FasterWhisperConfig.AntiHallucination = false;
        config.Subtitles.FasterWhisperConfig.Prompt = "ru sample prompt";
        config.Subtitles.FixAllCaps = false;
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
        snapshot.FasterWhisperConfig.AntiHallucination.Should().BeFalse();
        snapshot.FasterWhisperConfig.Prompt.Should().Be("ru sample prompt");
        snapshot.FixAllCaps.Should().BeFalse();
        snapshot.TranslateChatConfig.Should().NotBeSameAs(config.Subtitles.TranslateChatConfig);
        snapshot.TranslateChatConfig.PromptContextWindow.Should().Be("custom context prompt");
        snapshot.TranslateChatConfig.PromptGrammarCheck.Should().Be("custom grammar prompt");
        snapshot.TranslateChatConfig.ContextWindowBefore.Should().Be(0);
        snapshot.TranslateChatConfig.ContextWindowAfter.Should().Be(2);
        snapshot.TranslateChatConfig.GrammarCheckEnabled.Should().BeFalse();
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
    public async Task TranslateAsync_ShouldForceSequentialRequestsForLlmContextWindow()
    {
        // ContextWindow is stateless but is deliberately collapsed to one concurrent request for LLM providers
        // (so a single local model is not hammered, and the optional grammar pass does not double the parallel
        // load). This pins that guard, which the KeepContext test does not exercise.
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;
        config.Subtitles.TranslateMaxConcurrency = 4;
        config.Subtitles.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.ContextWindow;

        // Counts concurrency via the string overload; the context overload falls through to it by default.
        var service = new ConcurrentCountingTranslateService(TranslateServiceType.Ollama);
        BatchSubtitleTranslator translator = new(config.Subtitles, () => service);

        List<SubtitleData> subtitles = [CreateSub("one"), CreateSub("two"), CreateSub("three")];

        await translator.TranslateAsync(subtitles, Language.English, CancellationToken.None);

        service.MaxConcurrent.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_ContextWindow_PassesSurroundingSourceLinesAsContext()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;
        config.Subtitles.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.ContextWindow;
        config.Subtitles.TranslateChatConfig.ContextWindowBefore = 2;
        config.Subtitles.TranslateChatConfig.ContextWindowAfter = 1;
        config.Subtitles.ResegmentSubtitles = false;

        var service = new CapturingContextTranslateService(TranslateServiceType.Ollama);
        BatchSubtitleTranslator translator = new(config.Subtitles, () => service);

        List<SubtitleData> subtitles = [CreateSub("l0"), CreateSub("l1"), CreateSub("l2"), CreateSub("l3")];

        await translator.TranslateAsync(subtitles, Language.English, CancellationToken.None);

        // Focal "l2" (index 2): before window 2 → [l0, l1]; after window 1 → [l3].
        SubtitleTranslationContext ctx = service.Captured.Single(c => c.Text == "l2");
        ctx.Before.Should().Equal("l0", "l1");
        ctx.After.Should().Equal("l3");
        subtitles.Select(s => s.TranslatedText).Should().Equal("ru:l0", "ru:l1", "ru:l2", "ru:l3");
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

    [Theory]
    // A per-line CONTENT failure (the server responded, but this one reply is unusable) must fall back to the
    // source text for that single line and let the run continue — never fail the whole file. This is the exact
    // Clarkson's Farm repro for Degenerate, plus the other recoverable content kinds.
    [InlineData(TranslationFailureKind.Degenerate)]
    [InlineData(TranslationFailureKind.Truncated)]
    [InlineData(TranslationFailureKind.EmptyResponse)]
    [InlineData(TranslationFailureKind.NullContent)]
    public async Task TranslateAsync_ShouldKeepSourceForRecoverableContentFailureAndContinue(TranslationFailureKind kind)
    {
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;
        config.Subtitles.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.KeepContext;

        var service = new ScriptedTranslateService(TranslateServiceType.Ollama, text =>
            text == "bad"
                ? throw new TranslationException("content failure") { Kind = kind }
                : "ru:" + text);
        BatchSubtitleTranslator translator = new(config.Subtitles, () => service);

        List<SubtitleData> subtitles = [CreateSub("one"), CreateSub("bad"), CreateSub("three")];

        await translator.TranslateAsync(subtitles, Language.English, CancellationToken.None);

        // The bad line keeps no translation (source text is shown by the writer); the others translate fine and
        // the file is NOT failed.
        subtitles[0].TranslatedText.Should().Be("ru:one");
        subtitles[1].TranslatedText.Should().BeNull();
        subtitles[2].TranslatedText.Should().Be("ru:three");
    }

    [Fact]
    public async Task TranslateAsync_ShouldPropagateGenericFailureSoTheFileFails()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;
        config.Subtitles.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.KeepContext;

        // Default Kind is Generic — this is what SendChatRequest throws on a dead/unreachable server.
        var service = new ScriptedTranslateService(TranslateServiceType.Ollama,
            _ => throw new TranslationException("Cannot request to Ollama"));
        BatchSubtitleTranslator translator = new(config.Subtitles, () => service);

        Func<Task> act = () => translator.TranslateAsync(
            [CreateSub("one"), CreateSub("two")], Language.English, CancellationToken.None);

        // A dead server must fail the file, not silently emit an all-source ".ru.srt" marked Completed.
        await act.Should().ThrowAsync<TranslationException>()
            .Where(e => e.Kind == TranslationFailureKind.Generic);
    }

    [Fact]
    public async Task TranslateAsync_ShouldPropagateConfigFailure()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;
        config.Subtitles.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.KeepContext;

        var service = new ScriptedTranslateService(TranslateServiceType.Ollama,
            _ => throw new TranslationConfigException("target language not supported"));
        BatchSubtitleTranslator translator = new(config.Subtitles, () => service);

        Func<Task> act = () => translator.TranslateAsync(
            [CreateSub("one")], Language.English, CancellationToken.None);

        await act.Should().ThrowAsync<TranslationConfigException>();
    }

    [Fact]
    public async Task TranslateAsync_ShouldNotSwallowCancellation()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;
        config.Subtitles.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.KeepContext;

        var service = new ScriptedTranslateService(TranslateServiceType.Ollama,
            _ => throw new OperationCanceledException());
        BatchSubtitleTranslator translator = new(config.Subtitles, () => service);

        Func<Task> act = () => translator.TranslateAsync(
            [CreateSub("one")], Language.English, CancellationToken.None);

        // OperationCanceledException is not a TranslationException, so the content-failure filter must not catch
        // it; cancellation must stop the batch (mapped to Canceled by the processor), not look like success.
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TranslateAsync_ShouldKeepSourceForWhitespaceOnlyReply()
    {
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;
        config.Subtitles.TranslateChatConfig.TranslateMethod = ChatTranslateMethod.KeepContext;

        var service = new ScriptedTranslateService(TranslateServiceType.Ollama,
            text => text == "blank" ? "   \n\t  " : "ru:" + text);
        BatchSubtitleTranslator translator = new(config.Subtitles, () => service);

        List<SubtitleData> subtitles = [CreateSub("ok"), CreateSub("blank")];

        await translator.TranslateAsync(subtitles, Language.English, CancellationToken.None);

        subtitles[0].TranslatedText.Should().Be("ru:ok");
        subtitles[1].TranslatedText.Should().BeNull();
    }

    [Fact]
    public async Task TranslateAsync_ShouldSkipTranslationWhenSourceIsRussian()
    {
        // Feature: a Russian audio track is transcribed to Russian subtitles, so no translation is needed.
        Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Ollama;

        var service = new ScriptedTranslateService(TranslateServiceType.Ollama,
            _ => throw new InvalidOperationException("translation must not be invoked for a Russian source"));
        BatchSubtitleTranslator translator = new(config.Subtitles, () => service);

        List<SubtitleData> subtitles = [CreateSub("привет"), CreateSub("как дела")];

        await translator.TranslateAsync(subtitles, Language.Get("ru"), CancellationToken.None);

        subtitles.Should().OnlyContain(s => s.TranslatedText == null);
    }

    private static SubtitleData CreateSub(string text) => new()
    {
        Text = text,
        StartTime = TimeSpan.Zero,
        EndTime = TimeSpan.FromSeconds(1)
    };

    // A translate service whose per-line behaviour is scripted: the callback returns the translation or throws
    // (TranslationException / TranslationConfigException / OperationCanceledException) to simulate failures.
    private sealed class ScriptedTranslateService(TranslateServiceType serviceType, Func<string, string> translate)
        : ITranslateService
    {
        public TranslateServiceType ServiceType { get; } = serviceType;

        public void Initialize(Language src, TargetLanguage target) { }

        public async Task<string> TranslateAsync(string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            return translate(text);
        }

        public void Dispose() { }
    }

    // Captures the SubtitleTranslationContext passed to the context-aware overload so a test can assert the
    // window the translator built (which surrounding lines, in which order).
    private sealed class CapturingContextTranslateService(TranslateServiceType serviceType) : ITranslateService
    {
        public TranslateServiceType ServiceType { get; } = serviceType;
        public List<SubtitleTranslationContext> Captured { get; } = new();

        public void Initialize(Language src, TargetLanguage target) { }

        public Task<string> TranslateAsync(string text, CancellationToken token) => Task.FromResult("ru:" + text);

        public Task<string> TranslateAsync(SubtitleTranslationContext context, CancellationToken token)
        {
            Captured.Add(context);
            return Task.FromResult("ru:" + context.Text);
        }

        public void Dispose() { }
    }

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
