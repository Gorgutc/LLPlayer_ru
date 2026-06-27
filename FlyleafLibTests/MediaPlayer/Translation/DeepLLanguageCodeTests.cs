using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.Translation;

// DeepL uses region-qualified codes that differ from plain ISO-639-1. The internal static mappers
// DeepLTranslateService.ToSourceCode / ToTargetCode encode those special cases (reached here via
// InternalsVisibleTo("FlyleafLibTests")) and had no coverage. DeepLX delegates to the same service.
public class DeepLLanguageCodeTests
{
    [Theory]
    [InlineData("ku", "KMR")] // Kurdish (Kurmanji) special-case
    [InlineData("no", "nb")]  // Norwegian -> Bokmal special-case (stays lower-case, not uppercased)
    [InlineData("en", "EN")]
    [InlineData("de", "DE")]
    [InlineData("ru", "RU")]
    [InlineData("zh", "ZH")]
    [InlineData("pt", "PT")]
    public void ToSourceCode_MapsSpecialCasesAndUppercasesRest(string iso6391, string expected)
    {
        DeepLTranslateService.ToSourceCode(iso6391).Should().Be(expected);
    }

    [Theory]
    [InlineData(TargetLanguage.EnglishAmerican, "EN-US")]
    [InlineData(TargetLanguage.EnglishBritish, "EN-GB")]
    [InlineData(TargetLanguage.Portuguese, "PT-PT")]
    [InlineData(TargetLanguage.PortugueseBrazilian, "PT-BR")]
    [InlineData(TargetLanguage.ChineseSimplified, "ZH-HANS")]
    [InlineData(TargetLanguage.ChineseTraditional, "ZH-HANT")]
    [InlineData(TargetLanguage.Kurdish, "KMR")]
    [InlineData(TargetLanguage.German, "DE")]   // default branch: ToISO6391 -> "de" -> upper
    [InlineData(TargetLanguage.Russian, "RU")]
    [InlineData(TargetLanguage.French, "FR")]   // default branch: EnumMember "fr-FR" -> ISO "fr" -> upper
    [InlineData(TargetLanguage.Japanese, "JA")]
    public void ToTargetCode_MapsRegionVariantsAndDefaults(TargetLanguage target, string expected)
    {
        DeepLTranslateService.ToTargetCode(target).Should().Be(expected);
    }
}
