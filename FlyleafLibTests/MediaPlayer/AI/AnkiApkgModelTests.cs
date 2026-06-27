using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

public class AnkiApkgModelTests
{
    private static SavedWord Word(string term, string reading = "", string translation = "tr",
        string definition = "def", string example = "ex") =>
        new(term, reading, translation, definition, example, "de", "en", WordSource.WordClick, "t");

    private static ApkgContent Build(params SavedWord[] words) =>
        AnkiApkgModel.BuildContent(words, "My Deck", 1_700_000_000L, 1_700_000_000_000L);

    [Fact]
    public void BuildContent_OneNoteAndCardPerUniqueWord()
    {
        ApkgContent c = Build(Word("a"), Word("b"), Word("c"));

        c.Notes.Should().HaveCount(3);
        c.Cards.Should().HaveCount(3);
    }

    [Fact]
    public void BuildContent_DeduplicatesTerm_CaseInsensitive()
    {
        ApkgContent c = Build(Word("Haus"), Word("HAUS"), Word(" haus "));

        c.Notes.Should().HaveCount(1);
        c.Notes[0].Sfld.Should().Be("Haus");
    }

    [Fact]
    public void BuildContent_SkipsBlankTerms()
    {
        ApkgContent c = Build(Word("real"), Word("   "), Word(""));
        c.Notes.Should().HaveCount(1);
    }

    [Fact]
    public void Note_Flds_AreUnitSeparatorJoined_InFieldOrder()
    {
        ApkgContent c = Build(new SavedWord("Term0", "Read1", "Tr2", "Def3", "Ex4",
            "de", "en", WordSource.WordClick, "t"));

        string[] parts = c.Notes[0].Flds.Split('');
        parts.Should().Equal("Term0", "Read1", "Tr2", "Def3", "Ex4");
        c.Notes[0].Sfld.Should().Be("Term0");
    }

    [Fact]
    public void Guid_IsDeterministic_AndStablePerTerm()
    {
        ApkgContent c1 = Build(Word("stable"));
        ApkgContent c2 = Build(Word("stable"));

        c1.Notes[0].Guid.Should().Be(c2.Notes[0].Guid);
        c1.Notes[0].Guid.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Guid_DiffersForDifferentTerms()
    {
        ApkgContent c = Build(Word("one"), Word("two"));
        c.Notes[0].Guid.Should().NotBe(c.Notes[1].Guid);
    }

    [Fact]
    public void FieldChecksum_MatchesAnkiFormula_Sha1First8Hex()
    {
        // SHA1("test") = a94a8fe5ccb19ba61c4c0873d391e987982fbbd3 → first 8 hex "a94a8fe5" = 0xA94A8FE5 = 2840236005.
        AnkiApkgModel.FieldChecksum("test").Should().Be(2_840_236_005L);
    }

    [Fact]
    public void Notes_And_Cards_HaveSequentialIds_FromNowMs()
    {
        ApkgContent c = Build(Word("a"), Word("b"));

        // id_gen starts at nowMs; note then its card consume consecutive ids.
        c.Notes[0].Id.Should().Be(1_700_000_000_000L);
        c.Cards[0].Id.Should().Be(1_700_000_000_001L);
        c.Notes[1].Id.Should().Be(1_700_000_000_002L);
        c.Cards[1].Id.Should().Be(1_700_000_000_003L);
    }

    [Fact]
    public void Card_LinksToNote_AndDeck_WithIncreasingDue()
    {
        ApkgContent c = Build(Word("a"), Word("b"));

        c.Cards[0].Nid.Should().Be(c.Notes[0].Id);
        c.Cards[1].Nid.Should().Be(c.Notes[1].Id);
        c.Cards.Should().OnlyContain(card => card.Did == AnkiApkgModel.DeckId && card.Ord == 0);
        c.Cards.Select(card => card.Due).Should().Equal(1, 2);
    }

    [Fact]
    public void Note_Mid_IsModelId()
    {
        ApkgContent c = Build(Word("a"));
        c.Notes[0].Mid.Should().Be(AnkiApkgModel.ModelId);
    }

    [Fact]
    public void ModelsJson_HasFiveFields_OneTemplate_StringId()
    {
        ApkgContent c = Build(Word("a"));

        using JsonDocument doc = JsonDocument.Parse(c.ModelsJson);
        JsonElement model = doc.RootElement.GetProperty(AnkiApkgModel.ModelId.ToString());

        model.GetProperty("id").GetString().Should().Be(AnkiApkgModel.ModelId.ToString());
        model.GetProperty("name").GetString().Should().Be("LLPlayer");
        model.GetProperty("flds").GetArrayLength().Should().Be(5);
        model.GetProperty("tmpls").GetArrayLength().Should().Be(1);
        model.GetProperty("flds")[0].GetProperty("name").GetString().Should().Be("Term");
    }

    [Fact]
    public void DecksJson_ContainsDefaultAndOurDeck_WithName()
    {
        ApkgContent c = Build(Word("a"));

        using JsonDocument doc = JsonDocument.Parse(c.DecksJson);
        doc.RootElement.TryGetProperty("1", out _).Should().BeTrue();
        JsonElement ours = doc.RootElement.GetProperty(AnkiApkgModel.DeckId.ToString());
        ours.GetProperty("name").GetString().Should().Be("My Deck");
    }

    [Fact]
    public void ConfJson_CurModel_IsOurModelId()
    {
        ApkgContent c = Build(Word("a"));

        using JsonDocument doc = JsonDocument.Parse(c.ConfJson);
        doc.RootElement.GetProperty("curModel").GetString().Should().Be(AnkiApkgModel.ModelId.ToString());
    }

    [Fact]
    public void BuildContent_BlankDeckName_FallsBackToLLPlayer()
    {
        ApkgContent c = AnkiApkgModel.BuildContent([Word("a")], "   ", 1L, 1000L);

        using JsonDocument doc = JsonDocument.Parse(c.DecksJson);
        doc.RootElement.GetProperty(AnkiApkgModel.DeckId.ToString())
            .GetProperty("name").GetString().Should().Be("LLPlayer");
    }

    [Fact]
    public void BuildContent_TimeUnits_ColModAndScmAreMs_CrtAndRowModAreSeconds()
    {
        // Anki/genanki: col.crt + note.mod + card.mod are epoch SECONDS; col.mod + col.scm are epoch MILLISECONDS.
        ApkgContent c = AnkiApkgModel.BuildContent([Word("a")], "D", 1_700_000_000L, 1_700_000_000_000L);

        c.Crt.Should().Be(1_700_000_000L);
        c.Mod.Should().Be(1_700_000_000_000L);
        c.Scm.Should().Be(1_700_000_000_000L);
        c.Ver.Should().Be(11);
        c.Notes[0].Mod.Should().Be(1_700_000_000L);
        c.Cards[0].Mod.Should().Be(1_700_000_000L);
    }

    [Fact]
    public void GuidFor_KnownValue_PinsSha256Base91Algorithm()
    {
        // base91 of the first 8 bytes of SHA-256("test"), matching genanki's guid_for (SHA-256, NOT SHA-1).
        AnkiApkgModel.GuidFor("test").Should().Be("A=PyK.*)?=");
    }
}
