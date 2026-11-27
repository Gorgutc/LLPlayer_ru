using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using FlyleafLibTests.Core;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;
using LLPlayer.Core.Services.Models;

namespace FlyleafLibTests.Automation;

[Collection(RecognitionCommandQueueCollection.Name)]
public sealed class ControlWorkflowTests : IDisposable
{
    // Config(true) still creates the WPF video config, which otherwise requires a dispatcher.
    private readonly bool previousTestMode = (bool)typeof(Utils).GetProperty(nameof(Utils.IsTesting))!.GetValue(null)!;

    public ControlWorkflowTests() => Utils.IsTesting = true;

    public void Dispose() => Utils.IsTesting = previousTestMode;

    [Fact]
    public async Task Asr_job_completes_after_actual_engine_completion()
    {
        using var media = new TempMedia();
        var gpu = new GpuWorkCoordinator();
        var resources = new LanguageResourceCoordinator();
        TaskCompletionSource<BatchAsrResult> engine = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflows = new ControlWorkflows(() => throw new InvalidOperationException("A pinned snapshot must be reused."),
            () => { }, gpu, resources,
            (_, path, index, start, _, _, _, _, language) =>
            {
                Assert.Equal(media.Path, path);
                Assert.Equal(3, index);
                Assert.Equal(TimeSpan.FromSeconds(1), start);
                Assert.Equal(Language.Get("de"), language);
                return engine.Task;
            });
        CancellationToken token = TestContext.Current.CancellationToken;

        Task<BatchAsrResult> running = workflows.TranscribeAsync(media.Path, 3, null,
            TimeSpan.FromSeconds(1), 0, default, null, token, savedConfig: GpuConfig(), sourceLanguage: Language.Get("de"));
        ValueTask<GpuWorkLease> next = gpu.AcquireAsync("Batch subtitles", token);

        Assert.False(running.IsCompleted);
        Assert.False(next.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => resources.AcquireMutation());
        engine.SetResult(new BatchAsrResult([new() { Text = "ready" }], Language.English));
        BatchAsrResult result = await running.WaitAsync(token);
        (await next).Complete();
        using IDisposable mutation = resources.AcquireMutation();
        Assert.Equal("ready", Assert.Single(result.Subtitles).Text);
    }

    [Fact]
    public async Task Cancellation_keeps_lease_until_process_exit()
    {
        using var media = new TempMedia();
        var gpu = new GpuWorkCoordinator();
        var resources = new LanguageResourceCoordinator();
        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflows = new ControlWorkflows(GpuConfig, () => { }, gpu, resources,
            (_, _, _, _, _, _, _, _, _) => Task.FromException<BatchAsrResult>(new EngineProcessDrainException(
                "Unconfirmed stop", new IOException(), () => new ValueTask(stopped.Task))));
        CancellationToken token = TestContext.Current.CancellationToken;

        var error = await Assert.ThrowsAsync<EngineProcessDrainException>(() =>
            workflows.TranscribeAsync(media.Path, 2, null, TimeSpan.Zero, null, default, null, token));
        ValueTask<GpuWorkLease> next = gpu.AcquireAsync("Batch subtitles", token);
        Task retry = error.RetryStop().AsTask();

        Assert.False(retry.IsCompleted);
        Assert.False(next.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => resources.AcquireMutation());
        stopped.SetResult();
        await retry.WaitAsync(token);
        (await next).Complete();
        using IDisposable mutation = resources.AcquireMutation();
    }

    [Fact]
    public async Task Model_delete_waits_for_active_reader()
    {
        using var media = new TempMedia();
        string directory = Path.GetDirectoryName(media.Path)!;
        var resources = new LanguageResourceCoordinator();
        using var models = new ControlModelResources(() => new Config(true), new(directory, directory, directory), resources);
        ModelEntry entry = Assert.Single(models.List(ModelKind.Tesseract), candidate => candidate.Id == "tesseract:eng");
        await File.WriteAllTextAsync(entry.FullPath, "test data", TestContext.Current.CancellationToken);
        using IDisposable reader = resources.AcquireUse();

        Task removal = models.RemoveAsync(entry.Id, TestContext.Current.CancellationToken);

        Assert.False(removal.IsCompleted);
        Assert.True(File.Exists(entry.FullPath));
        reader.Dispose();
        await removal;
        Assert.False(File.Exists(entry.FullPath));
    }

    [Fact]
    public async Task Watch_uses_host_job_admission_without_starting_a_second_engine()
    {
        using var media = new TempMedia();
        string directory = Path.GetDirectoryName(media.Path)!;
        string video = Path.Combine(directory, "watch.mp4");
        var watcher = new TestBatchWatcher();
        var factory = new TestBatchFactory();
        await using var batch = new BatchWorkflow(new() { LastFolder = directory, WatchFolder = true },
            factory, watcher, action => action(), new TestBatchCatalog());
        var admitted = new List<string>();
        batch.AdmitWatchedFiles = paths => admitted.AddRange(paths);

        batch.Open();
        watcher.Signal(video);
        Assert.Equal([video], admitted);
        Assert.Equal(0, factory.Starts);
        Assert.False(batch.IsRunning);

        await batch.StartWatchedAsync(admitted);
        Assert.Equal(1, factory.Starts);
        Assert.Equal(BatchSubtitleStatus.Completed, Assert.Single(batch.Jobs).Status);
    }

    [Fact]
    public async Task Full_slot_translation_respects_requested_target()
    {
        var provider = new TestTranslator(text => "French " + text);
        var translator = new BatchSubtitleTranslator(new Config(true).Subtitles, () => provider);
        SubtitleData[] cues = [new() { Text = "one", StartTime = TimeSpan.FromSeconds(3), EndTime = TimeSpan.FromSeconds(4), SpeakerId = "speaker" }];

        await TranslateDocumentAsync(translator, cues, TargetLanguage.French);

        Assert.Equal(TargetLanguage.French, provider.Target);
        Assert.Equal("French one", cues[0].TranslatedText);
        Assert.Equal("one", cues[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(3), cues[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(4), cues[0].EndTime);
        Assert.Equal("speaker", cues[0].SpeakerId);
    }

    [Fact]
    public async Task Full_slot_translation_rejects_incomplete_provider_result_without_commit()
    {
        var provider = new TestTranslator(text => text == "missing" ? " " : "French " + text);
        var translator = new BatchSubtitleTranslator(new Config(true).Subtitles, () => provider);
        SubtitleData[] cues = [new() { Text = "one", TranslatedText = "old one" }, new() { Text = "missing" }];

        await Assert.ThrowsAsync<TranslationException>(() => TranslateDocumentAsync(translator, cues, TargetLanguage.French));

        Assert.Equal("old one", cues[0].TranslatedText);
        Assert.Null(cues[1].TranslatedText);
    }

    [Fact]
    public async Task Batch_target_retains_existing_russian_policy()
    {
        var provider = new TestTranslator(text => "Russian " + text);
        Config.SubtitlesConfig config = new Config(true).Subtitles;
        config.TranslateTargetLanguage = TargetLanguage.French;
        var translator = new BatchSubtitleTranslator(config, () => provider);
        SubtitleData[] cues = [new() { Text = "one" }];

        await translator.TranslateAsync(cues, Language.English, TestContext.Current.CancellationToken);

        Assert.Equal(TargetLanguage.Russian, provider.Target);
        Assert.Equal("Russian one", cues[0].TranslatedText);
    }

    private static Task TranslateDocumentAsync(BatchSubtitleTranslator translator, SubtitleData[] cues, TargetLanguage target)
    {
        return translator.TranslateDocumentAsync(cues, Language.English, target, null, TestContext.Current.CancellationToken);
    }

    private static Config GpuConfig()
    {
        Config config = new(true);
        config.Audio.Languages = [Language.English];
        config.Subtitles.Languages = [Language.English];
        config.Subtitles.ASREngine = SubASREngineType.FasterWhisper;
        config.Subtitles.FasterWhisperConfig.ExtraArguments = "--device auto";
        return config;
    }

    private sealed class TempMedia : IDisposable
    {
        readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("llplayer-control-workflow-");

        public TempMedia()
        {
            Path = System.IO.Path.Combine(directory.FullName, "test-media.wav");
            File.WriteAllText(Path, "test audio");
        }

        public string Path { get; }

        public void Dispose() => directory.Delete(recursive: true);
    }

    private sealed class TestBatchWatcher : IBatchFolderWatcher
    {
        public event Action<BatchWatchReady>? FileReady;
        public event Action<BatchWatchError>? Error { add { } remove { } }
        public long Start(string path, bool recursive) => 1;
        public void Signal(string path) => FileReady?.Invoke(new BatchWatchReady(1, [path]));
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class TestBatchCatalog : IBatchMediaCatalog
    {
        public bool FolderExists(string path) => Directory.Exists(path);
        public bool TranslationExists(string path) => false;
        public bool DubExists(string path) => false;
        public Task<IReadOnlyList<string>> ScanAsync(string folder, bool recursive, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<MediaAudioTrack>> ProbeAsync(string path, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<MediaAudioTrack>>([]);
    }

    private sealed class TestBatchFactory : IBatchRunFactory
    {
        public int Starts { get; private set; }

        public IBatchRun Create(BatchSettings settings, IReadOnlyList<BatchSubtitleJob> jobs)
        {
            Starts++;
            return new TestBatchRun(jobs);
        }

        private sealed class TestBatchRun(IReadOnlyList<BatchSubtitleJob> jobs) : IBatchRun
        {
            public bool IsRunningOnCpu => true;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public Task ProcessAsync(IProgress<BatchSubtitleProgress> progress, CancellationToken token)
            {
                foreach (var job in jobs)
                    progress.Report(new BatchSubtitleProgress(job, BatchSubtitleStatus.Completed));
                return Task.CompletedTask;
            }
        }
    }

    private sealed class TestTranslator(Func<string, string> translate) : ITranslateService
    {
        public TranslateServiceType ServiceType => TranslateServiceType.Ollama;
        public TargetLanguage Target { get; private set; }
        public void Initialize(Language source, TargetLanguage target) => Target = target;

        public Task<string> TranslateAsync(string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(translate(text));
        }

        public void Dispose() { }
    }
}
