using System.Text.Json;
using AwesomeAssertions;

namespace FlyleafLib;

public class DubbingConfigTests
{
    [Fact]
    public void CustomVoiceIds_SetNull_NormalizesToEmptyList()
    {
        DubbingConfig config = new();

        config.CustomVoiceIds = null!;

        config.CustomVoiceIds.Should().NotBeNull();
        config.CustomVoiceIds.Should().BeEmpty();
    }

    [Fact]
    public void CustomVoiceIds_JsonNull_NormalizesToEmptyList()
    {
        DubbingConfig? config = JsonSerializer.Deserialize<DubbingConfig>(
            """{"CustomVoiceIds":null}""");

        config.Should().NotBeNull();
        config!.CustomVoiceIds.Should().NotBeNull();
        config.CustomVoiceIds.Should().BeEmpty();
    }

    [Fact]
    public void DefaultVoiceId_SetWithSurroundingWhitespace_IsTrimmed()
    {
        // A hand-edited DefaultVoiceId with surrounding whitespace must normalize so the bound ComboBox
        // SelectedValue equals the (trimmed) picker entries and the engine receives a clean voice id.
        DubbingConfig config = new();

        config.DefaultVoiceId = "  my-voice  ";

        config.DefaultVoiceId.Should().Be("my-voice");
    }

    [Fact]
    public void DefaultVoiceId_JsonWithSurroundingWhitespace_IsTrimmed()
    {
        DubbingConfig? config = JsonSerializer.Deserialize<DubbingConfig>(
            """{"DefaultVoiceId":"  my-voice  "}""");

        config.Should().NotBeNull();
        config!.DefaultVoiceId.Should().Be("my-voice");
    }
}
