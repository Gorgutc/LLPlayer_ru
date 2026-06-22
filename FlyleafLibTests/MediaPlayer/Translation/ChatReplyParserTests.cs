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
    // Long repeated-block loop (the real-world LMStudio failure mode): one multi-word sentence repeated
    // with "---" separators. The repeat period in words far exceeds 3, so only the block scan catches it.
    [InlineData("Это длинная фраза для перевода субтитра.\n\n---\n\nЭто длинная фраза для перевода субтитра.\n\n---\n\nЭто длинная фраза для перевода субтитра.", true)]
    // Legitimate multi-line subtitle: two distinct lines, not a loop.
    [InlineData("This is the first line of dialogue.\nAnd here is the second, different line.", false)]
    // Spaceless-script (CJK) block loop: a whitespace split yields too few tokens, so the n-gram scan is
    // skipped — the block scan must still catch it (regression guard for the CJK bypass).
    [InlineData("これはテスト用の字幕の長い文章です。\n\n---\n\nこれはテスト用の字幕の長い文章です。\n\n---\n\nこれはテスト用の字幕の長い文章です。", true)]
    // 3+ consecutive identical blocks with no separator lines: still a loop.
    [InlineData("The same repeated sentence here.\nThe same repeated sentence here.\nThe same repeated sentence here.", true)]
    // Legitimate interleaved refrain (A B A B A): the dominant block is only 3-of-5, must NOT be flagged.
    [InlineData("Verse line alpha right here.\nVerse line beta over there.\nVerse line alpha right here.\nVerse line beta over there.\nVerse line alpha right here.", false)]
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
