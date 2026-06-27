using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

public class AiInsightLlmResolverTests
{
    [Fact]
    public void IsConfigured_ValidLocalEndpoint_NoModelRequired_ReturnsTrue()
    {
        // LM Studio default endpoint is a valid URI and does not require a model.
        AiInsightLlmResolver.IsConfigured(new LMStudioTranslateSettings()).Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_MalformedEndpoint_ReturnsFalse_NeverThrows()
    {
        // "127.0.0.1:8080" has no scheme, so GetHttpClient's `new Uri(Endpoint)` throws UriFormatException.
        // The probe must classify it "not configured" rather than let the exception escape resolution
        // (which would abort the resolver loop / block the dialog from opening).
        AiInsightLlmResolver.IsConfigured(new LMStudioTranslateSettings { Endpoint = "127.0.0.1:8080" })
            .Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_EmptyEndpoint_ReturnsFalse()
    {
        AiInsightLlmResolver.IsConfigured(new LMStudioTranslateSettings { Endpoint = "" })
            .Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_CloudMissingApiKey_ReturnsFalse()
    {
        // OpenAI requires an API key; without one GetHttpClient throws TranslationConfigException.
        AiInsightLlmResolver.IsConfigured(new OpenAITranslateSettings()).Should().BeFalse();
    }
}
