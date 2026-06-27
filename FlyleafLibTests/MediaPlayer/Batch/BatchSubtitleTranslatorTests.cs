using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
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
    public void CreateSubtitlesConfig_ShouldSnapshotLanguageFallbackPreferences()
    {
        // F-05-gap: the batch snapshot used to drop the per-slot language preferences and unknown-source
        // fallbacks, so batch transcription/translation silently ignored them.
        Utils.IsTesting = true;
        Config config = new(true);

        Language french = Language.Get("fra");
        Language german = Language.Get("deu");

        config.Subtitles.Languages = [french, german];
        config.Subtitles.LanguageAutoDetect = false;            // default true
        config.Subtitles.LanguageFallbackSecondarySame = false; // default true
        config.Subtitles.LanguageFallbackPrimary = french;
        config.Subtitles.LanguageFallbackSecondary = german;

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config.Subtitles);

        snapshot.LanguageAutoDetect.Should().BeFalse();
        snapshot.LanguageFallbackSecondarySame.Should().BeFalse();
        snapshot.LanguageFallbackPrimary.Should().Be(french);
        snapshot.LanguageFallbackSecondary.Should().Be(german);
        snapshot.Languages.Should().Equal(french, german);

        // The snapshot's language list must be an independent deep copy of the live config.
        snapshot.Languages.Should().NotBeSameAs(config.Subtitles.Languages);
        snapshot.Languages.Add(Language.English);
        config.Subtitles.Languages.Should().Equal(french, german);
    }

    [Fact]
    public void CreateSubtitlesConfig_ShouldCopyEveryScalarSubtitlesConfigSetting()
    {
        // Completeness guard against the recurring "batch snapshot forgot a field" bug class (the #42/#43 settings,
        // FixAllCaps, the language-fallback fields, ...). Enumerates every scalar, publicly-settable, persisted
        // (non-[JsonIgnore]) property declared on SubtitlesConfig, mutates it away from its default on the source,
        // snapshots, then asserts the snapshot carried the value over. Nested config objects (WhisperConfig,
        // FasterWhisperConfig, TranslateChatConfig, DubbingConfig) and collections are out of scope here — they
        // have dedicated clone methods and focused tests. If you add a new scalar setting, copy it in
        // CreateSubtitlesConfig (or extend the allow-list below with a documented reason).
        Utils.IsTesting = true;
        Config config = new(true);
        // Seed the language list so the LanguageFallback* getters don't lazily call GetSystemLanguages()
        // (which NREs in headless tests because OriginalCulture is unset) while we reflect over the properties.
        config.Subtitles.Languages = [Language.English];

        // Scalars the snapshot intentionally does NOT copy verbatim.
        HashSet<string> intentionallyDiverging =
        [
            nameof(Config.SubtitlesConfig.TranslateTargetLanguage) // forced to Russian for batch
        ];

        Type type = typeof(Config.SubtitlesConfig);
        Config.SubtitlesConfig source = config.Subtitles;

        List<PropertyInfo> scalarProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == type)
            .Where(p => p is { CanRead: true, CanWrite: true } && p.SetMethod!.IsPublic)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .Where(p => IsScalarSettingType(p.PropertyType))
            .ToList();

        scalarProps.Should().NotBeEmpty("the guard must actually enumerate SubtitlesConfig settings");

        foreach (PropertyInfo prop in scalarProps)
        {
            object? mutated = NextDistinctValue(prop.PropertyType, prop.GetValue(source));
            prop.SetValue(source, mutated);
        }

        Config.SubtitlesConfig snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(source);

        foreach (PropertyInfo prop in scalarProps)
        {
            if (intentionallyDiverging.Contains(prop.Name))
                continue;

            object? expected = prop.GetValue(source);
            object? actual = prop.GetValue(snapshot);
            actual.Should().Be(expected, $"the batch snapshot must copy SubtitlesConfig.{prop.Name}");
        }
    }

    private static bool IsScalarSettingType(Type t)
    {
        Type u = Nullable.GetUnderlyingType(t) ?? t;
        return u.IsEnum
            || u == typeof(bool) || u == typeof(int) || u == typeof(long)
            || u == typeof(double) || u == typeof(float) || u == typeof(string)
            || u == typeof(Language);
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

        if (u == typeof(Language))
        {
            Language french = Language.Get("fra");
            return current is Language cl && cl.Equals(french) ? Language.Get("deu") : french;
        }

        return current;
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
