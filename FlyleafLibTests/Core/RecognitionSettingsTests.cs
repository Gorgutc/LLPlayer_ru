using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;
using LLPlayer.Core.Services;
using Whisper.net.Ggml;

namespace FlyleafLibTests.Core;

public class RecognitionSettingsTests
{
    [Fact]
    public void Explicit_directory_resolver_and_snapshot_preserve_legacy_default()
    {
        var model = new WhisperCppModel { Model = GgmlType.LargeV3 };
        Config.SubtitlesConfig config = new() { Languages = [Language.English] };
        config.WhisperCppConfig.Model = model;
        Assert.Equal(model.ModelFilePath, config.WhisperCppConfig.GetModelFilePath());
        config.WhisperCppConfig.ModelDirectory = Path.GetTempPath();
        var snapshot = BatchSubtitleConfigSnapshot.CreateSubtitlesConfig(config);
        Assert.Equal(Path.Combine(Path.GetTempPath(), model.ModelFileName), snapshot.WhisperCppConfig.GetModelFilePath());
        Assert.NotSame(model, snapshot.WhisperCppConfig.Model);
    }

    [Fact]
    public void Explicit_directory_catalog_reads_positive_files_and_never_creates_missing_directory()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() => WhisperCppModelLoader.DiscoverDownloadedModels(dir));
        Assert.False(Directory.Exists(dir));
        Directory.CreateDirectory(dir);
        try
        {
            WhisperCppModel large = new() { Model = GgmlType.LargeV3, Quantization = QuantizationType.NoQuantization };
            WhisperCppModel empty = new() { Model = GgmlType.Base, Quantization = QuantizationType.NoQuantization };
            File.WriteAllBytes(Path.Combine(dir, large.ModelFileName), [1, 2]);
            File.WriteAllBytes(Path.Combine(dir, empty.ModelFileName), []);
            var model = Assert.Single(WhisperCppModelLoader.DiscoverDownloadedModels(dir));
            Assert.Equal(GgmlType.LargeV3, model.Model);
            Assert.Equal(QuantizationType.NoQuantization, model.Quantization);
            Assert.Equal(2, model.Size);
            Assert.Equal(2, Directory.GetFiles(dir).Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Store_defaults_corrupt_preservation_and_roundtrip_advanced_fields()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "recognition.json");
            var store = new RecognitionSettingsStore(path);
            Assert.Null(store.Load());
            Assert.False(File.Exists(path));
            File.WriteAllText(path, "{broken");
            Assert.Null(store.Load());
            Assert.NotNull(store.LoadWarning);
            Assert.Equal("{broken", File.ReadAllText(path));
            RecognitionSettings settings = new();
            settings.WhisperCpp.Temperature = 0.3f;
            settings.WhisperCpp.Prompt = "retain me";
            settings.WhisperCpp.Threads = 4;
            settings.OcrPrimaryLanguage = "ru";
            settings.OcrSecondarySame = false;
            settings.OcrSecondaryLanguage = "en";
            var detached = settings.Clone();
            detached.WhisperCpp.Prompt = "draft only";
            Assert.Equal("retain me", settings.WhisperCpp.Prompt);
            store.Save(settings);
            var loaded = store.Load()!;
            Assert.Equal(0.3f, loaded.WhisperCpp.Temperature);
            Assert.Equal("retain me", loaded.WhisperCpp.Prompt);
            Assert.Equal(4, loaded.WhisperCpp.Threads);
            Assert.Equal("ru", loaded.OcrPrimaryLanguage);
            Assert.False(loaded.OcrSecondarySame);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.Null(loaded.WhisperCpp.Model); // OCR-only settings can be saved.
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("und")]
    [InlineData("not-a-language")]
    [InlineData("zz-ZZ")]
    public void Invalid_language_is_rejected_without_touching_file(string code)
    {
        RecognitionSettings settings = new();
        settings.Whisper.LanguageDetection = false;
        settings.Whisper.Language = code;
        Assert.Throws<ArgumentException>(() => settings.Validate());
        settings.Whisper.LanguageDetection = true;
        settings.OcrPrimaryLanguage = code;
        Assert.Throws<ArgumentException>(() => settings.Validate());
    }

    [Theory]
    [InlineData("{\"Whisper\":{\"LanguageDetection\":false,\"Language\":\"not-a-language\"}}")]
    [InlineData("{\"OcrPrimaryLanguage\":\"zz-ZZ\"}")]
    [InlineData("{\"WhisperCpp\":{\"RuntimeLibraries\":null}}") ]
    [InlineData("{\"WhisperCpp\":{\"RuntimeLibraries\":[999]}}")]
    [InlineData("{\"WhisperCpp\":{\"Model\":{\"Model\":999}}}")]
    public void Invalid_runtime_or_model_input_is_preserved(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            var store = new RecognitionSettingsStore(path);
            Assert.Null(store.Load());
            Assert.NotNull(store.LoadWarning);
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Failed_save_leaves_live_state_untouched_and_stale_work_is_dropped()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var backend = new FakeBackend();
            using var actions = new RecognitionActions(backend);
            Exception? failure = await Record.ExceptionAsync(() => actions.SaveAndApplyAsync(new(), new RecognitionSettingsStore(dir)));
            Assert.True(failure is IOException or UnauthorizedAccessException,
                $"Expected IOException or UnauthorizedAccessException, got {failure?.GetType().FullName ?? "no exception"}.");
            Assert.Equal(0, backend.Applies);
            using ManualResetEventSlim entered = new(), release = new();
            Task first = actions.MutateAsync(() => { entered.Set(); release.Wait(); });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Task stale = actions.StartAsync(0);
            actions.Invalidate();
            release.Set();
            await first;
            await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => stale);
            Assert.Empty(backend.Calls);
            await actions.StartAsync(1);
            await actions.TogglePauseAsync();
            backend.IsPaused = true;
            await actions.TogglePauseAsync();
            await actions.CancelAsync(1);
            await actions.OcrAsync(0);
            Assert.Equal(new[] { "start1", "pause", "resume", "cancel1", "ocr0" }, backend.Calls);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => actions.StartAsync(2));
            backend.HasAudio = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() => actions.StartAsync(0));
            actions.Dispose();
            await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => actions.StartAsync(0));
            Assert.Equal(5, backend.Calls.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Accepted_mutations_are_FIFO_and_async_disposal_drains_running_work()
    {
        var backend = new FakeBackend();
        var actions = new RecognitionActions(backend);
        using ManualResetEventSlim entered = new(), release = new();
        List<int> calls = [];
        Task first = actions.MutateAsync(() => { entered.Set(); release.Wait(); calls.Add(0); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Task[] queued = Enumerable.Range(1, 20).Select(index => actions.MutateAsync(() => calls.Add(index))).ToArray();
        release.Set();
        await first;
        await Task.WhenAll(queued);
        Assert.Equal(Enumerable.Range(0, 21), calls);

        entered.Reset(); release.Reset();
        Task active = actions.MutateAsync(() => { entered.Set(); release.Wait(); calls.Add(21); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Task rejected = actions.StartAsync(0);
        Task draining = actions.DisposeAsync().AsTask();
        Assert.False(draining.IsCompleted);
        release.Set();
        await active;
        await draining;
        await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => rejected);
        Assert.Empty(backend.Calls);
    }

    sealed class FakeBackend : IRecognitionBackend
    {
        public List<string> Calls = [];
        public int Applies;
        public bool HasAudio { get; set; } = true;
        public bool IsLive => false;
        public bool IsBusy => false;
        public bool IsPaused { get; set; }
        public void Start(int slot) => Calls.Add("start" + slot);
        public void Pause() => Calls.Add("pause");
        public void Resume() => Calls.Add("resume");
        public void Cancel(int slot) => Calls.Add("cancel" + slot);
        public void Ocr(int slot) => Calls.Add("ocr" + slot);
        public void Apply(RecognitionSettings settings) => Applies++;
    }
}
