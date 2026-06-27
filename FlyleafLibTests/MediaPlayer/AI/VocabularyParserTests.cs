using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

public class VocabularyParserTests
{
    [Fact]
    public void Parse_CleanFiveFields_MapsAllPositionally()
    {
        var entries = VocabularyParser.Parse("casa | ka-sa | house | a dwelling | I live in a casa");

        entries.Should().ContainSingle();
        VocabularyEntry e = entries[0];
        e.Term.Should().Be("casa");
        e.Reading.Should().Be("ka-sa");
        e.Translation.Should().Be("house");
        e.Definition.Should().Be("a dwelling");
        e.Example.Should().Be("I live in a casa");
    }

    [Fact]
    public void Parse_StripsNumberedAndBulletedMarkers()
    {
        var entries = VocabularyParser.Parse(
            "1. alpha | a | first\n" +
            "2) beta | b | second\n" +
            "- gamma | g | third\n" +
            "* delta | d | fourth");

        entries.Select(e => e.Term).Should().Equal("alpha", "beta", "gamma", "delta");
        entries[0].Translation.Should().Be("first");
    }

    [Fact]
    public void Parse_MarkdownTable_DropsHeaderAndSeparator_KeepsRows()
    {
        var entries = VocabularyParser.Parse(
            "| term | reading | translation | definition | example |\n" +
            "| --- | --- | --- | --- | --- |\n" +
            "| casa | | house | building | en la casa |\n" +
            "| perro | | dog | animal | el perro ladra |");

        entries.Select(e => e.Term).Should().Equal("casa", "perro");
        entries[0].Translation.Should().Be("house");
        entries[0].Reading.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MissingTrailingFields_PadWithEmpty()
    {
        var entries = VocabularyParser.Parse("word | wd | meaning");

        entries.Should().ContainSingle();
        entries[0].Term.Should().Be("word");
        entries[0].Reading.Should().Be("wd");
        entries[0].Translation.Should().Be("meaning");
        entries[0].Definition.Should().BeEmpty();
        entries[0].Example.Should().BeEmpty();
    }

    [Fact]
    public void Parse_HeaderRowWithoutTablePipes_IsIgnored()
    {
        var entries = VocabularyParser.Parse(
            "term | reading | translation | definition | example\n" +
            "real | | actual | true | it is real");

        entries.Should().ContainSingle().Which.Term.Should().Be("real");
    }

    [Fact]
    public void Parse_ProseParagraphWithoutPipes_IsSkipped()
    {
        var entries = VocabularyParser.Parse(
            "Here are some useful words from the transcript that you might want to study.\n" +
            "casa | | house | | en la casa");

        entries.Should().ContainSingle().Which.Term.Should().Be("casa");
    }

    [Fact]
    public void Parse_ShortNoPipeToken_AcceptedAsTermOnly()
    {
        var entries = VocabularyParser.Parse("serendipity");

        entries.Should().ContainSingle();
        entries[0].Term.Should().Be("serendipity");
        entries[0].Translation.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DeduplicatesByTerm_CaseInsensitive_FirstWins()
    {
        var entries = VocabularyParser.Parse(
            "casa | | house\n" +
            "Casa | | home");

        entries.Should().ContainSingle();
        entries[0].Translation.Should().Be("house"); // first wins
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("```\n```")]
    [InlineData("A long explanatory sentence the model added with no structure at all.")]
    [InlineData("---\n***\n===")]
    public void Parse_GarbageOrEmpty_ReturnsEmpty_NeverThrows(string reply)
    {
        VocabularyParser.Parse(reply).Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullReply_ReturnsEmpty()
    {
        VocabularyParser.Parse(null).Should().BeEmpty();
    }

    [Fact]
    public void Merge_DeduplicatesAcrossChunks_AndCaps()
    {
        var a = VocabularyParser.Parse("one | | 1\ntwo | | 2");
        var b = VocabularyParser.Parse("TWO | | dos\nthree | | 3\nfour | | 4");

        var merged = VocabularyParser.Merge([a, b], cap: 3);

        merged.Select(e => e.Term).Should().Equal("one", "two", "three"); // "TWO" dropped as dup, capped at 3
    }

    [Fact]
    public void ToTsv_HasHeaderAndTabSeparatedRows()
    {
        var entries = new List<VocabularyEntry>
        {
            new("casa", "", "house", "building", "en la casa"),
        };

        string tsv = VocabularyParser.ToTsv(entries);

        tsv.Split('\n')[0].Should().Be("Term\tReading\tTranslation\tDefinition\tExample");
        tsv.Should().Contain("casa\t\thouse\tbuilding\ten la casa");
    }

    [Fact]
    public void ToTsv_NeutralizesEmbeddedTabsAndNewlines()
    {
        var entries = new List<VocabularyEntry>
        {
            new("a\tb", "r", "line1\nline2", "", ""),
        };

        string tsv = VocabularyParser.ToTsv(entries);

        // No row may contain a stray tab/newline beyond the 4 column separators.
        string row = tsv.Split('\n')[1];
        row.Count(c => c == '\t').Should().Be(4);
    }
}
