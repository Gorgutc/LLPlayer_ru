using System.Text.Json;
using FlyleafLib;
using LLPlayer.Core.Services;
using System.Diagnostics;

namespace FlyleafLibTests.Core;

public class RecognitionParitySettingsTests
{
    [Fact]
    public void Snapshot_captures_both_recognition_engines_chunk_limits_and_ocr_regions()
    {
        Config.SubtitlesConfig source = new() { Languages = [Language.English], ASREngine = SubASREngineType.FasterWhisper, ASRChunkSizeMB = 37, ASRChunkSeconds = 41 };
        source.FasterWhisperConfig.Model = "small";
        source.FasterWhisperConfig.Prompt = " exact prompt ";
        source.FasterWhisperConfig.ExtraArguments = "--device cpu --beam_size 3";
        source[0].OCREngine = SubOCREngineType.MicrosoftOCR;
        source[1].OCREngine = SubOCREngineType.Tesseract;
        source.TesseractOcrRegions = new() { ["zh"] = "chi_tra" };
        source.MsOcrRegions = new() { ["en"] = "en-GB" };
        RecognitionSettings snapshot = RecognitionSettings.FromConfig(source);
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        JsonElement root = json.RootElement;
        Assert.True(root.TryGetProperty("ASREngine", out _), "The detached recognition draft must retain the selected ASR engine.");
        Assert.Equal(37, root.GetProperty("ASRChunkSizeMB").GetInt32());
        Assert.Equal(41, root.GetProperty("ASRChunkSeconds").GetInt32());
        Assert.Equal("small", root.GetProperty("FasterWhisper").GetProperty("Model").GetString());
        Assert.Equal("chi_tra", root.GetProperty("TesseractOcrRegions").GetProperty("zh").GetString());
        Assert.Equal("en-GB", root.GetProperty("MsOcrRegions").GetProperty("en").GetString());
        Config.SubtitlesConfig target = new();
        snapshot.Apply(target);
        Assert.Equal(source.ASREngine, target.ASREngine);
        Assert.Equal(source[0].OCREngine, target[0].OCREngine);
        Assert.Equal(source[1].OCREngine, target[1].OCREngine);
        Assert.Equal(source.FasterWhisperConfig.Prompt, target.FasterWhisperConfig.Prompt);
        Assert.Equal(source.FasterWhisperConfig.ExtraArguments, target.FasterWhisperConfig.ExtraArguments);
        target.TesseractOcrRegions["zh"] = "chi_sim";
        Assert.Equal("chi_tra", source.TesseractOcrRegions["zh"]);
    }

    [Fact]
    public void Default_faster_whisper_executable_matches_the_running_operating_system()
    {
        Assert.Equal(OperatingSystem.IsWindows() ? "faster-whisper-xxl.exe" : "faster-whisper-xxl",
            Path.GetFileName(FasterWhisperConfig.DefaultEnginePath));
    }

    [Fact]
    public void Recognition_roundtrip_is_detached_and_old_files_preserve_absent_defaults()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"Whisper\":{\"Language\":\"ru\"}}");
            RecognitionSettingsStore store = new(path);
            RecognitionSettings legacy = store.Load()!;
            Assert.Equal(SubASREngineType.WhisperCpp, legacy.ASREngine);
            Assert.Equal(20, legacy.ASRChunkSizeMB);
            Assert.Equal(20, legacy.ASRChunkSeconds);
            Assert.True(legacy.FasterWhisper.AntiHallucination);
            Assert.Equal("--device cpu", legacy.FasterWhisper.ExtraArguments);
            Assert.Equal(SubOCREngineType.Tesseract, legacy.OcrPrimaryEngine);
            legacy.ASREngine = SubASREngineType.FasterWhisper;
            legacy.ASRChunkSizeMB = int.MaxValue;
            legacy.OcrSecondaryEngine = SubOCREngineType.MicrosoftOCR;
            legacy.FasterWhisper = new() { Model = "small", Prompt = " exact\n ", AntiHallucination = false,
                ExtraArguments = "--device cpu --beam_size 3", ProcessPriority = ProcessPriorityClass.BelowNormal };
            legacy.TesseractOcrRegions["zh"] = "chi_tra";
            legacy.MsOcrRegions["en"] = "en-GB";
            store.Save(legacy);
            var loaded = store.Load()!;
            Config.SubtitlesConfig config = new(); loaded.Apply(config);
            Assert.Equal((long)int.MaxValue * 1024 * 1024, config.ASRChunkSize);
            Assert.Equal(SubASREngineType.FasterWhisper, config.ASREngine);
            Assert.Equal(SubOCREngineType.MicrosoftOCR, config[1].OCREngine);
            Assert.Equal(ProcessPriorityClass.BelowNormal, config.FasterWhisperConfig.ProcessPriority);
            Assert.Equal(" exact\n ", config.FasterWhisperConfig.Prompt);
            Assert.False(config.FasterWhisperConfig.AntiHallucination);
            loaded.FasterWhisper.Prompt = "changed";
            loaded.TesseractOcrRegions["zh"] = "chi_sim";
            Assert.Equal(" exact\n ", config.FasterWhisperConfig.Prompt);
            Assert.Equal("chi_tra", config.TesseractOcrRegions["zh"]);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"ASREngine\":999}")]
    [InlineData("{\"OcrPrimaryEngine\":999}")]
    [InlineData("{\"ASRChunkSizeMB\":0}")]
    [InlineData("{\"ASRChunkSeconds\":-1}")]
    [InlineData("{\"FasterWhisper\":null}")]
    [InlineData("{\"FasterWhisper\":{\"UseManualEngine\":true,\"ManualEnginePath\":\"relative\"}}")]
    [InlineData("{\"TesseractOcrRegions\":{\"en\":\"../../bad\"}}")]
    [InlineData("{\"TesseractOcrRegions\":{\"en\":\"chi_tra\"}}")]
    [InlineData("{\"MsOcrRegions\":{\"en\":\"ru-RU\"}}")]
    [InlineData("{\"WhisperCpp\":{\"NoSpeechThreshold\":1.5}}")]
    [InlineData("{\"WhisperCpp\":{\"GpuDevice\":-1}}")]
    public void Invalid_extended_file_is_preserved_with_a_warning(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            RecognitionSettingsStore store = new(path);
            Assert.Null(store.Load());
            Assert.NotNull(store.LoadWarning);
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
