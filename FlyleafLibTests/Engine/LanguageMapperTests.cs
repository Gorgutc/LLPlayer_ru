using System.Globalization;
using System.Linq;
using AwesomeAssertions;

namespace FlyleafLib;

/// <summary>
/// Unit tests for the pure, deterministic language-code mapping tables exposed by the engine:
/// <see cref="TesseractModel.TesseractLangToISO6391"/>, <see cref="WhisperLanguage.LanguageToCode"/> /
/// <see cref="WhisperLanguage.GetWhisperLanguages"/>, and the <see cref="Language"/> ISO 639-2 B/T
/// dictionaries plus the culture-resolution guard clauses.
///
/// Expectations are derived from the source tables (not from a prior run). The Tesseract / Whisper / ISO 639
/// tables are static immutable dictionaries with zero culture/ICU dependency, so their assertions are
/// environment-stable. <see cref="Language.StringToCulture"/> / <see cref="Language.ThreeLetterToCulture"/>
/// delegate to <see cref="CultureInfo.GetCultureInfo(string)"/>, whose behaviour for obscure 3-letter codes
/// is ICU/OS-version dependent — so only the rock-solid guard clauses, universal 2-letter BCP-47 lookups, and
/// the hardcoded special cases that target standard BCP-47 cultures are asserted here.
/// </summary>
public class LanguageMapperTests
{
    // === Tesseract OCR: TesseractLangToISO6391 ====================================================

    [Fact]
    public void TesseractLangToISO6391_HasExpectedActiveEntryCount()
    {
        // Cardinality tripwire: 105 uncommented entries in the source table. Adding/removing a mapping
        // forces this to be revisited (commented variants are intentionally excluded).
        TesseractModel.TesseractLangToISO6391.Should().HaveCount(105);
    }

    [Theory]
    [InlineData(TesseractOCR.Enums.Language.English, "en")]
    [InlineData(TesseractOCR.Enums.Language.Russian, "ru")]
    [InlineData(TesseractOCR.Enums.Language.German, "de")]
    [InlineData(TesseractOCR.Enums.Language.French, "fr")]
    [InlineData(TesseractOCR.Enums.Language.Japanese, "ja")]
    [InlineData(TesseractOCR.Enums.Language.Korean, "ko")]
    [InlineData(TesseractOCR.Enums.Language.ChineseSimplified, "zh")]
    [InlineData(TesseractOCR.Enums.Language.ChineseTraditional, "zh")]
    [InlineData(TesseractOCR.Enums.Language.Haitian, "ht")]
    [InlineData(TesseractOCR.Enums.Language.Kurmanji, "ku")]
    [InlineData(TesseractOCR.Enums.Language.Panjabi, "pa")]
    [InlineData(TesseractOCR.Enums.Language.Arabic, "ar")]
    [InlineData(TesseractOCR.Enums.Language.Vietnamese, "vi")]
    [InlineData(TesseractOCR.Enums.Language.Yoruba, "yo")]
    [InlineData(TesseractOCR.Enums.Language.FrenchMiddle, "zz")] // placeholder code, intentionally non-ISO
    public void TesseractLangToISO6391_MapsKnownLanguagesToExpectedCode(
        TesseractOCR.Enums.Language lang, string expectedIso)
    {
        TesseractModel.TesseractLangToISO6391[lang].Should().Be(expectedIso);
    }

    [Fact]
    public void TesseractLangToISO6391_BothChineseVariantsMapToZh()
    {
        // The table deliberately maps both Chinese scripts to the same "zh" key (special-cased downstream).
        TesseractModel.TesseractLangToISO6391.Values.Count(v => v == "zh").Should().Be(2);
    }

    [Fact]
    public void TesseractLangToISO6391_FrenchMiddleIsTheOnlyPlaceholderCode()
    {
        // "zz" is the only non-ISO placeholder value in the active table.
        TesseractModel.TesseractLangToISO6391.Values.Count(v => v == "zz").Should().Be(1);
    }

    [Fact]
    public void TesseractLangToISO6391_AllValuesAreTwoLowercaseLetters()
    {
        // Catches a typo'd 3-letter / uppercase value being introduced. "zz" still satisfies the shape.
        TesseractModel.TesseractLangToISO6391.Values
            .Should().OnlyContain(v => v.Length == 2 && char.IsLower(v[0]) && char.IsLower(v[1]));
    }

    [Fact]
    public void TesseractLangToISO6391_AllKeysAreDefinedEnumValues()
    {
        TesseractModel.TesseractLangToISO6391.Keys
            .Should().OnlyContain(k => System.Enum.IsDefined(k));
    }

    // === Whisper: LanguageToCode (reverse of CodeToLanguage) ======================================

    [Fact]
    public void WhisperLanguageToCode_HasOneEntryPerLanguage()
    {
        // Reverse map of the 100-entry CodeToLanguage table; all language names are unique so the
        // reverse map preserves cardinality.
        WhisperLanguage.LanguageToCode.Should().HaveCount(100);
    }

    [Theory]
    [InlineData("english", "en")]
    [InlineData("russian", "ru")]
    [InlineData("japanese", "ja")]
    [InlineData("chinese", "zh")]
    [InlineData("cantonese", "yue")]
    [InlineData("haitian creole", "ht")]
    [InlineData("javanese", "jw")] // whisper uses "jw", not the ISO 639-1 "jv"
    public void WhisperLanguageToCode_MapsNameToCode(string name, string expectedCode)
    {
        WhisperLanguage.LanguageToCode[name].Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("ENGLISH", "en")]
    [InlineData("Russian", "ru")]
    [InlineData("Haitian Creole", "ht")]
    public void WhisperLanguageToCode_LookupIsCaseInsensitive(string name, string expectedCode)
    {
        // Built with StringComparer.OrdinalIgnoreCase.
        WhisperLanguage.LanguageToCode[name].Should().Be(expectedCode);
    }

    [Fact]
    public void WhisperLanguageToCode_DoesNotContainUnknownName()
    {
        WhisperLanguage.LanguageToCode.ContainsKey("klingon").Should().BeFalse();
    }

    // === Whisper: GetWhisperLanguages ============================================================

    [Fact]
    public void GetWhisperLanguages_ReturnsOnePerCode()
    {
        WhisperLanguage.GetWhisperLanguages().Should().HaveCount(100);
    }

    [Fact]
    public void GetWhisperLanguages_HasDistinctCodes()
    {
        WhisperLanguage.GetWhisperLanguages().Select(l => l.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetWhisperLanguages_IsSortedByEnglishName()
    {
        // Production pins StringComparer.InvariantCulture (mirroring Language.AllLanguages), so the order is
        // deterministic on any host culture; assert with the same explicit comparer.
        WhisperLanguage.GetWhisperLanguages()
            .Should().BeInAscendingOrder(l => l.EnglishName, StringComparer.InvariantCulture);
    }

    [Fact]
    public void GetWhisperLanguages_OrderIsCultureInvariant_UnderCzechCulture()
    {
        // Culture guard for the pinned comparer: Czech collates "ch" as a single letter AFTER "h", so an
        // ambient-culture OrderBy under cs-CZ moves "Chinese" past every H-name (Haitian Creole … Hungarian)
        // — proven RED without the StringComparer.InvariantCulture pin. With the pin the order is identical
        // on every host culture, so these asserts are stable regardless of the ICU version (T-03 lesson:
        // only assert invariant-stable expectations).
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");

            var names = WhisperLanguage.GetWhisperLanguages().Select(l => l.EnglishName).ToList();

            names.Should().Equal(names.OrderBy(n => n, StringComparer.InvariantCulture));
            names.IndexOf("Chinese").Should().BeLessThan(names.IndexOf("Croatian"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("ru", "Russian")]
    [InlineData("zh", "Chinese")]
    [InlineData("yue", "Cantonese")]
    [InlineData("jw", "Javanese")]
    [InlineData("ht", "Haitian Creole")] // exercises UpperFirstOfWords across a word boundary
    public void GetWhisperLanguages_AppliesTitleCaseToEnglishName(string code, string expectedName)
    {
        WhisperLanguage.GetWhisperLanguages()
            .Should().Contain(l => l.Code == code && l.EnglishName == expectedName);
    }

    [Fact]
    public void GetWhisperLanguages_EveryEnglishNameStartsUppercaseAndIsNonEmpty()
    {
        WhisperLanguage.GetWhisperLanguages()
            .Should().OnlyContain(l => l.EnglishName.Length > 0 && char.IsUpper(l.EnglishName[0]));
    }

    [Fact]
    public void GetWhisperLanguages_RoundTripsThroughLanguageToCode()
    {
        // The displayed (title-cased) name lowercased must be a LanguageToCode key resolving back to the
        // same code — proves the two public facades stay consistent over the whole table.
        foreach (WhisperLanguage wl in WhisperLanguage.GetWhisperLanguages())
        {
            WhisperLanguage.LanguageToCode[wl.EnglishName.ToLowerInvariant()].Should().Be(wl.Code);
        }
    }

    // === Language: ISO 639-2 B<->T dictionaries ==================================================

    [Fact]
    public void Iso639Dictionaries_EachHaveTwentyEntries()
    {
        Language.ISO639_2T_TO_2B.Should().HaveCount(20);
        Language.ISO639_2B_TO_2T.Should().HaveCount(20);
    }

    [Fact]
    public void Iso639Dictionaries_AreExactInversesForward()
    {
        foreach (var kv in Language.ISO639_2T_TO_2B)
        {
            Language.ISO639_2B_TO_2T[kv.Value].Should().Be(kv.Key);
        }
    }

    [Fact]
    public void Iso639Dictionaries_AreExactInversesBackward()
    {
        foreach (var kv in Language.ISO639_2B_TO_2T)
        {
            Language.ISO639_2T_TO_2B[kv.Value].Should().Be(kv.Key);
        }
    }

    [Theory]
    [InlineData("deu", "ger")]
    [InlineData("fra", "fre")]
    [InlineData("zho", "chi")]
    [InlineData("nld", "dut")]
    public void Iso639_2T_TO_2B_MapsKnownCodes(string iso2t, string expected2b)
    {
        Language.ISO639_2T_TO_2B[iso2t].Should().Be(expected2b);
    }

    [Theory]
    [InlineData("ger", "deu")]
    [InlineData("fre", "fra")]
    [InlineData("chi", "zho")]
    public void Iso639_2B_TO_2T_MapsKnownCodes(string iso2b, string expected2t)
    {
        Language.ISO639_2B_TO_2T[iso2b].Should().Be(expected2t);
    }

    // === Language: StringToCulture guard clauses + stable lookups ================================

    [Fact]
    public void StringToCulture_Null_ReturnsNull()
    {
        Language.StringToCulture(null!).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("e")]   // length < 2
    [InlineData("a")]   // length < 2
    [InlineData("und")] // explicit undetermined sentinel
    public void StringToCulture_GuardInputs_ReturnNull(string input)
    {
        Language.StringToCulture(input).Should().BeNull();
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")] // lower-cased before lookup
    [InlineData("ru", "ru")]
    [InlineData("de", "de")]
    [InlineData("fr", "fr")]
    public void StringToCulture_TwoLetterCodes_ResolveExpectedCulture(string input, string expectedIso6391)
    {
        CultureInfo culture = Language.StringToCulture(input);

        culture.Should().NotBeNull();
        culture.TwoLetterISOLanguageName.Should().Be(expectedIso6391);
    }

    [Theory]
    [InlineData("english", "en")]
    [InlineData("russian", "ru")]
    public void StringToCulture_FullEnglishName_ResolvesViaFallbackScan(string input, string expectedIso6391)
    {
        // Input is neither a 2-letter code nor "und": CultureInfo.GetCultureInfo(input) throws, the bare
        // catch swallows it (ret stays null), then the fallback loop scans all cultures and matches one by
        // Name/NativeName/EnglishName (OrdinalIgnoreCase). This exercises BOTH the exception path and the
        // all-cultures fallback scan deterministically — a neutral culture's invariant EnglishName
        // ("English"/"Russian") is stable across .NET versions, so this is not ICU-fragile.
        CultureInfo culture = Language.StringToCulture(input);

        culture.Should().NotBeNull();
        culture.TwoLetterISOLanguageName.Should().Be(expectedIso6391);
    }

    // === Language: ThreeLetterToCulture hardcoded special cases ==================================

    [Theory]
    [InlineData("zht", "zh-Hant")]
    [InlineData("pob", "pt-BR")]
    [InlineData("tgl", "fil")] // Filipino is a first-class .NET neutral culture (stable Name)
    // Intentionally NOT asserting the "nor"->"nob" and "scc"->"srp" special cases: those resolve via
    // CultureInfo.GetCultureInfo("nob")/("srp"), whose availability and returned .Name are ICU/OS-version
    // dependent — asserting them would be brittle across the CI (.NET 10.x) vs local (.NET 11-preview) hosts.
    public void ThreeLetterToCulture_SpecialCases_MapToStandardCulture(string input, string expectedName)
    {
        CultureInfo culture = Language.ThreeLetterToCulture(input);

        culture.Should().NotBeNull();
        culture.Name.Should().Be(expectedName);
    }
}
