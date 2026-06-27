using System.Collections.Generic;
using System.Text.Json;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

public class AnkiConnectRequestsTests
{
    private static SavedWord Word(string term) =>
        new(term, "rd", "tr", "df", "ex", "de", "en", WordSource.WordClick, "t");

    [Fact]
    public void BuildVersion_ActionAndVersion()
    {
        using JsonDocument doc = JsonDocument.Parse(AnkiConnectRequests.BuildVersion());
        doc.RootElement.GetProperty("action").GetString().Should().Be("version");
        doc.RootElement.GetProperty("version").GetInt32().Should().Be(6);
    }

    [Fact]
    public void BuildCreateDeck_CarriesDeckName()
    {
        using JsonDocument doc = JsonDocument.Parse(AnkiConnectRequests.BuildCreateDeck("My Deck"));
        doc.RootElement.GetProperty("action").GetString().Should().Be("createDeck");
        doc.RootElement.GetProperty("version").GetInt32().Should().Be(6);
        doc.RootElement.GetProperty("params").GetProperty("deck").GetString().Should().Be("My Deck");
    }

    [Fact]
    public void BuildCreateModel_HasFiveFieldsAndOneTemplate()
    {
        using JsonDocument doc = JsonDocument.Parse(AnkiConnectRequests.BuildCreateModel());
        JsonElement p = doc.RootElement.GetProperty("params");

        doc.RootElement.GetProperty("action").GetString().Should().Be("createModel");
        p.GetProperty("modelName").GetString().Should().Be("LLPlayer");
        p.GetProperty("inOrderFields").GetArrayLength().Should().Be(5);
        p.GetProperty("inOrderFields")[0].GetString().Should().Be("Term");
        p.GetProperty("isCloze").GetBoolean().Should().BeFalse();

        JsonElement tmpl = p.GetProperty("cardTemplates")[0];
        tmpl.GetProperty("Name").GetString().Should().Be("Card 1");
        tmpl.GetProperty("Front").GetString().Should().Contain("{{Term}}");
        tmpl.GetProperty("Back").GetString().Should().Contain("{{Translation}}");
    }

    [Fact]
    public void BuildAddNotes_ActionVersionAndNoteCount()
    {
        string json = AnkiConnectRequests.BuildAddNotes([Word("a"), Word("b")], "Deck");
        using JsonDocument doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("action").GetString().Should().Be("addNotes");
        doc.RootElement.GetProperty("version").GetInt32().Should().Be(6);
        doc.RootElement.GetProperty("params").GetProperty("notes").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void BuildAddNotes_FirstNote_HasAllFiveFields_AndDeckModelOptionsTags()
    {
        string json = AnkiConnectRequests.BuildAddNotes(
            [new SavedWord("T", "R", "Tr", "D", "E", "de", "en", WordSource.WordClick, "t")], "Deck");
        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement note = doc.RootElement.GetProperty("params").GetProperty("notes")[0];
        note.GetProperty("deckName").GetString().Should().Be("Deck");
        note.GetProperty("modelName").GetString().Should().Be("LLPlayer");

        JsonElement fields = note.GetProperty("fields");
        fields.GetProperty("Term").GetString().Should().Be("T");
        fields.GetProperty("Reading").GetString().Should().Be("R");
        fields.GetProperty("Translation").GetString().Should().Be("Tr");
        fields.GetProperty("Definition").GetString().Should().Be("D");
        fields.GetProperty("Example").GetString().Should().Be("E");

        note.GetProperty("options").GetProperty("allowDuplicate").GetBoolean().Should().BeFalse();
        note.GetProperty("options").GetProperty("duplicateScope").GetString().Should().Be("deck");
        note.GetProperty("tags")[0].GetString().Should().Be("llplayer");
    }

    [Fact]
    public void BuildAddNotes_EmptyList_ProducesEmptyNotesArray()
    {
        using JsonDocument doc = JsonDocument.Parse(AnkiConnectRequests.BuildAddNotes([], "Deck"));
        doc.RootElement.GetProperty("params").GetProperty("notes").GetArrayLength().Should().Be(0);
    }
}
