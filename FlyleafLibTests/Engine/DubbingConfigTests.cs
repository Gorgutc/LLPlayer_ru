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

    // HC-29: the atempo bounds are free TextBoxes. A typo (0.15 for 1.15) or a 0/negative must not reach the
    // sidecar, where a sub-1 max slows overflowing clips and a <= 0 rate makes librosa.time_stretch throw and
    // fails the whole file's dub. The setter clamps to [0.5, 2.0]; in-range values (incl. the 0.9 default) pass.
    [Theory]
    [InlineData(0.15, 0.5)]
    [InlineData(0.0, 0.5)]
    [InlineData(-1.0, 0.5)]
    [InlineData(3.0, 2.0)]
    [InlineData(1.15, 1.15)]
    [InlineData(0.9, 0.9)]
    public void AtempoMax_SetOutOfRange_IsClampedToSaneRange(double input, double expected)
    {
        DubbingConfig config = new();

        config.AtempoMax = input;

        config.AtempoMax.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(-2.0, 0.5)]
    [InlineData(5.0, 2.0)]
    [InlineData(0.9, 0.9)]
    public void AtempoMin_SetOutOfRange_IsClampedToSaneRange(double input, double expected)
    {
        DubbingConfig config = new();

        config.AtempoMin = input;

        config.AtempoMin.Should().Be(expected);
    }

    [Fact]
    public void AtempoMax_JsonTypo_IsClampedOnDeserialize()
    {
        // The classic 0.15-for-1.15 typo in a hand-edited config must be neutralized on load, not survive to
        // the sidecar. STJ uses the property setter, so the clamp applies to deserialization too.
        DubbingConfig? config = JsonSerializer.Deserialize<DubbingConfig>("""{"AtempoMax":0.15}""");

        config.Should().NotBeNull();
        config!.AtempoMax.Should().Be(0.5);
    }

    [Fact]
    public void AtempoDefaults_AreUnchanged()
    {
        // Byte-identical guard: the field initializers do not run through the clamping setter, and 0.9/1.15 are
        // in range anyway, so the default dub is unaffected by HC-29.
        DubbingConfig config = new();

        config.AtempoMin.Should().Be(0.9);
        config.AtempoMax.Should().Be(1.15);
    }

    // HC-45: OutputFormat was the only DubbingConfig field a hand-edited config could set to an un-encodable
    // value. A stray "part"/"tmp" reaches the output filename and, because ResolveExistingRussianDubPath skips
    // .part/.tmp fragments, leaves DubExistsAnyFormat=false forever (permanent re-render + the auto-loader
    // never attaches the output). The setter now whitelists FLAC (the only container the sidecar encodes);
    // anything outside the set — including blank/null — coerces to "flac".
    [Theory]
    [InlineData("part", "flac")]
    [InlineData("tmp", "flac")]
    [InlineData("mp3", "flac")]
    [InlineData(".FLAC ", "flac")]
    [InlineData("", "flac")]
    [InlineData(null, "flac")]
    [InlineData("flac", "flac")]
    public void OutputFormat_SetUnsupportedOrBlank_CoercesToFlac(string? input, string expected)
    {
        DubbingConfig config = new();

        config.OutputFormat = input!;

        config.OutputFormat.Should().Be(expected);
    }

    [Fact]
    public void OutputFormat_JsonUnsupported_CoercesOnDeserialize()
    {
        // A hand-edited "part"/"tmp"/mislabelled container must be neutralized on load, not survive to the
        // filename. STJ uses the property setter, so the whitelist applies to deserialization too.
        DubbingConfig? config = JsonSerializer.Deserialize<DubbingConfig>("""{"OutputFormat":"part"}""");

        config.Should().NotBeNull();
        config!.OutputFormat.Should().Be("flac");
    }

    [Fact]
    public void OutputFormat_DefaultIsFlac_Unchanged()
    {
        // Byte-identical guard: the field initializer (DefaultExtension = "flac") is in-whitelist, so the
        // default dub container is unaffected by HC-45.
        DubbingConfig config = new();

        config.OutputFormat.Should().Be("flac");
    }
}
