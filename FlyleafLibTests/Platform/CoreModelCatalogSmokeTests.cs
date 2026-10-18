using System.Diagnostics;
using FlyleafLib;
using LLPlayer.Core.Services;
using Whisper.net.Ggml;

namespace FlyleafLibTests.Platform;

public class CoreModelCatalogSmokeTests
{
    [Fact]
    public async Task Public_catalog_adapter_creates_directory_and_reads_file_lengths()
    {
        string root = Path.Combine(Path.GetTempPath(), $"llplayer-catalog-{Guid.NewGuid():N}");
        string directory = Path.Combine(root, "models");
        string marker = Path.Combine(root, "passed.txt");
        Directory.CreateDirectory(root);
        try
        {
            string host = Path.Combine(Path.GetFullPath(Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../..")), "dotnet");
            ProcessStartInfo start = new(host) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { typeof(CoreModelCatalogSmokeTests).Assembly.Location, "-method",
                $"{typeof(CoreModelCatalogSmokeTests).FullName}.{nameof(PublicCatalogChild)}", "-explicit", "only", "-noLogo" })
                start.ArgumentList.Add(arg);
            start.Environment["LLPLAYER_WHISPER_MODELS_DIR"] = directory;
            start.Environment["LLPLAYER_CORE_CATALOG_RESULT"] = marker;
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                Assert.True(process.ExitCode == 0, $"Isolated catalog child failed: {await stdout} {await stderr}");
                Assert.Equal("passed", File.ReadAllText(marker));
            }
            finally
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact(Explicit = true)]
    public void PublicCatalogChild()
    {
        string? marker = Environment.GetEnvironmentVariable("LLPLAYER_CORE_CATALOG_RESULT");
        Assert.SkipWhen(string.IsNullOrEmpty(marker), "Catalog child is launched by its parent test.");
        Assert.False(Directory.Exists(WhisperConfig.ModelsDirectory));
        var first = WhisperCppModelLoader.LoadAllModels();
        Assert.True(Directory.Exists(WhisperConfig.ModelsDirectory));
        Assert.All(first, row => Assert.False(row.Downloaded));
        File.WriteAllBytes(first[0].ModelFilePath, []);
        File.WriteAllBytes(first[1].ModelFilePath, [1, 2, 3]);
        var second = WhisperCppModelLoader.LoadAllModels();
        Assert.False(second[0].Downloaded);
        Assert.Equal(3, second[1].Size);
        Assert.NotSame(first[1], second[1]);
        var downloaded = Assert.Single(WhisperCppModelLoader.LoadDownloadedModels());
        Assert.Equal(second[1], downloaded);
        Assert.Equal(3, downloaded.Size);
        Directory.Delete(WhisperConfig.ModelsDirectory, true);
        File.WriteAllText(WhisperConfig.ModelsDirectory, "blocks directory creation");
        Assert.Throws<IOException>(() => WhisperCppModelLoader.LoadAllModels());
        Assert.Throws<IOException>(() => WhisperCppModelLoader.LoadDownloadedModels());
        File.WriteAllText(marker!, "passed");
    }

    [Fact]
    public void Explicit_real_model_directory_catalog_probe_is_read_only()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LLPLAYER_TEST_CORE_MODEL_CATALOG") == "1",
            "Set LLPLAYER_TEST_CORE_MODEL_CATALOG=1 and LLPLAYER_WHISPER_MODELS_DIR for catalog smoke.");
        string? directory = Environment.GetEnvironmentVariable("LLPLAYER_WHISPER_MODELS_DIR");
        Assert.NotNull(directory);
        Assert.True(Path.IsPathFullyQualified(directory));
        Assert.Equal(Path.GetFullPath(directory), WhisperConfig.ModelsDirectory);
        Assert.True(Directory.Exists(directory));
        var path = Path.Combine(directory, "ggml-large-v3.bin");
        long length = new FileInfo(path).Length;
        Assert.True(length > 0);
        var row = Assert.Single(WhisperCppModelLoader.LoadAllModels(), row =>
            row.Model == GgmlType.LargeV3 && row.Quantization == QuantizationType.NoQuantization);
        Assert.Equal(path, row.ModelFilePath);
        Assert.Equal(length, row.Size);
        Assert.True(row.Downloaded);
    }
}
