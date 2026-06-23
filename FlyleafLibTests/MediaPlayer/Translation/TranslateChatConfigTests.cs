using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib;

public class TranslateChatConfigTests
{
    [Fact]
    public void Default_TranslateMethod_IsKeepContext()
    {
        new TranslateChatConfig().TranslateMethod.Should().Be(ChatTranslateMethod.KeepContext);
    }

    [Fact]
    public void Default_SubtitleContextCount_IsSix()
    {
        new TranslateChatConfig().SubtitleContextCount.Should().Be(6);
    }

    [Fact]
    public void OneByOnePrompt_ContainsAllRequiredPlaceholders()
    {
        TranslateChatConfig.DefaultPromptOneByOne.Should().Contain("{source_lang}");
        TranslateChatConfig.DefaultPromptOneByOne.Should().Contain("{target_lang}");
        TranslateChatConfig.DefaultPromptOneByOne.Should().Contain("{source_text}");
    }

    [Fact]
    public void KeepContextPrompt_HasLangPlaceholders_ButNotSourceText()
    {
        // KeepContext sends each line as a separate user message, so {source_text} must NOT appear (it would
        // leak unsubstituted into the system prompt).
        TranslateChatConfig.DefaultPromptKeepContext.Should().Contain("{source_lang}");
        TranslateChatConfig.DefaultPromptKeepContext.Should().Contain("{target_lang}");
        TranslateChatConfig.DefaultPromptKeepContext.Should().NotContain("{source_text}");
    }

    [Theory]
    [InlineData(TranslateChatConfig.DefaultPromptOneByOne)]
    [InlineData(TranslateChatConfig.DefaultPromptKeepContext)]
    public void BothPrompts_CarryAccuracyInstructions(string prompt)
    {
        prompt.Should().Contain("Output ONLY");      // no source text / notes leakage
        prompt.Should().Contain("MEANING");          // translate meaning, not word for word
        prompt.Should().Contain("tone");             // preserve tone/register
    }

    [Fact]
    public void NewDefaults_DifferFromLegacyDefaults()
    {
        TranslateChatConfig.DefaultPromptOneByOne.Should().NotBe(TranslateChatConfig.LegacyDefaultPromptOneByOne);
        TranslateChatConfig.DefaultPromptKeepContext.Should().NotBe(TranslateChatConfig.LegacyDefaultPromptKeepContext);
    }

    [Fact]
    public void PromptProperties_NormalizeLineEndingsToLf()
    {
        TranslateChatConfig config = new();
        config.PromptOneByOne.Should().NotContain("\r");
        config.PromptKeepContext.Should().NotContain("\r");
    }
}
