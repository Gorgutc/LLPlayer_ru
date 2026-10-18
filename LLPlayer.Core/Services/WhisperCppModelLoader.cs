using System.IO;
using FlyleafLib;
using Whisper.net.Ggml;

namespace LLPlayer.Core.Services;

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

    /// <summary>Inspect existing weights without creating directories or changing global model lookup.</summary>
    public static List<WhisperCppModel> DiscoverDownloadedModels(string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new ArgumentException("The model directory must be an absolute path.", nameof(directory));
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The model directory does not exist.");
        List<WhisperCppModel> models = CreateCatalog();
        foreach (WhisperCppModel model in models)
        {
            string path = Path.Combine(directory, model.ModelFileName);
            model.Size = File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        return models.Where(model => model.Downloaded).ToList();
    }

    public static List<WhisperCppModel> LoadAllModels()
    {
        return LoadAllModels(WhisperConfig.EnsureModelsDirectory,
            path => File.Exists(path) ? new FileInfo(path).Length : null);
    }

    internal static List<WhisperCppModel> LoadAllModels(Action ensureDirectory, Func<string, long?> probeFileLength)
    {
        ensureDirectory();

        List<WhisperCppModel> models = CreateCatalog();

        foreach (WhisperCppModel model in models)
        {
            // Update download status
            string path = model.ModelFilePath;
            model.Size = probeFileLength(path) ?? 0;
        }

        return models;
    }

    private static List<WhisperCppModel> CreateCatalog() =>
        (from type in Enum.GetValues<GgmlType>()
         from quantization in OfferedQuantizations
         select new WhisperCppModel { Model = type, Quantization = quantization }).ToList();

    public static List<WhisperCppModel> LoadDownloadedModels()
    {
        return LoadDownloadedModels(WhisperConfig.EnsureModelsDirectory,
            path => File.Exists(path) ? new FileInfo(path).Length : null);
    }

    internal static List<WhisperCppModel> LoadDownloadedModels(Action ensureDirectory, Func<string, long?> probeFileLength)
    {
        return LoadAllModels(ensureDirectory, probeFileLength)
            .Where(m => m.Downloaded)
            .ToList();
    }
}
