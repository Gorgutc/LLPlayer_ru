using AwesomeAssertions;
using FlyleafLib;

namespace FlyleafLib;

public class SubtitleCaseFixerTests
{
    [Theory]
    [InlineData("В СЛЕДУЮЩЕЙ СЕРИИ", "В следующей серии")]
    [InlineData("ТА САМАЯ ИГРУШКА НА РАДИОУПРАВЛЕНИИ", "Та самая игрушка на радиоуправлении")]
    [InlineData("О, БОЖЕ! У НАС ПРОБЛЕМА", "О, боже! У нас проблема")]
    [InlineData("THE QUICK BROWN FOX JUMPS", "The quick brown fox jumps")]
    [InlineData("БОЛЬШОЙ КРАСНЫЙ дом", "Большой красный дом")] // ~0.82 uppercase: pins the threshold's upper edge
    public void FixAllCaps_RewritesAllCapsToSentenceCase(string input, string expected)
    {
        SubtitleCaseFixer.FixAllCaps(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Normal sentence here.")]   // already mixed case
    [InlineData("The Quick Brown Fox")]     // title case (below the uppercase threshold)
    [InlineData("НАТО")]                    // single all-caps word (acronym) -> untouched (one word)
    [InlineData("OK!")]                     // single word
    [InlineData("Привет, мир.")]            // normal Cyrillic
    [InlineData("")]                        // empty
    [InlineData("123 456")]                 // no cased letters
    [InlineData("HTTPS://EXAMPLE.COM/PATH")] // URL: case-sensitive path, skipped by the :// guard
    public void FixAllCaps_LeavesNonAllCapsUnchanged(string input)
    {
        SubtitleCaseFixer.FixAllCaps(input).Should().Be(input);
    }

    [Fact]
    public void FixAllCaps_PreservesLineBreaks_EachLineStartsASentence()
    {
        SubtitleCaseFixer.FixAllCaps("В СЛЕДУЮЩЕЙ СЕРИИ\nУ НАС ПРОБЛЕМА")
            .Should().Be("В следующей серии\nУ нас проблема");
    }

    [Fact]
    public void FixAllCaps_CapitalizesAfterEachSentenceEnder()
    {
        SubtitleCaseFixer.FixAllCaps("СТОП. НАЗАД. ВПЕРЁД")
            .Should().Be("Стоп. Назад. Вперёд");
    }

    [Fact]
    public void FixAllCaps_Null_ReturnsEmpty()
    {
        SubtitleCaseFixer.FixAllCaps(null).Should().BeEmpty();
    }
}
