using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

public class WordDefinitionPromptsTests
{
    [Fact]
    public void BuildDefinition_SubstitutesLanguagesWordAndContext()
    {
        OpenAIMessage[] msgs = WordDefinitionPrompts.BuildDefinition("perro", "Spanish", "English", "el perro corre");

        msgs.Should().HaveCount(2);
        msgs[0].role.Should().Be("system");
        msgs[1].role.Should().Be("user");

        string all = msgs[0].content + "\n" + msgs[1].content;
        all.Should().Contain("Spanish");
        all.Should().Contain("English");
        all.Should().Contain("perro");
        all.Should().Contain("el perro corre");
    }

    [Fact]
    public void BuildDefinition_NullSourceLang_UsesFallbackPhrase_AndNoContextMarker()
    {
        OpenAIMessage[] msgs = WordDefinitionPrompts.BuildDefinition("x", null, "English", null);

        msgs[0].content.Should().Contain("the source language");
        msgs[1].content.Should().Contain("(none)"); // no context sentence supplied
    }

    [Fact]
    public void ParseLlmReply_PipeForm_SplitsReadingAndDefinition()
    {
        DictionaryEntry e = WordDefinitionPrompts.ParseLlmReply("/per.ro/ | a dog", "perro");

        e.IsEmpty.Should().BeFalse();
        e.Reading.Should().Be("/per.ro/");
        e.Senses.Should().ContainSingle();
        e.Senses[0].Definition.Should().Be("a dog");
    }

    [Fact]
    public void ParseLlmReply_NoPipe_TreatsWholeLineAsDefinition()
    {
        DictionaryEntry e = WordDefinitionPrompts.ParseLlmReply("a friendly greeting", "hello");

        e.Reading.Should().BeEmpty();
        e.Senses[0].Definition.Should().Be("a friendly greeting");
    }

    [Fact]
    public void ParseLlmReply_TakesFirstNonEmptyLine()
    {
        DictionaryEntry e = WordDefinitionPrompts.ParseLlmReply("\n\n  /x/ | def one\nsecond line", "w");

        e.Reading.Should().Be("/x/");
        e.Senses[0].Definition.Should().Be("def one");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|")]        // pipe but empty definition
    [InlineData("/x/ |   ")] // reading but empty definition
    public void ParseLlmReply_EmptyOrNoDefinition_ReturnsEmpty(string reply)
    {
        WordDefinitionPrompts.ParseLlmReply(reply, "w").IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ParseLlmReply_CapsLongDefinition()
    {
        string longDef = new('a', WordDefinitionBudget.MaxDefinitionChars + 100);

        DictionaryEntry e = WordDefinitionPrompts.ParseLlmReply("| " + longDef, "w");

        e.Senses[0].Definition.Length.Should().BeLessThanOrEqualTo(WordDefinitionBudget.MaxDefinitionChars);
    }
}
