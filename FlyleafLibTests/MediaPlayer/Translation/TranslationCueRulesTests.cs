using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLibTests.MediaPlayer.Translation;

// HC-42: the interactive (SubTranslator) and batch (BatchSubtitleTranslator) paths now share these per-cue rules
// instead of each keeping a hand-mirrored copy. These tests pin the shared behaviour so a future edit that
// changes one rule is caught here rather than silently desyncing batch vs interactive translation.
public class TranslationCueRulesTests
{
    private static readonly SubtitleSegmentOptions Opt = new()
    {
        MaxCharsPerLine = 42,
        MaxLinesPerCue = 2,
        MaxCjkCharsPerLine = 21,
        MaxCueDurationSec = 6.0,
        MinCueDurationSec = 1.0,
    };

    // A long line that SubtitleSegmenter.WrapLines splits into two lines (see SubtitleSegmenterTests).
    private const string LongLine = "The quick brown fox jumps over the lazy dog near the river.";

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\n\t ", false)]
    [InlineData("hi", true)]
    [InlineData("  x  ", true)]
    public void ShouldAcceptReply_RejectsBlankRepliesOnly(string? reply, bool expected)
    {
        TranslationCueRules.ShouldAcceptReply(reply).Should().Be(expected);
    }

    [Fact]
    public void PostProcess_ResegmentOff_ReturnsInputUnchanged()
    {
        // Gate off: the reply is returned verbatim, even when it would otherwise wrap.
        string result = TranslationCueRules.PostProcess(LongLine, resegment: false, Opt);

        result.Should().Be(LongLine);
        result.Should().NotContain("\n");
    }

    [Fact]
    public void PostProcess_ResegmentOn_WrapsExactlyLikeSegmenter()
    {
        // Gate on: identical to a direct SubtitleSegmenter.WrapLines call, and the long line actually wraps.
        string result = TranslationCueRules.PostProcess(LongLine, resegment: true, Opt);

        result.Should().Be(SubtitleSegmenter.WrapLines(LongLine, Opt));
        result.Should().Contain("\n");
        result.Should().NotBe(LongLine);
    }

    [Theory]
    [InlineData(-3, -1, 0, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(2, 5, 2, 5)]
    [InlineData(-1, 4, 0, 4)]
    public void ClampWindow_ClampsNegativesToZero(int before, int after, int expBefore, int expAfter)
    {
        (int b, int a) = TranslationCueRules.ClampWindow(before, after);
        b.Should().Be(expBefore);
        a.Should().Be(expAfter);
    }

    [Fact]
    public void BuildContext_FlattensNeighboursAndKeepsFocalText()
    {
        SubtitleTranslationContext ctx = TranslationCueRules.BuildContext(
            "focal line",
            rawBefore: new[] { "a\nb", "c" },
            rawAfter: new[] { "d\ne" });

        ctx.Text.Should().Be("focal line");
        ctx.Before.Should().Equal("a b", "c");   // multi-line neighbour flattened, single-line kept
        ctx.After.Should().Equal("d e");
    }

    [Fact]
    public void BuildContext_NoNeighbours_YieldsEmptyWindow()
    {
        SubtitleTranslationContext ctx = TranslationCueRules.BuildContext(
            "only line", rawBefore: [], rawAfter: []);

        ctx.Text.Should().Be("only line");
        ctx.Before.Should().BeEmpty();
        ctx.After.Should().BeEmpty();
    }
}
