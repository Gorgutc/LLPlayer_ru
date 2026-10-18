using AwesomeAssertions;
using System.Diagnostics;
using System.Text.Json;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace FlyleafLib.Platform;

[Collection("PortableEngine")]
public sealed class PortableAsrTests
{
    [Fact]
    public void InteractiveWhisperWithoutModel_ReportsModelInsteadOfWindowsCrt()
    {
        Config config = new(true);
        SubtitlesASR asr = new(new SubtitlesManager(config, 2), config);

        asr.CanExecute(out string error, out string action).Should().BeFalse();

        error.Should().Contain("model is not set");
        action.Should().Be(KnownErrorActionKeys.DownloadWhisperModel);
    }

    [Fact]
    public void BatchWhisperWithoutModel_ReportsModelInsteadOfWindowsCrt()
    {
        Action create = () => new BatchAsrTranscriber(new Config(true));

        create.Should().Throw<InvalidOperationException>().WithMessage("*model is not set*");
    }

    [Fact]
    public async Task MissingWhisperModel_ReportsExactPathInInteractiveAndBatch()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"llplayer-empty-models-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { await RunChild(nameof(MissingModelChild), directory); }
        finally { Directory.Delete(directory); }
    }

    [Fact(Explicit = true)]
    public void MissingModelChild()
    {
        string marker = RequireChild();
        string directory = Environment.GetEnvironmentVariable("LLPLAYER_WHISPER_MODELS_DIR")!;
        WhisperConfig.ModelsDirectory.Should().Be(directory);
        Config config = new(true);
        config.Subtitles.WhisperCppConfig.Model = new WhisperCppModel { Model = GgmlType.LargeV3, Quantization = QuantizationType.NoQuantization };
        string path = config.Subtitles.WhisperCppConfig.Model.ModelFilePath;
        SubtitlesASR asr = new(new SubtitlesManager(config, 2), config);
        asr.CanExecute(out string error, out string action).Should().BeFalse();
        error.Should().Contain(path);
        action.Should().Be(KnownErrorActionKeys.DownloadWhisperModel);
        Action create = () => new BatchAsrTranscriber(config);
        create.Should().Throw<InvalidOperationException>().Where(e => e.Message.Contains(path));
        File.WriteAllText(marker, "missing model preflight passed");
    }

    [Theory]
    [InlineData("relative/models")]
    [InlineData("   ")]
    public async Task InvalidModelDirectoryOverride_DoesNotSilentlyFallback(string directory)
        => await RunChild(nameof(InvalidModelDirectoryChild), directory);

    [Fact(Explicit = true)]
    public void InvalidModelDirectoryChild()
    {
        string marker = RequireChild();
        Action resolve = () => _ = WhisperConfig.ModelsDirectory;
        resolve.Should().Throw<ArgumentException>().WithMessage("*LLPLAYER_WHISPER_MODELS_DIR*");
        File.WriteAllText(marker, "invalid override rejected");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AbsentModelDirectoryOverride_PreservesAppBaseDefault(string? directory)
        => await RunChild(nameof(DefaultModelDirectoryChild), directory);

    [Fact(Explicit = true)]
    public void DefaultModelDirectoryChild()
    {
        string marker = RequireChild();
        WhisperConfig.ModelsDirectory.Should().Be(Path.Combine(AppContext.BaseDirectory, "whispermodels"));
        File.WriteAllText(marker, "appbase default preserved");
    }

    [Fact]
    public void BatchWhisper_DoesNotRequireWindowsCrtOnPortableTarget()
    {
        BatchAsrTranscriber.RequiresVcRedistPreflight(new Config(true)).Should().BeFalse();
    }

    [Fact]
    public void PortableWhisperFactory_DisablesGpuEvenForPersistedGpuDevice()
    {
        WhisperCppConfig config = new() { GpuDevice = 3 };

        config.GetFactoryOptions().UseGpu.Should().BeFalse();
    }

    [Fact]
    public async Task CpuWhisperRuntime_LoadsNativeApiWithoutAModel()
        => await RunChild(nameof(CpuWhisperChild));

    [Fact]
    public async Task MissingNativeRuntime_IsReportedAtActualServiceLoadBoundary()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"llplayer-no-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            // Copy only managed test-host files; no runtimes/ tree is present in the isolated appbase.
            foreach (string file in Directory.GetFiles(AppContext.BaseDirectory))
                if (!file.EndsWith(".so", StringComparison.Ordinal))
                    File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            await RunChild(nameof(MissingNativeChild), directory, Path.Combine(directory, "FlyleafLibTests.dll"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact(Explicit = true)]
    public void MissingNativeChild()
    {
        string marker = RequireChild();
        StartHeadlessEngine();
        Config config = new(true);
        config.Subtitles.WhisperCppConfig.Model = new WhisperCppModel { Model = GgmlType.Tiny };
        config.Subtitles.WhisperCppConfig.RuntimeLibraries = [RuntimeLibrary.Cpu];
        Action load = () => new WhisperCppASRService(config);
        var failure = load.Should().Throw<InvalidOperationException>().WithMessage("*CPU Whisper native libraries*");
        failure.Which.InnerException.Should().BeOfType<FileNotFoundException>();
        if (OperatingSystem.IsWindows())
            failure.Which.Message.Should().Contain(Path.Combine("runtimes", "win-x64")).And.Contain("VCRUNTIME140");
        else
            failure.Which.Message.Should().Contain(Path.Combine("runtimes", "linux-x64")).And.Contain("libgomp.so.1");
        File.WriteAllText(marker, failure.Which.ToString());
    }

    static async Task RunChild(string method, string? modelsDirectory = null, string? assembly = null)
    {
        string marker = Path.Combine(Path.GetTempPath(), $"llplayer-whisper-{Guid.NewGuid():N}.txt");
        try
        {
            string host = Path.Combine(Path.GetFullPath(Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../..")),
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            ProcessStartInfo start = new(host) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { assembly ?? typeof(PortableAsrTests).Assembly.Location, "-method",
                $"{typeof(PortableAsrTests).FullName}.{method}", "-explicit", "only", "-noLogo" })
                start.ArgumentList.Add(arg);
            start.Environment["LLPLAYER_WHISPER_SMOKE_RESULT"] = marker;
            if (modelsDirectory is null) start.Environment.Remove("LLPLAYER_WHISPER_MODELS_DIR");
            else start.Environment["LLPLAYER_WHISPER_MODELS_DIR"] = modelsDirectory;
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                process.ExitCode.Should().Be(0, $"isolated child must pass: {await stdout} {await stderr}");
                File.Exists(marker).Should().BeTrue("the child must execute its assertions");
                File.ReadAllText(marker).Should().NotBeNullOrWhiteSpace();
            }
            finally
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
        }
        finally { File.Delete(marker); }
    }

    static string RequireChild()
    {
        string? marker = Environment.GetEnvironmentVariable("LLPLAYER_WHISPER_SMOKE_RESULT");
        Assert.SkipWhen(string.IsNullOrEmpty(marker), "Smoke child is launched by its parent test.");
        return marker!;
    }

    static void StartHeadlessEngine()
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        Directory.Exists(ffmpeg).Should().BeTrue("engine-service smoke requires LLPLAYER_FFMPEG_DIR");
        Utils.IsTesting = false;
        Engine.Start(new EngineConfig
        {
            FFmpegPath = ffmpeg, PluginsPath = null, UIRefresh = false,
            LogLevel = LogLevel.Quiet, FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Quiet
        });
        Engine.IsLoaded.Should().BeTrue();
    }

    [Fact(Explicit = true)]
    public void CpuWhisperChild()
    {
        string marker = RequireChild();
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
        string? info = WhisperFactory.GetRuntimeInfo();
        info.Should().NotBeNullOrWhiteSpace();
        WhisperFactory.GetSupportedLanguages().Should().Contain("en");
        RuntimeOptions.LoadedLibrary.Should().Be(RuntimeLibrary.Cpu);
        File.WriteAllText(marker!, info!);
    }

    // Owner-run smoke on a separately provisioned full model. Normal gates exercise only model-free native API.
    [Fact(Explicit = true)]
    public async Task LargeV3CpuInferenceChild()
    {
        string? sample = Environment.GetEnvironmentVariable("LLPLAYER_WHISPER_TEST_WAV");
        string? result = Environment.GetEnvironmentVariable("LLPLAYER_WHISPER_INFERENCE_RESULT");
        Assert.SkipWhen(string.IsNullOrEmpty(sample) || string.IsNullOrEmpty(result), "Explicit full-model inference requires sample and evidence paths.");
        StartHeadlessEngine();
        Config config = new(true);
        config.Subtitles.WhisperCppConfig.Model = new WhisperCppModel { Model = GgmlType.LargeV3 };
        config.Subtitles.WhisperCppConfig.RuntimeLibraries = [RuntimeLibrary.Cpu];
        config.Subtitles.WhisperCppConfig.Threads = Math.Min(Environment.ProcessorCount, 8);
        config.Subtitles.WhisperConfig.LanguageDetection = false;
        config.Subtitles.WhisperConfig.Language = "en";
        string model = config.Subtitles.WhisperCppConfig.Model.ModelFilePath;
        File.Exists(model).Should().BeTrue("the owner-provisioned full model must exist at the resolved upstream filename");
        config.Subtitles.WhisperCppConfig.GetFactoryOptions().UseGpu.Should().BeFalse();
        Stopwatch watch = Stopwatch.StartNew();
        await using WhisperCppASRService service = new(config);
        using MemoryStream wave = new(await File.ReadAllBytesAsync(sample!, TestContext.Current.CancellationToken));
        List<(string text, TimeSpan start, TimeSpan end, string language)> cues = [];
        await foreach (var cue in service.Do(wave, TestContext.Current.CancellationToken))
            cues.Add((cue.text, cue.start, cue.end, cue.language));
        watch.Stop();
        string text = string.Join(" ", cues.Select(cue => cue.text));
        text.Should().ContainEquivalentOf("country").And.ContainEquivalentOf("do for you");
        cues.Should().NotBeEmpty();
        cues.Should().OnlyContain(cue => cue.start >= TimeSpan.Zero && cue.end > cue.start &&
            cue.end <= TimeSpan.FromSeconds(12) && cue.language == "en");
        cues.Select(cue => cue.start).Should().BeInAscendingOrder();
        RuntimeOptions.LoadedLibrary.Should().Be(RuntimeLibrary.Cpu);
        File.WriteAllText(result!, JsonSerializer.Serialize(new
        {
            model, modelBytes = new FileInfo(model).Length,
            modelSha256 = Environment.GetEnvironmentVariable("LLPLAYER_WHISPER_MODEL_SHA256"),
            sample, runtime = RuntimeOptions.LoadedLibrary.ToString(), useGpu = false,
            threads = config.Subtitles.WhisperCppConfig.Threads, elapsedSeconds = watch.Elapsed.TotalSeconds,
            text, cues = cues.Select(cue => new { cue.text, start = cue.start.TotalSeconds, end = cue.end.TotalSeconds, cue.language })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
