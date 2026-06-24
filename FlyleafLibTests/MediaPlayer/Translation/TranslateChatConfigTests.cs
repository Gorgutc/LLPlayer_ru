using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib;

public class TranslateChatConfigTests
{
    [Fact]
    public void Default_TranslateMethod_IsContextWindow()
    {
        // 0.3.6 default: ContextWindow (surrounding-line context, one line of output). Existing KeepContext
        // configs are migrated once in Config.UpdateDefault.
        new TranslateChatConfig().TranslateMethod.Should().Be(ChatTranslateMethod.ContextWindow);
    }

    [Fact]
    public void Default_SubtitleContextCount_IsSix()
    {
        new TranslateChatConfig().SubtitleContextCount.Should().Be(6);
    }

    [Fact]
    public void Default_ContextWindow_IsSixBeforeAndAfter()
    {
        TranslateChatConfig config = new();
        config.ContextWindowBefore.Should().Be(6);
        config.ContextWindowAfter.Should().Be(6);
    }

    [Fact]
    public void Default_GrammarCheck_IsEnabled()
    {
        new TranslateChatConfig().GrammarCheckEnabled.Should().BeTrue();
    }

    [Fact]
    public void OneByOnePrompt_ContainsAllRequiredPlaceholders()
    {
        TranslateChatConfig.DefaultPromptOneByOne.Should().Contain("{source_lang}");
        TranslateChatConfig.DefaultPromptOneByOne.Should().Contain("{target_lang}");
        TranslateChatConfig.DefaultPromptOneByOne.Should().Contain("{source_text}");
    }

    [Theory]
    // These prompts send each line as a separate user message (or build the window in code), so {source_text}
    // must NOT appear (it would leak unsubstituted into the system prompt).
    [InlineData(TranslateChatConfig.DefaultPromptKeepContext)]
    [InlineData(TranslateChatConfig.DefaultPromptContextWindow)]
    public void NonTemplatedPrompts_HaveLangPlaceholders_ButNotSourceText(string prompt)
    {
        prompt.Should().Contain("{source_lang}");
        prompt.Should().Contain("{target_lang}");
        prompt.Should().NotContain("{source_text}");
    }

    [Theory]
    [InlineData(TranslateChatConfig.DefaultPromptOneByOne)]
    [InlineData(TranslateChatConfig.DefaultPromptKeepContext)]
    [InlineData(TranslateChatConfig.DefaultPromptContextWindow)]
    public void TranslationPrompts_CarryAccuracyInstructions(string prompt)
    {
        prompt.Should().Contain("Output ONLY");      // no source text / notes leakage
        prompt.Should().Contain("MEANING");          // translate meaning, not word for word
        prompt.Should().Contain("tone");             // preserve tone/register
    }

    [Fact]
    public void ContextWindowPrompt_InstructsTranslatingOnlyTheFocalLine()
    {
        // The whole point of ContextWindow: surrounding lines are context only, exactly one line is output, and
        // the model must use the inflected form the context requires (the bug this fixes).
        string prompt = TranslateChatConfig.DefaultPromptContextWindow;
        prompt.Should().Contain("Line to translate");
        prompt.Should().Contain("Context before");
        prompt.Should().Contain("Context after");
        prompt.Should().Contain("ONLY the \"Line to translate\"");
    }

    [Fact]
    public void GrammarCheckPrompt_HasLangPlaceholders_AndProofreadIntent()
    {
        string prompt = TranslateChatConfig.DefaultPromptGrammarCheck;
        prompt.Should().Contain("{source_lang}");
        prompt.Should().Contain("{target_lang}");
        prompt.Should().NotContain("{source_text}");
        prompt.Should().Contain("Output ONLY");
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
        config.PromptContextWindow.Should().NotContain("\r");
        config.PromptGrammarCheck.Should().NotContain("\r");
    }
}
