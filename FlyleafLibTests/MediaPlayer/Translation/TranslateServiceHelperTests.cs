using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.Translation;

// TranslateServiceHelper.TryGetLanguage (extension on ITranslateService) validates a source Language + a
// TargetLanguage against the service's supported set before a batch/interactive translation starts. It is pure
// branch logic: (a) unknown source; (b) same language unless Chinese Simplified<->Traditional; (c) source ISO not
// in TranslateLanguage.Langs; (d) source unsupported by the service; (e) target ISO not in Langs (unreachable —
// every TargetLanguage code exists); (f) target unsupported by the service; else return both langs. Only
// service.ServiceType is read, so a minimal stub suffices. Language.Get("en"/"ru"/"zh"/"de"/"wo") resolve as
// cultures; default languages support GoogleV1|DeepL|Bing (+ every LLM), Wolof is DeepL-only.
public class TranslateServiceHelperTests
{
    private sealed class StubService(TranslateServiceType serviceType) : ITranslateService
    {
        public TranslateServiceType ServiceType { get; } = serviceType;
        public void Initialize(Language src, TargetLanguage target) { }
        public Task<string> TranslateAsync(string text, CancellationToken token) => Task.FromResult(text);
        public void Dispose() { }
    }

    private static ITranslateService Service(TranslateServiceType type) => new StubService(type);

    [Fact]
    public void UnknownSource_Throws() // branch (a)
    {
        FluentActions.Invoking(() => Service(TranslateServiceType.GoogleV1)
                .TryGetLanguage(Language.Unknown, TargetLanguage.EnglishAmerican))
            .Should().Throw<TranslationConfigException>().WithMessage("*unknown*");
    }

    [Fact]
    public void SameLanguageNonChinese_Throws() // branch (b) throw arm
    {
        FluentActions.Invoking(() => Service(TranslateServiceType.Ollama)
                .TryGetLanguage(Language.Get("ru"), TargetLanguage.Russian))
            .Should().Throw<TranslationConfigException>().WithMessage("*same*");
    }

    [Theory]
    [InlineData(TargetLanguage.ChineseSimplified)]
    [InlineData(TargetLanguage.ChineseTraditional)]
    public void ChineseSameIso_IsAllowed(TargetLanguage target) // branch (b) skipped for zh regions
    {
        var (srcLang, targetLang) = Service(TranslateServiceType.Ollama)
            .TryGetLanguage(Language.Get("zh"), target);
        srcLang.ISO6391.Should().Be("zh");
        targetLang.ISO6391.Should().Be("zh");
    }

    [Fact]
    public void SourceUnsupportedByService_Throws() // branch (d)
    {
        // Wolof is DeepL-only (+LLM/DeepLX); GoogleV1 is not in its supported set.
        FluentActions.Invoking(() => Service(TranslateServiceType.GoogleV1)
                .TryGetLanguage(Language.Get("wo"), TargetLanguage.EnglishAmerican))
            .Should().Throw<TranslationConfigException>().WithMessage("*not supported by GoogleV1*");
    }

    [Fact]
    public void TargetUnsupportedByService_Throws() // branch (f)
    {
        // Target Wolof is DeepL-only; source English is GoogleV1-supported, so the target check fires.
        FluentActions.Invoking(() => Service(TranslateServiceType.GoogleV1)
                .TryGetLanguage(Language.Get("en"), TargetLanguage.Wolof))
            .Should().Throw<TranslationConfigException>().WithMessage("*target language is not supported by GoogleV1*");
    }

    [Fact]
    public void ValidLlmPair_ReturnsBothLangs() // success return (LLM supports every language)
    {
        var (srcLang, targetLang) = Service(TranslateServiceType.Ollama)
            .TryGetLanguage(Language.Get("en"), TargetLanguage.Russian);
        srcLang.ISO6391.Should().Be("en");
        targetLang.ISO6391.Should().Be("ru");
    }

    [Fact]
    public void ValidNonLlmPair_ReturnsBothLangs() // success return via the support-flag path
    {
        var (srcLang, targetLang) = Service(TranslateServiceType.GoogleV1)
            .TryGetLanguage(Language.Get("en"), TargetLanguage.German);
        srcLang.ISO6391.Should().Be("en");
        targetLang.ISO6391.Should().Be("de");
    }
}
