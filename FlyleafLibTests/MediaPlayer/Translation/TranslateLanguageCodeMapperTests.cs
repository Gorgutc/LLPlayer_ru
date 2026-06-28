using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.Translation;

// Google and Microsoft (Bing/Azure) translate services use region-qualified codes that diverge from plain
// ISO-639-1. The mappers ToSourceCode (instance — it also honours the user's per-language Regions override)
// and ToTargetCode (static) encode those special cases and had no coverage. They are reached here via the
// internal seam + InternalsVisibleTo("FlyleafLibTests"); DeepLLanguageCodeTests covers the DeepL equivalents.
// Expected values are derived from the switch/lookup tables in the services. ASCII codes only -> no culture/ICU
// fragility. Constructing a service only allocates an HttpClient on a shared handler (no network is touched).
public class TranslateLanguageCodeMapperTests
{
    // ---------------- Google v1 ----------------

    [Theory]
    [InlineData("nb", "no")]    // Norwegian Bokmål handled as plain Norwegian, BEFORE any region lookup
    [InlineData("en", "en")]    // no region language -> verbatim
    [InlineData("ru", "ru")]
    [InlineData("ja", "ja")]
    [InlineData("zh", "zh-CN")] // DefaultRegions zh -> zh-CN
    [InlineData("fr", "fr-FR")]
    [InlineData("pt", "pt-BR")]
    public void GoogleV1_ToSourceCode_DefaultRegionsAndSpecialCase(string iso6391, string expected)
    {
        using var svc = new GoogleV1TranslateService(new GoogleV1TranslateSettings());
        svc.ToSourceCode(iso6391).Should().Be(expected);
    }

    [Fact]
    public void GoogleV1_ToSourceCode_HonoursUserRegionOverride()
    {
        // The user picked the Traditional region for a Chinese source; ToSourceCode must prefer that over the
        // default (zh-CN).
        var settings = new GoogleV1TranslateSettings();
        settings.Regions["zh"] = "zh-TW";
        using var svc = new GoogleV1TranslateService(settings);
        svc.ToSourceCode("zh").Should().Be("zh-TW");
    }

    [Fact]
    public void GoogleV1_ToSourceCode_IgnoresRegionOverrideForNonRegionLanguage()
    {
        // "en" is not in DefaultRegions, so the per-language Regions override is never consulted.
        var settings = new GoogleV1TranslateSettings();
        settings.Regions["en"] = "en-GB";
        using var svc = new GoogleV1TranslateService(settings);
        svc.ToSourceCode("en").Should().Be("en");
    }

    [Theory]
    [InlineData(TargetLanguage.ChineseSimplified, "zh-CN")]
    [InlineData(TargetLanguage.ChineseTraditional, "zh-TW")]
    [InlineData(TargetLanguage.French, "fr-FR")]
    [InlineData(TargetLanguage.FrenchCanadian, "fr-CA")]
    [InlineData(TargetLanguage.Portuguese, "pt-PT")]
    [InlineData(TargetLanguage.PortugueseBrazilian, "pt-BR")]
    [InlineData(TargetLanguage.NorwegianBokmål, "no")]   // nb -> no
    [InlineData(TargetLanguage.German, "de")]            // default branch -> ToISO6391
    [InlineData(TargetLanguage.Russian, "ru")]
    [InlineData(TargetLanguage.Japanese, "ja")]
    [InlineData(TargetLanguage.EnglishAmerican, "en")]   // default branch: EnumMember "en-US" -> ISO "en"
    public void GoogleV1_ToTargetCode(TargetLanguage target, string expected)
    {
        GoogleV1TranslateService.ToTargetCode(target).Should().Be(expected);
    }

    // ---------------- Microsoft (Bing/Azure) ----------------

    [Theory]
    [InlineData("zh", "zh-Hans")]  // DefaultRegions zh -> zh-Hans
    [InlineData("fr", "fr")]
    [InlineData("pt", "pt")]
    [InlineData("lg", "lug")]      // Ganda
    [InlineData("mn", "mn-Cyrl")]  // Mongolian
    [InlineData("ny", "nya")]      // Chichewa
    [InlineData("rn", "run")]      // Rundi
    [InlineData("sr", "sr-Latn")]  // Serbian
    [InlineData("no", "nb")]       // Norwegian -> Bokmal
    [InlineData("en", "en")]       // not special -> verbatim
    [InlineData("ru", "ru")]
    public void Microsoft_ToSourceCode_DefaultRegionsAndSpecialCases(string iso6391, string expected)
    {
        using var svc = new BingTranslateService(new BingTranslateSettings());
        svc.ToSourceCode(iso6391).Should().Be(expected);
    }

    [Fact]
    public void Microsoft_ToSourceCode_HonoursUserRegionOverride()
    {
        var settings = new BingTranslateSettings();
        settings.Regions["zh"] = "zh-Hant";
        using var svc = new BingTranslateService(settings);
        svc.ToSourceCode("zh").Should().Be("zh-Hant");
    }

    [Fact]
    public void Microsoft_ToSourceCode_IgnoresRegionOverrideForNonRegionLanguage()
    {
        // "en" hits neither DefaultRegions nor a special-case, so the Regions override is never consulted.
        var settings = new BingTranslateSettings();
        settings.Regions["en"] = "en-GB";
        using var svc = new BingTranslateService(settings);
        svc.ToSourceCode("en").Should().Be("en");
    }

    [Theory]
    [InlineData(TargetLanguage.ChineseSimplified, "zh-Hans")]
    [InlineData(TargetLanguage.ChineseTraditional, "zh-Hant")]
    [InlineData(TargetLanguage.French, "fr")]
    [InlineData(TargetLanguage.FrenchCanadian, "fr-ca")]
    [InlineData(TargetLanguage.Portuguese, "pt-pt")]
    [InlineData(TargetLanguage.PortugueseBrazilian, "pt")]
    [InlineData(TargetLanguage.Ganda, "lug")]
    [InlineData(TargetLanguage.Mongolian, "mn-Cyrl")]
    [InlineData(TargetLanguage.Chichewa, "nya")]
    [InlineData(TargetLanguage.Rundi, "run")]
    [InlineData(TargetLanguage.Serbian, "sr-Latn")]
    [InlineData(TargetLanguage.German, "de")]   // default branch -> ToISO6391
    [InlineData(TargetLanguage.Russian, "ru")]
    [InlineData(TargetLanguage.Japanese, "ja")]
    public void Microsoft_ToTargetCode(TargetLanguage target, string expected)
    {
        // ToTargetCode is an internal static on MicrosoftTranslateServiceBase, shared by Bing and Azure.
        MicrosoftTranslateServiceBase.ToTargetCode(target).Should().Be(expected);
    }
}
