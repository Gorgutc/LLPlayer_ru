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
    public void CustomVoiceIds_SetWithBlanksAndDuplicates_Normalizes()
    {
        DubbingConfig config = new();

        config.CustomVoiceIds =
        [
            " custom-one ",
            "",
            "CUSTOM-ONE",
            null!,
            "custom-two",
            " custom-two ",
        ];

        config.CustomVoiceIds.Should().Equal("custom-one", "custom-two");
    }

    [Fact]
    public void CustomVoiceIds_JsonWithBlanksAndDuplicates_Normalizes()
    {
        DubbingConfig? config = JsonSerializer.Deserialize<DubbingConfig>(
            """{"CustomVoiceIds":[" custom-one ","","CUSTOM-ONE",null,"custom-two"," custom-two "]}""");

        config.Should().NotBeNull();
        config!.CustomVoiceIds.Should().Equal("custom-one", "custom-two");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DefaultVoiceId_SetBlank_NormalizesToBuiltInDefault(string? value)
    {
        DubbingConfig config = new();

        config.DefaultVoiceId = value!;

        config.DefaultVoiceId.Should().Be("ru-preset-1");
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
    public void DefaultVoiceId_SetBuiltInWithDifferentCase_IsCanonicalized()
    {
        DubbingConfig config = new();

        config.DefaultVoiceId = "  RU-PRESET-2  ";

        config.DefaultVoiceId.Should().Be("ru-preset-2");
    }

    [Fact]
    public void DefaultVoiceId_JsonWithSurroundingWhitespace_IsTrimmed()
    {
        DubbingConfig? config = JsonSerializer.Deserialize<DubbingConfig>(
            """{"DefaultVoiceId":"  my-voice  "}""");

        config.Should().NotBeNull();
        config!.DefaultVoiceId.Should().Be("my-voice");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DefaultVoiceId_JsonBlank_NormalizesToBuiltInDefault(string? value)
    {
        string json = value is null
            ? """{"DefaultVoiceId":null}"""
            : $$"""{"DefaultVoiceId":"{{value}}"}""";

        DubbingConfig? config = JsonSerializer.Deserialize<DubbingConfig>(json);

        config.Should().NotBeNull();
        config!.DefaultVoiceId.Should().Be("ru-preset-1");
    }
}
