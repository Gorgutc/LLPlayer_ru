using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer.Translation;

// TargetLanguageExtensions.DisplayName (a pure switch) and ToISO6391 (reads the [EnumMember] code and takes the
// part before a hyphen) had no coverage. DisplayName expectations come straight from the switch arms; ToISO6391
// expectations come from the EnumMember codes already characterised by DeepLLanguageCodeTests
// (EnglishAmerican="en-US", French="fr-FR", Portuguese="pt-PT", ChineseSimplified="zh-Hans", Russian="ru",
// German="de"). Non-ASCII enum members (e.g. NorwegianBokmal) are intentionally skipped to keep this file ASCII.
public class TargetLanguageExtensionsTests
{
    [Theory]
    [InlineData(TargetLanguage.EnglishAmerican, "English (American)")]
    [InlineData(TargetLanguage.EnglishBritish, "English (British)")]
    [InlineData(TargetLanguage.French, "French")]
    [InlineData(TargetLanguage.FrenchCanadian, "French (Canadian)")]
    [InlineData(TargetLanguage.Portuguese, "Portuguese")]
    [InlineData(TargetLanguage.PortugueseBrazilian, "Portuguese (Brazilian)")]
    [InlineData(TargetLanguage.ChineseSimplified, "Chinese (Simplified)")]
    [InlineData(TargetLanguage.ChineseTraditional, "Chinese (Traditional)")]
    [InlineData(TargetLanguage.Kurdish, "Kurdish (Kurmanji)")]
    [InlineData(TargetLanguage.Myanmar, "Myanmar (Burmese)")]
    [InlineData(TargetLanguage.Russian, "Russian")]   // default branch -> enum ToString()
    [InlineData(TargetLanguage.German, "German")]     // default branch
    [InlineData(TargetLanguage.Japanese, "Japanese")] // default branch
    public void DisplayName_MapsSpecialCasesAndDefaultsToEnumName(TargetLanguage target, string expected)
    {
        target.DisplayName().Should().Be(expected);
    }

    [Theory]
    [InlineData(TargetLanguage.EnglishAmerican, "en")]   // EnumMember "en-US" -> "en"
    [InlineData(TargetLanguage.French, "fr")]            // EnumMember "fr-FR" -> "fr"
    [InlineData(TargetLanguage.Portuguese, "pt")]        // EnumMember "pt-PT" -> "pt"
    [InlineData(TargetLanguage.ChineseSimplified, "zh")] // EnumMember "zh-Hans" -> "zh"
    [InlineData(TargetLanguage.Russian, "ru")]           // EnumMember "ru" (no hyphen)
    [InlineData(TargetLanguage.German, "de")]            // EnumMember "de" (no hyphen)
    public void ToISO6391_TakesCodeBeforeHyphen(TargetLanguage target, string expected)
    {
        target.ToISO6391().Should().Be(expected);
    }
}
