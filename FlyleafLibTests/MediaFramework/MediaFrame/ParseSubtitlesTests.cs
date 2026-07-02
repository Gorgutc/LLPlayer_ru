using System.Drawing;
using AwesomeAssertions;

namespace FlyleafLib.MediaFramework.MediaFrame;

// ParseSubtitles.SSAtoSubStyles is the default subtitle styling parser: it strips SSA/ASS inline override
// tags ({\b1}..{\b0}, italic/underline/strikeout, {\c&Hbbggrr&}..{\c}) and returns the plain text plus the
// styled ranges (SubStyle from/len). For an SSA "Dialogue" line it also extracts the displayed text after the
// last ",," (converting \N -> newline and trimming), and finally applies a fixed Greek-apostrophe substitution.
// Pure string parser, no I/O / no culture sensitivity (colors are fixed BGR hex). Expectations are traced from
// the implementation. Malformed input (an unterminated '{\..' with no '}', a lone '{\}' or '{\b}', or a leading
// backslash) used to throw in production; those paths are now guarded (HC-03) and asserted at the bottom.
public class ParseSubtitlesTests
{
    [Fact]
    public void Null_ReturnsEmptyAndNoStyles()
    {
        string text = ParseSubtitles.SSAtoSubStyles(null!, out var styles);
        text.Should().Be("");
        styles.Should().BeEmpty();
    }

    [Fact]
    public void Empty_ReturnsEmptyAndNoStyles()
    {
        string text = ParseSubtitles.SSAtoSubStyles("", out var styles);
        text.Should().Be("");
        styles.Should().BeEmpty();
    }

    [Fact]
    public void PlainText_NoTags_NoDoubleComma_IsReturnedVerbatim()
    {
        // No ",," means no \N conversion and no Trim is applied.
        string text = ParseSubtitles.SSAtoSubStyles("Hello world", out var styles);
        text.Should().Be("Hello world");
        styles.Should().BeEmpty();
    }

    [Fact]
    public void PlainText_NoDoubleComma_PreservesSurroundingWhitespace()
    {
        ParseSubtitles.SSAtoSubStyles("  hi  ", out _).Should().Be("  hi  ");
    }

    [Fact]
    public void DialogueLine_TakesTextAfterLastDoubleComma()
    {
        ParseSubtitles.SSAtoSubStyles("a,,b,,c", out var styles).Should().Be("c");
        styles.Should().BeEmpty();
    }

    [Fact]
    public void DialogueLine_ConvertsLiteralBackslashN_ToNewline()
    {
        ParseSubtitles.SSAtoSubStyles("x,,line1\\Nline2", out _).Should().Be("line1\nline2");
    }

    [Fact]
    public void DialogueLine_TrimsExtractedText()
    {
        ParseSubtitles.SSAtoSubStyles("x,,  spaced  ", out _).Should().Be("spaced");
    }

    [Fact]
    public void Bold_OpenClose_ProducesBoldRangeOverInnerText()
    {
        string text = ParseSubtitles.SSAtoSubStyles("{\\b1}Bold{\\b0}", out var styles);
        text.Should().Be("Bold");
        styles.Should().HaveCount(1);
        styles[0].style.Should().Be(SubStyles.BOLD);
        styles[0].from.Should().Be(0);
        styles[0].len.Should().Be(4);
    }

    [Fact]
    public void Italic_OpenClose_ProducesItalicRange()
    {
        string text = ParseSubtitles.SSAtoSubStyles("{\\i1}abc{\\i0}", out var styles);
        text.Should().Be("abc");
        styles.Should().HaveCount(1);
        styles[0].style.Should().Be(SubStyles.ITALIC);
        styles[0].from.Should().Be(0);
        styles[0].len.Should().Be(3);
    }

    [Fact]
    public void Underline_OpenClose_ProducesUnderlineRange()
    {
        string text = ParseSubtitles.SSAtoSubStyles("{\\u1}xy{\\u0}", out var styles);
        text.Should().Be("xy");
        styles.Should().ContainSingle();
        styles[0].style.Should().Be(SubStyles.UNDERLINE);
        styles[0].from.Should().Be(0);
        styles[0].len.Should().Be(2);
    }

    [Fact]
    public void Strikeout_OpenClose_ProducesStrikeoutRange()
    {
        string text = ParseSubtitles.SSAtoSubStyles("{\\s1}z{\\s0}", out var styles);
        text.Should().Be("z");
        styles.Should().ContainSingle();
        styles[0].style.Should().Be(SubStyles.STRIKEOUT);
        styles[0].from.Should().Be(0);
        styles[0].len.Should().Be(1);
    }

    [Fact]
    public void Color_OpenClose_ParsesBgrHexIntoArgb()
    {
        // {\c&H0000FF&} is BGR &HBBGGRR => BB=00 GG=00 RR=FF => opaque red.
        string text = ParseSubtitles.SSAtoSubStyles("{\\c&H0000FF&}Red{\\c}", out var styles);
        text.Should().Be("Red");
        styles.Should().HaveCount(1);
        styles[0].style.Should().Be(SubStyles.COLOR);
        styles[0].from.Should().Be(0);
        styles[0].len.Should().Be(3);
        styles[0].value.Should().Be(Color.FromArgb(255, 255, 0, 0));
    }

    [Fact]
    public void Color_CloseWithoutOpen_AddsNoStyle()
    {
        // The close branch guards `if (color.from == -1) break;` so a stray {\c} with no prior open is a no-op.
        string text = ParseSubtitles.SSAtoSubStyles("{\\c}text", out var styles);
        text.Should().Be("text");
        styles.Should().BeEmpty();
    }

    [Fact]
    public void Color_ShortHexCode_LeavesValueTransparentAndEmitsNoStyle()
    {
        // code "\c&H&" is shorter than 7 chars => `if (code.Length < 7) break;` => value stays Transparent,
        // so the end-of-string flush skips it (`color.value != Color.Transparent`).
        string text = ParseSubtitles.SSAtoSubStyles("{\\c&H&}X", out var styles);
        text.Should().Be("X");
        styles.Should().BeEmpty();
    }

    [Fact]
    public void Color_TwoDigitHex_SetsRedOnly()
    {
        // &HFF => only the red byte is present (the green/blue extraction guards are not entered).
        string text = ParseSubtitles.SSAtoSubStyles("{\\c&HFF&}X", out var styles);
        text.Should().Be("X");
        styles.Should().ContainSingle();
        styles[0].style.Should().Be(SubStyles.COLOR);
        styles[0].value.Should().Be(Color.FromArgb(255, 255, 0, 0));
    }

    [Fact]
    public void Color_SixDigitHex_IsParsedAsBbGgRr()
    {
        // &H112233 is &HBBGGRR: BB=11 GG=22 RR=33 => B=0x11, G=0x22, R=0x33. Exercises all three channel reads
        // with distinct values (the open color also runs to the end as a non-closing style).
        string text = ParseSubtitles.SSAtoSubStyles("{\\c&H112233&}X", out var styles);
        text.Should().Be("X");
        styles.Should().ContainSingle();
        styles[0].style.Should().Be(SubStyles.COLOR);
        styles[0].value.Should().Be(Color.FromArgb(255, 0x33, 0x22, 0x11));
    }

    [Fact]
    public void OverlappingStyles_CloseInReverseOrder()
    {
        // {\b1}{\i1}AB{\i0}{\b0}: bold then italic open, italic then bold close -> both span [0,2], emitted in
        // closing order (italic first).
        string text = ParseSubtitles.SSAtoSubStyles("{\\b1}{\\i1}AB{\\i0}{\\b0}", out var styles);
        text.Should().Be("AB");
        styles.Should().HaveCount(2);
        styles[0].style.Should().Be(SubStyles.ITALIC);
        styles[0].from.Should().Be(0);
        styles[0].len.Should().Be(2);
        styles[1].style.Should().Be(SubStyles.BOLD);
        styles[1].from.Should().Be(0);
        styles[1].len.Should().Be(2);
    }

    [Fact]
    public void NonClosingBold_RunsToEndOfText()
    {
        string text = ParseSubtitles.SSAtoSubStyles("{\\b1}Bold", out var styles);
        text.Should().Be("Bold");
        styles.Should().ContainSingle();
        styles[0].style.Should().Be(SubStyles.BOLD);
        styles[0].from.Should().Be(0);
        styles[0].len.Should().Be(4);
    }

    [Fact]
    public void Tag_BetweenText_OffsetsAreRelativeToStrippedText()
    {
        string text = ParseSubtitles.SSAtoSubStyles("ab{\\b1}cd{\\b0}ef", out var styles);
        text.Should().Be("abcdef");
        styles.Should().HaveCount(1);
        styles[0].style.Should().Be(SubStyles.BOLD);
        styles[0].from.Should().Be(2);
        styles[0].len.Should().Be(2);
    }

    [Fact]
    public void MultipleDisjointStyles_AreEmittedInClosingOrder()
    {
        string text = ParseSubtitles.SSAtoSubStyles("{\\b1}A{\\b0}{\\i1}B{\\i0}", out var styles);
        text.Should().Be("AB");
        styles.Should().HaveCount(2);
        styles[0].style.Should().Be(SubStyles.BOLD);
        styles[0].from.Should().Be(0);
        styles[0].len.Should().Be(1);
        styles[1].style.Should().Be(SubStyles.ITALIC);
        styles[1].from.Should().Be(1);
        styles[1].len.Should().Be(1);
    }

    [Fact]
    public void Backslash_NotAfterBrace_IsLiteral()
    {
        // The override-code path only triggers for a backslash whose previous char is '{'.
        ParseSubtitles.SSAtoSubStyles("a\\b", out var styles).Should().Be("a\\b");
        styles.Should().BeEmpty();
    }

    [Fact]
    public void BackslashN_WithoutDoubleComma_IsNotConverted()
    {
        // \N -> newline conversion is part of the Dialogue (",,") extraction only.
        ParseSubtitles.SSAtoSubStyles("a\\Nb", out _).Should().Be("a\\Nb");
    }

    [Fact]
    public void GreekApostrophe_IsSubstituted()
    {
        // The parser ends with sout.Replace("ʼ", "Ά").
        ParseSubtitles.SSAtoSubStyles("aʼb", out _).Should().Be("aΆb");
    }

    // --- HC-03: malformed/boundary ASS must not throw (these inputs crashed before the guards) ---

    [Fact]
    public void UnterminatedOverrideBlock_DoesNotThrow()
    {
        // "{\i1 Hello" has no closing '}': previously s.Substring(i, negativeLen) threw ArgumentOutOfRange.
        FluentActions.Invoking(() => ParseSubtitles.SSAtoSubStyles("{\\i1 Hello", out _))
            .Should().NotThrow();
    }

    [Fact]
    public void LoneBackslashBrace_DoesNotThrow()
    {
        // "{\}" yields code "\" (length 1): previously switch(code[1]) threw IndexOutOfRange.
        FluentActions.Invoking(() => ParseSubtitles.SSAtoSubStyles("{\\}", out _))
            .Should().NotThrow();
    }

    [Theory]
    [InlineData("{\\b}")]
    [InlineData("{\\u}")]
    [InlineData("{\\s}")]
    public void ToggleTagWithoutDigit_DoesNotThrow(string input)
    {
        // "{\b}" yields code "\b" (length 2): previously code[2] threw IndexOutOfRange for b/u/s (i was already guarded).
        FluentActions.Invoking(() => ParseSubtitles.SSAtoSubStyles(input, out _))
            .Should().NotThrow();
    }

    [Fact]
    public void LeadingBackslash_IsLiteral_DoesNotThrow()
    {
        // A backslash at index 0 previously evaluated s[i-1] == s[-1] -> IndexOutOfRange.
        ParseSubtitles.SSAtoSubStyles("\\x", out var styles).Should().Be("\\x");
        styles.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{\\c&HZZ&}x")]
    [InlineData("{\\c&HG1&}x")]
    public void ColorTagWithNonHexPayload_DoesNotThrow(string input)
    {
        // A color override with a non-hex payload previously threw FormatException in int.Parse(HexNumber).
        FluentActions.Invoking(() => ParseSubtitles.SSAtoSubStyles(input, out _))
            .Should().NotThrow();
    }
}
