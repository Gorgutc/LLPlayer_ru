using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

public class SavedWordTests
{
    private static SavedWord Make(string term = "Haus") =>
        new(term, "haus", "house", "a building", "Das ist ein Haus.", "de", "en", WordSource.WordClick, "2026-06-27T10:00:00Z");

    [Fact]
    public void DedupeKey_IsTrimmedLowercase()
    {
        Make("  Haus  ").DedupeKey.Should().Be("haus");
    }

    [Fact]
    public void DedupeKey_IsCaseInsensitive_EqualForDifferentCasing()
    {
        Make("HAUS").DedupeKey.Should().Be(Make("haus").DedupeKey);
    }

    [Fact]
    public void ToVocabularyEntry_PreservesFiveCardFields()
    {
        VocabularyEntry e = Make().ToVocabularyEntry();

        e.Term.Should().Be("Haus");
        e.Reading.Should().Be("haus");
        e.Translation.Should().Be("house");
        e.Definition.Should().Be("a building");
        e.Example.Should().Be("Das ist ein Haus.");
    }

    [Fact]
    public void FromVocabularyEntry_CopiesFieldsAndProvenance()
    {
        VocabularyEntry v = new("perro", "pe-rro", "dog", "an animal", "El perro corre.");

        SavedWord w = SavedWord.FromVocabularyEntry(v, "es", "en", WordSource.AiInsights, "2026-06-27T11:00:00Z");

        w.Term.Should().Be("perro");
        w.Reading.Should().Be("pe-rro");
        w.Translation.Should().Be("dog");
        w.Definition.Should().Be("an animal");
        w.Example.Should().Be("El perro corre.");
        w.SourceLanguage.Should().Be("es");
        w.TargetLanguage.Should().Be("en");
        w.Source.Should().Be(WordSource.AiInsights);
        w.SavedAtUtc.Should().Be("2026-06-27T11:00:00Z");
    }

    [Fact]
    public void NormalizeNulls_ReplacesNullStringsWithEmpty()
    {
        SavedWord w = new("term", null!, null!, null!, null!, null!, null!, WordSource.WordClick, null!);

        SavedWord n = w.NormalizeNulls();

        n.Term.Should().Be("term");
        n.Reading.Should().Be("");
        n.Translation.Should().Be("");
        n.Definition.Should().Be("");
        n.Example.Should().Be("");
        n.SourceLanguage.Should().Be("");
        n.TargetLanguage.Should().Be("");
        n.SavedAtUtc.Should().Be("");
    }
}
