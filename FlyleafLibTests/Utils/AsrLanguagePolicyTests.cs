using AwesomeAssertions;

namespace FlyleafLib;

// Pure truth-table tests for the ASR language-pinning policy (F-17 pin vs T-10 per-segment detection).
// With the per-segment toggle OFF these must reduce to the original F-17 behavior; ON, pinning is disabled.
public class AsrLanguagePolicyTests
{
    // --- ShouldPinLanguage ---

    [Fact]
    public void ShouldPinLanguage_Off_PinsWhenAutoDetectingAndLanguageKnown()
    {
        // F-17 behavior (toggle off): pin the already-detected language onto later segments/chunks.
        AsrLanguagePolicy.ShouldPinLanguage(perSegmentLanguage: false, isLanguageDetect: true, detectedLanguage: "en")
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldPinLanguage_Off_DoesNotPinBeforeAnyLanguageDetected()
    {
        // Nothing detected yet (null or empty) → cannot pin; the first segment still auto-detects.
        AsrLanguagePolicy.ShouldPinLanguage(false, true, null).Should().BeFalse();
        AsrLanguagePolicy.ShouldPinLanguage(false, true, "").Should().BeFalse();
    }

    [Fact]
    public void ShouldPinLanguage_Off_DoesNotPinWhenNotAutoDetecting()
    {
        // A fixed/model language never auto-detects, so pinning never applied even with the toggle off.
        AsrLanguagePolicy.ShouldPinLanguage(false, false, "en").Should().BeFalse();
    }

    [Fact]
    public void ShouldPinLanguage_On_NeverPins()
    {
        // T-10 per-segment: never pin, regardless of detect state / known language.
        AsrLanguagePolicy.ShouldPinLanguage(true, true, "en").Should().BeFalse();
        AsrLanguagePolicy.ShouldPinLanguage(true, true, null).Should().BeFalse();
        AsrLanguagePolicy.ShouldPinLanguage(true, false, "en").Should().BeFalse();
    }

    // --- ShouldResetPerChunk ---

    [Fact]
    public void ShouldResetPerChunk_OnlyWhenPerSegmentAndAutoDetecting()
    {
        // faster-whisper re-detects per chunk only when per-segment is on AND auto-detection is active.
        AsrLanguagePolicy.ShouldResetPerChunk(perSegmentLanguage: true, isLanguageDetect: true).Should().BeTrue();
        AsrLanguagePolicy.ShouldResetPerChunk(true, false).Should().BeFalse();
        AsrLanguagePolicy.ShouldResetPerChunk(false, true).Should().BeFalse();
        AsrLanguagePolicy.ShouldResetPerChunk(false, false).Should().BeFalse();
    }
}
