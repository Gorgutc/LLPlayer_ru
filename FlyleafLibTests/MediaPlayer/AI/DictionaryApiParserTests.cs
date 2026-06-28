using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

public class DictionaryApiParserTests
{
    // A trimmed but structurally faithful dictionaryapi.dev success body (ASCII phonetic to keep the file ASCII).
    private const string Success =
        "[{\"word\":\"hello\",\"phonetic\":\"/heh-loh/\"," +
        "\"meanings\":[{\"partOfSpeech\":\"noun\",\"definitions\":[" +
        "{\"definition\":\"a greeting\",\"example\":\"she said hello\"}," +
        "{\"definition\":\"an utterance of hello\"}]}]}]";

    [Fact]
    public void Parse_Success_ReadsTermPhoneticAndSenses()
    {
        DictionaryEntry e = DictionaryApiParser.ParseDictionaryApiDev(Success);

        e.IsEmpty.Should().BeFalse();
        e.Term.Should().Be("hello");
        e.Reading.Should().Be("/heh-loh/");
        e.Senses.Should().HaveCount(2);
        e.Senses[0].PartOfSpeech.Should().Be("noun");
        e.Senses[0].Definition.Should().Be("a greeting");
        e.Senses[0].Example.Should().Be("she said hello");
        e.Senses[1].Example.Should().BeEmpty(); // a definition without an example is tolerated
    }

    [Fact]
    public void Parse_NotFound404Object_ReturnsEmpty()
    {
        // The dictionaryapi.dev 404 body is a JSON OBJECT, not an array.
        string json = "{\"title\":\"No Definitions Found\",\"message\":\"Sorry pal\",\"resolution\":\"try again\"}";

        DictionaryApiParser.ParseDictionaryApiDev(json).IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData("[]")]          // empty array
    [InlineData("not json")]    // not JSON at all
    [InlineData("")]            // empty
    [InlineData("   ")]         // whitespace
    [InlineData("123")]         // a number, not an array
    public void Parse_UnexpectedOrEmpty_ReturnsEmpty_NeverThrows(string json)
    {
        DictionaryApiParser.ParseDictionaryApiDev(json).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Parse_PhoneticFallsBackToPhoneticsArray()
    {
        string json =
            "[{\"word\":\"x\",\"phonetics\":[{\"text\":\"\"},{\"text\":\"/eks/\"}]," +
            "\"meanings\":[{\"partOfSpeech\":\"noun\",\"definitions\":[{\"definition\":\"the letter x\"}]}]}]";

        DictionaryApiParser.ParseDictionaryApiDev(json).Reading.Should().Be("/eks/");
    }

    [Fact]
    public void Parse_PrefersTopLevelPhoneticOverPhoneticsArray()
    {
        // The top-level "phonetic" wins over phonetics[].text; a swap of the two scans would fail this.
        string json =
            "[{\"word\":\"x\",\"phonetic\":\"/top/\",\"phonetics\":[{\"text\":\"/arr/\"}]," +
            "\"meanings\":[{\"partOfSpeech\":\"noun\",\"definitions\":[{\"definition\":\"d\"}]}]}]";

        DictionaryApiParser.ParseDictionaryApiDev(json).Reading.Should().Be("/top/");
    }

    [Fact]
    public void Parse_NoUsableDefinition_ReturnsEmpty()
    {
        // meanings present but the only definition string is blank.
        string json =
            "[{\"word\":\"x\",\"meanings\":[{\"partOfSpeech\":\"noun\",\"definitions\":[{\"definition\":\"\"}]}]}]";

        DictionaryApiParser.ParseDictionaryApiDev(json).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Parse_AggregatesSensesAcrossMultipleEntries()
    {
        string json =
            "[{\"word\":\"bow\",\"meanings\":[{\"partOfSpeech\":\"noun\",\"definitions\":[{\"definition\":\"a knot\"}]}]}," +
            "{\"word\":\"bow\",\"meanings\":[{\"partOfSpeech\":\"verb\",\"definitions\":[{\"definition\":\"to bend\"}]}]}]";

        DictionaryEntry e = DictionaryApiParser.ParseDictionaryApiDev(json);

        e.Senses.Should().HaveCount(2);
        e.Senses[0].Definition.Should().Be("a knot");
        e.Senses[1].Definition.Should().Be("to bend");
    }
}
