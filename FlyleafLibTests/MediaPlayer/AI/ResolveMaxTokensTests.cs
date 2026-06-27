using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.Translation;

/// <summary>
/// Guards the engine edit for F-07: <see cref="OpenAIBaseTranslateService.ResolveMaxTokens"/> must reproduce the
/// prior inline token logic byte-for-byte when no override is supplied (so the translation path cannot regress),
/// and apply the override correctly (user cap as a floor; never send both fields) for AI insights.
/// LMStudio has DefaultMaxTokensFallback=2048 (local); OpenAI has null (cloud).
/// </summary>
public class ResolveMaxTokensTests
{
    private static LMStudioTranslateSettings Local() => new();
    private static OpenAITranslateSettings Cloud() => new();

    // ---- override == null : parity with the previous inline translation expression ----

    [Fact]
    public void NoOverride_Local_NeitherCap_UsesFallbackAsMaxTokens()
    {
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(Local(), antiLoop: false, maxOutputTokensOverride: null);
        mct.Should().BeNull();
        mt.Should().Be(2048);
    }

    [Fact]
    public void NoOverride_Local_AntiLoop_DoublesFallback()
    {
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(Local(), antiLoop: true, maxOutputTokensOverride: null);
        mct.Should().BeNull();
        mt.Should().Be(4096);
    }

    [Fact]
    public void NoOverride_Cloud_NeitherCap_NullFallback()
    {
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(Cloud(), antiLoop: false, maxOutputTokensOverride: null);
        mct.Should().BeNull();
        mt.Should().BeNull();
    }

    [Fact]
    public void NoOverride_Cloud_AntiLoop_StaysNull()
    {
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(Cloud(), antiLoop: true, maxOutputTokensOverride: null);
        mct.Should().BeNull();
        mt.Should().BeNull();
    }

    [Fact]
    public void NoOverride_UserMaxTokensSet_Wins()
    {
        var s = Local();
        s.MaxTokens = 500;
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(s, antiLoop: false, maxOutputTokensOverride: null);
        mct.Should().BeNull();
        mt.Should().Be(500);
    }

    [Fact]
    public void NoOverride_UserMaxCompletionTokensSet_SuppressesMaxTokens()
    {
        var s = Local();
        s.MaxCompletionTokens = 300;
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(s, antiLoop: false, maxOutputTokensOverride: null);
        mct.Should().Be(300);
        mt.Should().BeNull();
    }

    [Fact]
    public void NoOverride_BothCapsSet_BothPassedThrough()
    {
        var s = Local();
        s.MaxCompletionTokens = 300;
        s.MaxTokens = 500;
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(s, antiLoop: false, maxOutputTokensOverride: null);
        mct.Should().Be(300);
        mt.Should().Be(500);
    }

    // ---- override set : AI-insights path ----

    [Fact]
    public void Override_NoUserCap_SendsOnlyMaxTokens()
    {
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(Local(), antiLoop: false, maxOutputTokensOverride: 4096);
        mct.Should().BeNull();
        mt.Should().Be(4096); // bypasses the 2048 fallback (which exists to fail-fast a translation loop)
    }

    [Fact]
    public void Override_Cloud_NoUserCap_UsesMaxCompletionTokens_NotMaxTokens()
    {
        // Cloud (null fallback, e.g. OpenAI) with no user cap must send max_completion_tokens (o-series safe),
        // never max_tokens — matching the translation path, which sends neither field on cloud.
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(Cloud(), antiLoop: false, maxOutputTokensOverride: 1024);
        mct.Should().Be(1024);
        mt.Should().BeNull();
    }

    [Fact]
    public void Override_WithMaxCompletionTokens_SendsOnlyCompletion_NeverBoth()
    {
        var s = Cloud();
        s.MaxCompletionTokens = 1000;
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(s, antiLoop: false, maxOutputTokensOverride: 4096);
        mct.Should().Be(4096); // override > user cap
        mt.Should().BeNull();  // never send both
    }

    [Fact]
    public void Override_UserCapHigherThanOverride_IsUsedAsFloor()
    {
        var s = Cloud();
        s.MaxCompletionTokens = 8000;
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(s, antiLoop: false, maxOutputTokensOverride: 4096);
        mct.Should().Be(8000); // never silently lowered below the user's value
        mt.Should().BeNull();
    }

    [Fact]
    public void Override_UserMaxTokensHigher_IsUsedAsFloor_OnMaxTokens()
    {
        var s = Local();
        s.MaxTokens = 5000;
        var (mct, mt) = OpenAIBaseTranslateService.ResolveMaxTokens(s, antiLoop: false, maxOutputTokensOverride: 1024);
        mct.Should().BeNull();
        mt.Should().Be(5000);
    }
}
