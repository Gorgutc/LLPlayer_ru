using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Whisper.net.Ggml;

namespace FlyleafLib;

public class WhisperCppModelTests
{
    // Mirrors the real persistence path: the player config is loaded/saved through
    // AppConfig.GetJsonSerializerOptions(), which registers a JsonStringEnumConverter, so enums persist as STRINGS.
    private static JsonSerializerOptions JsonOpts() => new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ---- ModelFileName: OFF-path (NoQuantization) must stay byte-identical to the legacy name ----

    [Fact]
    public void ModelFileName_NoQuantization_IsLegacyName()
    {
        new WhisperCppModel { Model = GgmlType.Base }.ModelFileName
            .Should().Be("ggml-base.bin");

        // explicit NoQuantization yields the identical string (default == NoQuantization == 0)
        new WhisperCppModel { Model = GgmlType.Base, Quantization = QuantizationType.NoQuantization }.ModelFileName
            .Should().Be("ggml-base.bin");
    }

    [Theory]
    [InlineData(GgmlType.Base, QuantizationType.Q5_0, "ggml-base-q5_0.bin")]
    [InlineData(GgmlType.Base, QuantizationType.Q5_1, "ggml-base-q5_1.bin")]
    [InlineData(GgmlType.Base, QuantizationType.Q8_0, "ggml-base-q8_0.bin")]
    [InlineData(GgmlType.Medium, QuantizationType.Q8_0, "ggml-medium-q8_0.bin")]
    [InlineData(GgmlType.LargeV3Turbo, QuantizationType.Q5_1, "ggml-largev3turbo-q5_1.bin")]
    public void ModelFileName_Quantized_UsesWhisperCppConvention(GgmlType model, QuantizationType quant, string expected)
    {
        new WhisperCppModel { Model = model, Quantization = quant }.ModelFileName
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(GgmlType.Base)]
    [InlineData(GgmlType.Small)]
    [InlineData(GgmlType.LargeV3Turbo)]
    public void ModelFileName_LegacyAndQuant_NeverCollide(GgmlType model)
    {
        string full = new WhisperCppModel { Model = model }.ModelFileName;

        foreach (QuantizationType q in new[] { QuantizationType.Q5_0, QuantizationType.Q5_1, QuantizationType.Q8_0 })
        {
            new WhisperCppModel { Model = model, Quantization = q }.ModelFileName
                .Should().NotBe(full);
        }
    }

    // ---- ToString: label shows the quant only for a quantized variant ----

    [Fact]
    public void ToString_NoQuantization_IsModelOnly()
    {
        new WhisperCppModel { Model = GgmlType.Base }.ToString()
            .Should().Be("Base");
    }

    [Fact]
    public void ToString_Quantized_IncludesQuant()
    {
        new WhisperCppModel { Model = GgmlType.Base, Quantization = QuantizationType.Q5_1 }.ToString()
            .Should().Be("Base (q5_1)");
    }

    // ---- Equals / GetHashCode must distinguish quantization (#1 regression risk) ----

    [Fact]
    public void Equals_DistinguishesQuantization()
    {
        var full = new WhisperCppModel { Model = GgmlType.Base };
        var q51 = new WhisperCppModel { Model = GgmlType.Base, Quantization = QuantizationType.Q5_1 };
        var q51b = new WhisperCppModel { Model = GgmlType.Base, Quantization = QuantizationType.Q5_1 };
        var smallQ51 = new WhisperCppModel { Model = GgmlType.Small, Quantization = QuantizationType.Q5_1 };

        full.Equals(q51).Should().BeFalse();
        q51.Equals(q51b).Should().BeTrue();
        q51.Equals(smallQ51).Should().BeFalse();

        // legacy <-> legacy equality preserved (back-compat selection restore)
        full.Equals(new WhisperCppModel { Model = GgmlType.Base }).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_MatchesEquals()
    {
        var q51 = new WhisperCppModel { Model = GgmlType.Base, Quantization = QuantizationType.Q5_1 };
        var q51b = new WhisperCppModel { Model = GgmlType.Base, Quantization = QuantizationType.Q5_1 };
        var full = new WhisperCppModel { Model = GgmlType.Base };

        // equal => equal hashes
        q51.GetHashCode().Should().Be(q51b.GetHashCode());
        // different quant => different hash (so they don't collapse in a HashSet/Distinct)
        full.GetHashCode().Should().NotBe(q51.GetHashCode());
    }

    // ---- Persistence: string-enum round-trip + absent-defaulting back-compat ----

    [Fact]
    public void JsonRoundTrip_PreservesQuantization_AsString()
    {
        var model = new WhisperCppModel { Model = GgmlType.Base, Quantization = QuantizationType.Q5_1 };

        string json = JsonSerializer.Serialize(model, JsonOpts());

        // string-enum (not the integer 4) — this is what production actually writes
        json.Should().Contain("Q5_1");

        WhisperCppModel? roundTripped = JsonSerializer.Deserialize<WhisperCppModel>(json, JsonOpts());

        roundTripped.Should().NotBeNull();
        roundTripped!.Model.Should().Be(GgmlType.Base);
        roundTripped.Quantization.Should().Be(QuantizationType.Q5_1);
        roundTripped.ModelFileName.Should().Be("ggml-base-q5_1.bin");
    }

    [Fact]
    public void BackCompat_LegacyJsonWithoutQuantization_DefaultsToNoQuantization()
    {
        // A config written before 0.3.21 has no "Quantization" key.
        const string legacyJson = """{ "Model": "Base" }""";

        WhisperCppModel? model = JsonSerializer.Deserialize<WhisperCppModel>(legacyJson, JsonOpts());

        model.Should().NotBeNull();
        model!.Model.Should().Be(GgmlType.Base);
        model.Quantization.Should().Be(QuantizationType.NoQuantization);
        model.ModelFileName.Should().Be("ggml-base.bin");
    }
}
