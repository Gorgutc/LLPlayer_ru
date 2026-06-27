using System.IO;
using FlyleafLib;
using Whisper.net.Ggml;

namespace LLPlayer.Services;

public class WhisperCppModelLoader
{
    // Quantizations offered per model, NoQuantization first so the full model leads each group and
    // Models.First() stays the legacy default. q5_0/q5_1/q8_0 are the broadly-published whisper.cpp quant
    // levels on the download mirror (smaller/faster); q4_* is intentionally omitted (lower quality, spottier
    // availability). An offered combo that is missing on the server is handled gracefully at download time.
    private static readonly QuantizationType[] OfferedQuantizations =
    [
        QuantizationType.NoQuantization,
        QuantizationType.Q5_0,
        QuantizationType.Q5_1,
        QuantizationType.Q8_0,
    ];

    public static List<WhisperCppModel> LoadAllModels()
    {
        WhisperConfig.EnsureModelsDirectory();

        List<WhisperCppModel> models =
            (from t in Enum.GetValues<GgmlType>()
             from q in OfferedQuantizations
             select new WhisperCppModel { Model = t, Quantization = q })
            .ToList();

        foreach (WhisperCppModel model in models)
        {
            // Update download status
            string path = model.ModelFilePath;
            if (File.Exists(path))
            {
                model.Size = new FileInfo(path).Length;
            }
        }

        return models;
    }

    public static List<WhisperCppModel> LoadDownloadedModels()
    {
        return LoadAllModels()
            .Where(m => m.Downloaded)
            .ToList();
    }
}
