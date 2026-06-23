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
    // The exact production bug (Clarkson's Farm): a faithful translation of a source line that itself shouts a
    // word ~6× must NOT be flagged — the reply's repetition budget scales with the source's own repetition.
    [InlineData("Иди и открой эти ворота. Быстро, быстро, быстро, быстро, быстро, быстро.",
                "Go and open these gates. Quick, quick, quick, quick, quick, quick.", false)]
    // Faithful unigram ×6 mirrored from a source that repeats ×6: not a loop.
    [InlineData("иди иди иди иди иди иди открой эти ворота сейчас же",
                "go go go go go go open these gates right now", false)]
    // Genuine runaway unigram loop with a calm (non-repetitive) source: still a loop.
    [InlineData("привет привет привет привет привет привет привет привет привет",
                "Hello there, how are you doing this fine evening my friend?", true)]
    // Genuine bigram runaway with a calm source: still a loop (caught via n=2).
    [InlineData("да ладно да ладно да ладно да ладно да ладно да ладно",
                "I cannot believe what you just told me about the meeting.", true)]
    // Overshoot: a repetitive source (×6) does NOT grant unlimited budget — a reply repeating ×12 is a loop.
    [InlineData("быстро быстро быстро быстро быстро быстро быстро быстро быстро быстро быстро быстро",
                "Quick, quick, quick, quick, quick, quick!", true)]
    // Boundary: a reply at exactly source(6)+headroom(2)=8 consecutive repeats is allowed (strict > compare).
    [InlineData("стоп стоп стоп стоп стоп стоп стоп стоп прекрати сейчас же",
                "stop stop stop stop stop stop please stop now okay friend", false)]
    // One past the boundary: 9 > 8 -> flagged. Confirms the budget edge is exact.
    [InlineData("стоп стоп стоп стоп стоп стоп стоп стоп стоп прекрати сейчас",
                "stop stop stop stop stop stop please stop now okay friend", true)]
    // Block "---" loop with a non-repetitive source: the block scan must stay armed.
    [InlineData("Это длинная фраза для перевода субтитра.\n\n---\n\nЭто длинная фраза для перевода субтитра.\n\n---\n\nЭто длинная фраза для перевода субтитра.",
                "He walked slowly down the quiet road toward the old house.", true)]
    // Source is itself a repeated-block cue (e.g. a repeated SDH line): a faithful 3× block translation is NOT a loop.
    [InlineData("Звонит телефон сейчас.\n\n---\n\nЗвонит телефон сейчас.\n\n---\n\nЗвонит телефон сейчас.",
                "The phone is ringing now.\n\n---\n\nThe phone is ringing now.\n\n---\n\nThe phone is ringing now.", false)]
    // A short reply is never analysed, even with a source present (the <40-char short-circuit wins first).
    [InlineData("ok ok ok", "da da da", false)]
    // Source repeats a BIGRAM ×5 and the reply faithfully mirrors a bigram ×5: not a loop (the budget scales
    // from the source's own n=2 repetition).
    [InlineData("ну давай ну давай ну давай ну давай ну давай",
                "come on come on come on come on come on", false)]
    // Source repeats a bigram ×5 but the reply is a unigram runaway ×9: still a loop — a bigram-derived budget
    // must not fully mask an unrelated unigram loop once it overshoots.
    [InlineData("хватит хватит хватит хватит хватит хватит хватит хватит хватит",
                "come on come on come on come on come on", true)]
    // Dominance branch, non-consecutive 4-of-6 dominant block, against a source whose own dominant block
    // repeats ×3: NOT a loop (4 > srcTopCount(3)+1 is false). Pins the `topCount > srcTopCount + 1` offset.
    [InlineData("Открой дверь сейчас.\n\n---\n\nСтой тут пожалуйста.\n\n---\n\nОткрой дверь сейчас.\n\n---\n\nСтой тут пожалуйста.\n\n---\n\nОткрой дверь сейчас.\n\n---\n\nОткрой дверь сейчас.",
                "Open the door now.\n\n---\n\nOpen the door now.\n\n---\n\nOpen the door now.\n\n---\n\nGo away from here.", false)]
    // Same non-consecutive 4-of-6 dominant reply, but against a non-repetitive source: IS a loop (the dominance
    // path fires when the source does not justify the repetition).
    [InlineData("Открой дверь сейчас.\n\n---\n\nСтой тут пожалуйста.\n\n---\n\nОткрой дверь сейчас.\n\n---\n\nСтой тут пожалуйста.\n\n---\n\nОткрой дверь сейчас.\n\n---\n\nОткрой дверь сейчас.",
                "He walked slowly down the quiet road toward the old house.", true)]
    public void IsDegenerate_SourceAware(string reply, string source, bool expected)
    {
        ChatReplyParser.IsDegenerate(reply, source).Should().Be(expected);
    }

    [Fact]
    // The single-argument overload must behave exactly like the source-blind original (source == null), so the
    // existing callers (Hello path) and the IsDegenerate_DetectsLoops corpus keep their behaviour.
    public void IsDegenerate_SingleArg_EqualsNullSource()
    {
        string loop = "repeat repeat repeat repeat repeat repeat repeat repeat";
        ChatReplyParser.IsDegenerate(loop).Should().Be(ChatReplyParser.IsDegenerate(loop, null));
        ChatReplyParser.IsDegenerate(loop).Should().BeTrue();
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
