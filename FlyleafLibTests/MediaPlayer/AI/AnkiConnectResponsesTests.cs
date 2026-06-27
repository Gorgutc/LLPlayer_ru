using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

public class AnkiConnectResponsesTests
{
    [Fact]
    public void GetError_NoError_ReturnsNull()
    {
        AnkiConnectResponses.GetError("""{"result":[1,2],"error":null}""").Should().BeNull();
    }

    [Fact]
    public void GetError_WithError_ReturnsString()
    {
        AnkiConnectResponses.GetError("""{"result":null,"error":"boom"}""").Should().Be("boom");
    }

    [Fact]
    public void GetError_MalformedOrEmpty_ReturnsNull()
    {
        AnkiConnectResponses.GetError("{not json").Should().BeNull();
        AnkiConnectResponses.GetError("").Should().BeNull();
        AnkiConnectResponses.GetError(null).Should().BeNull();
    }

    [Fact]
    public void IsAlreadyExists_MatchesCaseInsensitively()
    {
        AnkiConnectResponses.IsAlreadyExists("Model name already exists").Should().BeTrue();
        AnkiConnectResponses.IsAlreadyExists("Deck ALREADY EXISTS").Should().BeTrue();
        AnkiConnectResponses.IsAlreadyExists("some other error").Should().BeFalse();
        AnkiConnectResponses.IsAlreadyExists(null).Should().BeFalse();
    }

    [Fact]
    public void ParseAddNotes_AllAdded_NoSkips()
    {
        AnkiAddNotesOutcome o = AnkiConnectResponses.ParseAddNotes("""{"result":[1496198395707,1496198395708],"error":null}""", 2);
        o.Added.Should().Be(2);
        o.Skipped.Should().Be(0);
        o.Error.Should().BeNull();
    }

    [Fact]
    public void ParseAddNotes_NullIdsCountedAsSkipped()
    {
        // AnkiConnect returns null for a duplicate note it refused to add.
        AnkiAddNotesOutcome o = AnkiConnectResponses.ParseAddNotes("""{"result":[1496198395707,null,1496198395709],"error":null}""", 3);
        o.Added.Should().Be(2);
        o.Skipped.Should().Be(1);
        o.Error.Should().BeNull();
    }

    [Fact]
    public void ParseAddNotes_ErrorField_ReturnsError()
    {
        AnkiAddNotesOutcome o = AnkiConnectResponses.ParseAddNotes("""{"result":null,"error":"deck was not found"}""", 3);
        o.Added.Should().Be(0);
        o.Error.Should().Be("deck was not found");
    }

    [Fact]
    public void ParseAddNotes_MalformedOrEmpty_ReturnsError()
    {
        AnkiConnectResponses.ParseAddNotes("{bad", 1).Error.Should().NotBeNull();
        AnkiConnectResponses.ParseAddNotes("", 1).Error.Should().NotBeNull();
        AnkiConnectResponses.ParseAddNotes(null, 1).Error.Should().NotBeNull();
    }
}
