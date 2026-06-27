using AwesomeAssertions;

namespace FlyleafLib;

public class SubtitleSearcherTests
{
    private static bool Match(string query, string text, bool matchCase = false, bool wholeWord = false, bool useRegex = false)
    {
        SubtitleSearcher? s = SubtitleSearcher.TryCreate(query, matchCase, wholeWord, useRegex);
        s.Should().NotBeNull("the query should compile for this case");
        return s!.IsMatch(text);
    }

    // ---- default: case-insensitive substring (must match the prior behavior) ----

    [Theory]
    [InlineData("hello", "Hello world", true)]
    [InlineData("HELLO", "hello world", true)]
    [InlineData("lo wo", "hello world", true)]
    [InlineData("xyz", "hello world", false)]
    public void Default_IsCaseInsensitiveSubstring(string query, string text, bool expected)
    {
        Match(query, text).Should().Be(expected);
    }

    [Fact]
    public void Default_IsCyrillicCaseInsensitive()
    {
        Match("привет", "ПРИВЕТ, МИР").Should().BeTrue();
    }

    // ---- match case ----

    [Theory]
    [InlineData("Hello", "Hello world", true)]
    [InlineData("hello", "Hello world", false)]
    public void MatchCase_IsCaseSensitive(string query, string text, bool expected)
    {
        Match(query, text, matchCase: true).Should().Be(expected);
    }

    // ---- whole word ----

    [Theory]
    [InlineData("cat", "the cat sat", true)]
    [InlineData("cat", "category error", false)]   // substring but not a whole word
    [InlineData("cat", "a scatter", false)]
    public void WholeWord_MatchesOnlyFullWords(string query, string text, bool expected)
    {
        Match(query, text, wholeWord: true).Should().Be(expected);
    }

    [Fact]
    public void WholeWord_IsCaseInsensitiveByDefault()
    {
        Match("CAT", "the cat sat", wholeWord: true).Should().BeTrue();
    }

    [Fact]
    public void WholeWord_RespectsMatchCase()
    {
        Match("CAT", "the cat sat", matchCase: true, wholeWord: true).Should().BeFalse();
    }

    [Fact]
    public void WholeWord_WorksForCyrillic()
    {
        Match("кот", "тут кот спит", wholeWord: true).Should().BeTrue();
        Match("кот", "котёнок спит", wholeWord: true).Should().BeFalse();
    }

    [Fact]
    public void WholeWord_EscapesRegexMetacharacters()
    {
        // "a.b" must be a literal whole word, not a regex (the '.' is escaped)
        Match("a.b", "an a.b token", wholeWord: true).Should().BeTrue();
        Match("a.b", "an axb token", wholeWord: true).Should().BeFalse();
    }

    // ---- regex ----

    [Theory]
    [InlineData("h.llo", "hello", true)]
    [InlineData("h.llo", "hallo", true)]
    [InlineData("^world", "world peace", true)]
    [InlineData("^world", "hello world", false)]
    [InlineData(@"\d+", "scene 42", true)]
    public void Regex_MatchesPattern(string query, string text, bool expected)
    {
        Match(query, text, useRegex: true).Should().Be(expected);
    }

    [Fact]
    public void Regex_IsCaseInsensitiveByDefault()
    {
        Match("HELLO", "hello", useRegex: true).Should().BeTrue();
    }

    [Fact]
    public void Regex_RespectsMatchCase()
    {
        Match("HELLO", "hello", matchCase: true, useRegex: true).Should().BeFalse();
    }

    [Fact]
    public void Regex_WithWholeWord_AnchorsToBoundaries()
    {
        Match("ca.", "the cat sat", wholeWord: true, useRegex: true).Should().BeTrue();   // "cat" is a whole word
        Match("ca.", "category error", wholeWord: true, useRegex: true).Should().BeFalse(); // "cat" inside "category"
    }

    [Fact]
    public void Regex_WithWholeWord_BindsBoundaryAcrossAlternation()
    {
        // The \b(?:...)\b wrap must bind word boundaries across the WHOLE alternation. A naive \ba|bc\b parses as
        // (\ba)|(bc\b) and would wrongly match "bc" inside "zbc"; the grouping prevents that.
        Match("a|bc", "zbc", wholeWord: true, useRegex: true).Should().BeFalse();
        Match("cat|dog", "a dog", wholeWord: true, useRegex: true).Should().BeTrue();
        Match("cat|dog", "a doghouse", wholeWord: true, useRegex: true).Should().BeFalse();
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[a-")]
    [InlineData("*abc")]
    public void Regex_InvalidPattern_ReturnsNull(string badPattern)
    {
        SubtitleSearcher.TryCreate(badPattern, matchCase: false, wholeWord: false, useRegex: true)
            .Should().BeNull();
    }

    [Fact]
    public void WholeWordRegex_InvalidPattern_ReturnsNull()
    {
        SubtitleSearcher.TryCreate("(", matchCase: false, wholeWord: true, useRegex: true)
            .Should().BeNull();
    }

    [Fact]
    public void NonRegexInvalidLookingQuery_StillValid()
    {
        // a regex metacharacter in plain/whole-word mode is a literal, never an invalid-pattern null
        SubtitleSearcher.TryCreate("(", matchCase: false, wholeWord: false, useRegex: false).Should().NotBeNull();
        Match("(", "a ( bracket").Should().BeTrue();
    }

    // ---- empty / null text ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmptyText_NeverMatches(string? text)
    {
        SubtitleSearcher.TryCreate("anything", false, false, false)!.IsMatch(text).Should().BeFalse();
        SubtitleSearcher.TryCreate("anything", false, true, false)!.IsMatch(text).Should().BeFalse();
        SubtitleSearcher.TryCreate("any.", false, false, true)!.IsMatch(text).Should().BeFalse();
    }
}
