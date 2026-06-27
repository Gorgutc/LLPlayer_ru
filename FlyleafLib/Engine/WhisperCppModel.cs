using System.IO;
using System.Text.Json.Serialization;
using Whisper.net.Ggml;

namespace FlyleafLib;

#nullable enable

public class WhisperCppModel : NotifyPropertyChanged, IEquatable<WhisperCppModel>
{
    public GgmlType Model { get; set; }

    // whisper.cpp quantization. NoQuantization = the full model (legacy behavior: byte-identical filename and
    // label). Quantized variants (q5_0/q5_1/q8_0) trade a little accuracy for smaller files and faster ASR.
    // Persisted as a string alongside Model; absent in pre-0.3.21 configs it deserializes to NoQuantization, so
    // an existing selection still resolves the same ggml-{model}.bin (no migration needed).
    public QuantizationType Quantization { get; set; }

    [JsonIgnore]
    public long Size
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(Downloaded));
            }
        }
    }

    [JsonIgnore]
    public string ModelFileName
    {
        get
        {
            string modelName = Model.ToString().ToLower();
            // NoQuantization keeps the legacy "ggml-{model}.bin" byte-identical so existing downloads/configs still
            // resolve. Quantized variants follow the whisper.cpp convention "ggml-{model}-{quant}.bin" (e.g.
            // ggml-base-q5_1.bin), guaranteeing they never collide with the full model on disk. ToLowerInvariant on
            // the quant token keeps it locale-safe (the model segment stays ToLower() for byte-identity).
            string suffix = Quantization == QuantizationType.NoQuantization
                ? string.Empty
                : $"-{Quantization.ToString().ToLowerInvariant()}";
            return $"ggml-{modelName}{suffix}.bin";
        }
    }

    [JsonIgnore]
    public string ModelFilePath => Path.Combine(WhisperConfig.ModelsDirectory, ModelFileName);

    [JsonIgnore]
    public bool Downloaded => Size > 0;

    public override string ToString() => Quantization == QuantizationType.NoQuantization
        ? Model.ToString()
        : $"{Model} ({Quantization.ToString().ToLowerInvariant()})";

    public bool Equals(WhisperCppModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Model == other.Model && Quantization == other.Quantization;
    }

    public override bool Equals(object? obj) => obj is WhisperCppModel o && Equals(o);

    public override int GetHashCode()
    {
        return HashCode.Combine((int)Model, (int)Quantization);
    }
}
