using AwesomeAssertions;

namespace FlyleafLib;

// LanguageBadge is the pure logic behind the sidebar per-cue language badge (T-10 follow-up): the badge code
// formatting and the visibility gate. Expectations are derived from the code: Language.Get(string) leaves
// ISO6391 null on its Unknown branch (unresolvable / "und" / blank input), and ASR stamps Language on every
// cue even with ASRPerSegmentLanguage off — which is exactly why ShouldShow must gate on the toggle.
public class LanguageBadgeTests
{
    // === ToBadgeCode =============================================================================

    [Fact]
    public void ToBadgeCode_NullLanguage_ReturnsEmpty()
    {
        LanguageBadge.ToBadgeCode(null).Should().Be("");
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("ru", "ru")]
    [InlineData("EN", "en")] // Language.Get lower-cases on lookup; badge is lower-case either way
    public void ToBadgeCode_ResolvableLanguage_ReturnsLowerCaseIso6391(string input, string expected)
    {
        LanguageBadge.ToBadgeCode(Language.Get(input)).Should().Be(expected);
    }

    [Theory]
    [InlineData("und")] // explicit undetermined marker → Unknown branch, ISO6391 never assigned (stays null)
    [InlineData("")]    // blank input → StringToCulture returns null → same Unknown branch
    public void ToBadgeCode_UnknownLanguage_ReturnsEmpty(string input)
    {
        // RED-without-fix guard: without the null/whitespace guard this would NRE on ISO6391.ToLowerInvariant().
        LanguageBadge.ToBadgeCode(Language.Get(input)).Should().Be("");
    }

    [Fact]
    public void ToBadgeCode_EnglishStatic_ReturnsEn()
    {
        LanguageBadge.ToBadgeCode(Language.English).Should().Be("en");
    }

    // === ShouldShow ==============================================================================

    [Fact]
    public void ShouldShow_GateOff_HidesEvenWithResolvableLanguage()
    {
        // ASR stamps Language on every cue even with the per-segment toggle off (it mirrors the pinned
        // transcript language) — the gate is what keeps the default-config UI byte-identical.
        LanguageBadge.ShouldShow(false, Language.Get("en")).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_GateOn_NullLanguage_Hides()
    {
        LanguageBadge.ShouldShow(true, null).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_GateOn_UnknownLanguage_Hides()
    {
        LanguageBadge.ShouldShow(true, Language.Get("und")).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_GateOn_ResolvableLanguage_Shows()
    {
        LanguageBadge.ShouldShow(true, Language.Get("en")).Should().BeTrue();
    }
}
