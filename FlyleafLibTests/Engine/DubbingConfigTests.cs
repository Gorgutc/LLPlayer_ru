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
}
