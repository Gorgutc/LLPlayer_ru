using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib;

public class ChatReplyParserTests
{
    [Theory]
    // Too short / too few words: deliberately NOT flagged (thresholds are conservative to avoid
    // false-positives on legitimately repetitive subtitles).
    [InlineData("ok ok ok", false)]
    [InlineData("", false)]
    // Normal, varied translation: not a loop.
    [InlineData("The quick brown fox jumps over the lazy dog every morning.", false)]
    // Unigram loop: the same word repeated many consecutive times.
    [InlineData("repeat repeat repeat repeat repeat repeat repeat repeat", true)]
    // Bigram loop: the same two-word phrase repeated many consecutive times.
    [InlineData("foo bar foo bar foo bar foo bar foo bar foo bar", true)]
    public void IsDegenerate_DetectsLoops(string input, bool expected)
    {
        ChatReplyParser.IsDegenerate(input).Should().Be(expected);
    }

    [Theory]
    // No reasoning tag: returned unchanged.
    [InlineData("Just a normal translation", "Just a normal translation")]
    // Closed reasoning tag: stripped, the answer portion is kept (leading whitespace trimmed).
    [InlineData("<think>thinking...</think>Hello world", "Hello world")]
    [InlineData("<reasoning>steps</reasoning>\n\nResult", "Result")]
    // Truncated reasoning (open tag, no close): there is no usable answer, so an empty result is
    // returned and the caller fails the reply instead of leaking raw chain-of-thought as the translation.
    [InlineData("<think>cut off mid thought", "")]
    public void StripReasoning_HandlesReasoningTags(string input, string expected)
    {
        ChatReplyParser.StripReasoning(input).ToString().Should().Be(expected);
    }
}
