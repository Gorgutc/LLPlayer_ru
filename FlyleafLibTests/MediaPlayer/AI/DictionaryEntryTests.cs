using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

public class DictionaryEntryTests
{
    private static DictionaryEntry Make() => new("dog", "/dog/",
    [
        new DictionarySense("noun", "a domesticated carnivore", "the dog barked"),
        new DictionarySense("verb", "to follow persistently", ""),
        new DictionarySense("noun", "a despicable person", ""),
        new DictionarySense("noun", "an extra sense", ""),
    ]);

    [Fact]
    public void FlattenForDisplay_IncludesReadingPartOfSpeechDefinitionAndExample()
    {
        string s = Make().FlattenForDisplay();

        s.Should().StartWith("/dog/");
        s.Should().Contain("(noun) a domesticated carnivore");
        s.Should().Contain("- \"the dog barked\"");
        s.Should().Contain("(verb) to follow persistently");
    }

    [Fact]
    public void FlattenForDisplay_CapsAtMaxSenses()
    {
        string s = Make().FlattenForDisplay(maxSenses: 2);

        s.Should().Contain("a domesticated carnivore");
        s.Should().Contain("to follow persistently");
        s.Should().NotContain("a despicable person");
    }

    [Fact]
    public void ToFields_ReadingIsPhonetic_DefinitionExcludesReadingLine()
    {
        (string reading, string definition) = Make().ToFields(maxSenses: 2);

        reading.Should().Be("/dog/");
        // The definition starts with a sense (not the reading line) — pins Flatten(includeReading:false)
        // independent of the specific phonetic string's content.
        definition.Should().StartWith("(noun)");
        definition.Should().NotContain("/dog/");
        definition.Should().Contain("a domesticated carnivore");
        definition.Should().Contain("to follow persistently");
        definition.Should().NotContain("a despicable person"); // capped to 2 senses
    }

    [Fact]
    public void Empty_IsEmpty_AndProjectsToBlankFields()
    {
        DictionaryEntry.Empty.IsEmpty.Should().BeTrue();
        DictionaryEntry.Empty.FlattenForDisplay().Should().BeEmpty();

        (string reading, string definition) = DictionaryEntry.Empty.ToFields();
        reading.Should().BeEmpty();
        definition.Should().BeEmpty();
    }

    [Fact]
    public void Flatten_SkipsSensesWithBlankDefinition()
    {
        DictionaryEntry e = new("x", "",
        [
            new DictionarySense("noun", "   ", ""),
            new DictionarySense("verb", "real def", ""),
        ]);

        e.FlattenForDisplay().Should().Be("(verb) real def");
    }
}
