using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

public class AiInsightServiceSelectorTests
{
    [Fact]
    public void TranslateServiceIsLLM_AndUsable_IsPreferred()
    {
        AiInsightServiceSelector.Select(
                TranslateServiceType.Ollama, TranslateServiceType.GoogleV1, [TranslateServiceType.Ollama])
            .Should().Be(TranslateServiceType.Ollama);
    }

    [Fact]
    public void TranslateNonLLM_FallsBackToWordServiceLLM()
    {
        AiInsightServiceSelector.Select(
                TranslateServiceType.GoogleV1, TranslateServiceType.LMStudio, [TranslateServiceType.LMStudio])
            .Should().Be(TranslateServiceType.LMStudio);
    }

    [Fact]
    public void BothNonLLM_UsesFirstConfiguredUsableLlm()
    {
        AiInsightServiceSelector.Select(
                TranslateServiceType.GoogleV1, TranslateServiceType.DeepL, [TranslateServiceType.OpenAI])
            .Should().Be(TranslateServiceType.OpenAI);
    }

    [Fact]
    public void TranslateLlmButNotConfigured_FallsThroughToUsableLlm()
    {
        // Ollama is selected for translation and IS an LLM, but it is not in the usable set — a configured
        // OpenAI must win rather than a selected-but-unconfigured Ollama.
        AiInsightServiceSelector.Select(
                TranslateServiceType.Ollama, TranslateServiceType.GoogleV1, [TranslateServiceType.OpenAI])
            .Should().Be(TranslateServiceType.OpenAI);
    }

    [Fact]
    public void NoUsableLlm_ReturnsNull()
    {
        AiInsightServiceSelector.Select(TranslateServiceType.GoogleV1, TranslateServiceType.Bing, [])
            .Should().BeNull();
    }

    [Fact]
    public void ConfiguredListWithNonLlmOnly_ReturnsNull()
    {
        // A non-LLM accidentally in the usable list must not be selected.
        AiInsightServiceSelector.Select(
                TranslateServiceType.GoogleV1, TranslateServiceType.Azure, [TranslateServiceType.DeepLX])
            .Should().BeNull();
    }
}
