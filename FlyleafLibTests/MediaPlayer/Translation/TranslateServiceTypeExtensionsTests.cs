using System;
using System.Numerics;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib;

/// <summary>
/// Unit tests for <see cref="TranslateServiceTypeExtensions"/> — the pure classification/factory helpers
/// over the <see cref="TranslateServiceType"/> [Flags] enum. These pin the LLM/non-LLM split that the AI
/// insight / context-window logic depends on, and the enum→settings mapping used by the service factory.
/// </summary>
public class TranslateServiceTypeExtensionsTests
{
    // --- IsLLM ------------------------------------------------------------------------------------

    [Theory]
    // LLM providers (the seven OpenAI-compatible / local-LLM services).
    [InlineData(TranslateServiceType.Ollama)]
    [InlineData(TranslateServiceType.LMStudio)]
    [InlineData(TranslateServiceType.KoboldCpp)]
    [InlineData(TranslateServiceType.OpenAI)]
    [InlineData(TranslateServiceType.OpenAILike)]
    [InlineData(TranslateServiceType.Claude)]
    [InlineData(TranslateServiceType.LiteLLM)]
    public void IsLLM_True_ForLlmProviders(TranslateServiceType serviceType)
    {
        serviceType.IsLLM().Should().BeTrue();
    }

    [Theory]
    // Classic machine-translation providers — not LLMs.
    [InlineData(TranslateServiceType.GoogleV1)]
    [InlineData(TranslateServiceType.Bing)]
    [InlineData(TranslateServiceType.Azure)]
    [InlineData(TranslateServiceType.DeepL)]
    [InlineData(TranslateServiceType.DeepLX)]
    public void IsLLM_False_ForNonLlmProviders(TranslateServiceType serviceType)
    {
        serviceType.IsLLM().Should().BeFalse();
    }

    [Theory]
    [InlineData(TranslateServiceType.Ollama | TranslateServiceType.LMStudio, true)]   // all constituents are LLM
    [InlineData(TranslateServiceType.Ollama | TranslateServiceType.GoogleV1, false)]  // one non-LLM constituent
    [InlineData(TranslateServiceType.GoogleV1 | TranslateServiceType.DeepL, false)]   // no LLM constituent
    public void IsLLM_OnCombinedFlags_RequiresEveryConstituentToBeLlm(TranslateServiceType combined, bool expected)
    {
        // IsLLM delegates to LLMServices.HasFlag, which is true only when EVERY bit of the argument is set —
        // so a combined value counts as "LLM" only if all of its constituent providers are LLM providers.
        combined.IsLLM().Should().Be(expected);
    }

    // --- LLMServices -----------------------------------------------------------------------------

    [Fact]
    public void LLMServices_ContainsExactlySevenProviders()
    {
        // Cardinality tripwire: if the LLM set ever gains or loses a provider, this fails and forces the
        // explicit membership tests below to be updated too. It does not, on its own, prove WHICH providers
        // are in the set — LLMServices_IncludesEveryLlmProvider / _ExcludesMachineTranslationProviders do that.
        BitOperations.PopCount((uint)TranslateServiceTypeExtensions.LLMServices).Should().Be(7);
    }

    [Fact]
    public void LLMServices_IncludesEveryLlmProvider()
    {
        TranslateServiceType llm = TranslateServiceTypeExtensions.LLMServices;

        llm.HasFlag(TranslateServiceType.Ollama).Should().BeTrue();
        llm.HasFlag(TranslateServiceType.LMStudio).Should().BeTrue();
        llm.HasFlag(TranslateServiceType.KoboldCpp).Should().BeTrue();
        llm.HasFlag(TranslateServiceType.OpenAI).Should().BeTrue();
        llm.HasFlag(TranslateServiceType.OpenAILike).Should().BeTrue();
        llm.HasFlag(TranslateServiceType.Claude).Should().BeTrue();
        llm.HasFlag(TranslateServiceType.LiteLLM).Should().BeTrue();
    }

    [Fact]
    public void LLMServices_ExcludesMachineTranslationProviders()
    {
        TranslateServiceType llm = TranslateServiceTypeExtensions.LLMServices;

        llm.HasFlag(TranslateServiceType.GoogleV1).Should().BeFalse();
        llm.HasFlag(TranslateServiceType.Bing).Should().BeFalse();
        llm.HasFlag(TranslateServiceType.Azure).Should().BeFalse();
        llm.HasFlag(TranslateServiceType.DeepL).Should().BeFalse();
        llm.HasFlag(TranslateServiceType.DeepLX).Should().BeFalse();
    }

    // --- DefaultSettings -------------------------------------------------------------------------

    [Theory]
    [InlineData(TranslateServiceType.GoogleV1, typeof(GoogleV1TranslateSettings))]
    [InlineData(TranslateServiceType.Bing, typeof(BingTranslateSettings))]
    [InlineData(TranslateServiceType.Azure, typeof(AzureTranslateSettings))]
    [InlineData(TranslateServiceType.DeepL, typeof(DeepLTranslateSettings))]
    [InlineData(TranslateServiceType.DeepLX, typeof(DeepLXTranslateSettings))]
    [InlineData(TranslateServiceType.Ollama, typeof(OllamaTranslateSettings))]
    [InlineData(TranslateServiceType.LMStudio, typeof(LMStudioTranslateSettings))]
    [InlineData(TranslateServiceType.KoboldCpp, typeof(KoboldCppTranslateSettings))]
    [InlineData(TranslateServiceType.OpenAI, typeof(OpenAITranslateSettings))]
    [InlineData(TranslateServiceType.OpenAILike, typeof(OpenAILikeTranslateSettings))]
    [InlineData(TranslateServiceType.Claude, typeof(ClaudeTranslateSettings))]
    [InlineData(TranslateServiceType.LiteLLM, typeof(LiteLLMTranslateSettings))]
    public void DefaultSettings_ReturnsConcreteSettings_PerServiceType(TranslateServiceType serviceType, Type expected)
    {
        serviceType.DefaultSettings().Should().BeOfType(expected);
    }

    [Theory]
    [InlineData(0)]              // no bits set
    [InlineData(9999)]           // mixed defined+undefined bits
    [InlineData(int.MaxValue)]   // every bit set → no single case matches
    public void DefaultSettings_Throws_OnUndefinedServiceType(int raw)
    {
        Action act = () => ((TranslateServiceType)raw).DefaultSettings();
        act.Should().Throw<InvalidOperationException>();
    }
}
