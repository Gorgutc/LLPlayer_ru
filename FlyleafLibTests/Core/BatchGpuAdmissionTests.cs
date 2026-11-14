using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.Core;

public sealed class BatchGpuAdmissionTests
{
    [Fact]
    public async Task Dubbing_batch_waits_outside_model_lease_and_keeps_one_permit_until_shutdown()
    {
        using var timeout = Deadline();
        CancellationToken token = timeout.Token;
        using var input = new SubtitleInput();
        var coordinator = new GpuWorkCoordinator();
        var resources = new LanguageResourceCoordinator();
        var stopping = NewSignal();
        var stopped = NewSignal();
        var renderer = new Renderer
        {
            Stop = async () =>
            {
                stopping.TrySetResult();
                await stopped.Task.WaitAsync(token);
            }
        };
        int rendererCount = 0;
        var factory = new BatchRunFactory(
            () => AsrConfig("--device cpu"),
            createDubbingRenderer: _ => { rendererCount++; return renderer; },
            acquireRunLease: resources.AcquireUse,
            gpuCoordinator: coordinator);
        GpuWorkLease first = await coordinator.AcquireAsync("Interactive ASR", token);
        IBatchRun run = factory.Create(new BatchSettings { GenerateDubbing = true }, input.Jobs);
        Task processing = run.ProcessAsync(new ProgressSink(), token);

        Assert.True(run.IsWaitingForGpu);
        Assert.False(processing.IsCompleted);
        using (resources.AcquireMutation()) Assert.Empty(renderer.Files);
        first.Complete();
        await processing.WaitAsync(token);

        Assert.Equal(1, rendererCount);
        Assert.Equal(input.Jobs.Select(job => job.MediaPath), renderer.Files);
        Assert.All(input.Jobs, job => Assert.Equal(BatchSubtitleStatus.Completed, job.Status));
        Task<GpuWorkLease> waiting = coordinator.AcquireAsync("Next interactive ASR", token).AsTask();
        Task disposing = run.DisposeAsync().AsTask();
        try
        {
            await stopping.Task.WaitAsync(token);
            Assert.False(waiting.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => resources.AcquireMutation());
        }
        finally
        {
            stopped.TrySetResult();
            await disposing.WaitAsync(token);
        }

        (await waiting.WaitAsync(token)).Complete();
        using (resources.AcquireMutation()) Assert.Null(coordinator.PendingDrain);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Canceled_gpu_wait_leaves_jobs_unstarted_while_explicit_cpu_can_finish(bool gpu)
    {
        using var timeout = Deadline();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var input = new SubtitleInput();
        var coordinator = new GpuWorkCoordinator();
        var resources = new LanguageResourceCoordinator();
        var factory = new BatchRunFactory(() => AsrConfig(gpu ? "--device auto" : "--device=cpu"),
            acquireRunLease: resources.AcquireUse, gpuCoordinator: coordinator);
        GpuWorkLease first = await coordinator.AcquireAsync("Interactive ASR", timeout.Token);
        await using IBatchRun run = factory.Create(new BatchSettings(), input.Jobs);
        Task processing = run.ProcessAsync(new ProgressSink(), request.Token);
        try
        {
            if (gpu)
            {
                Assert.True(run.IsWaitingForGpu);
                using (resources.AcquireMutation()) await request.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
                Assert.All(input.Jobs, job => Assert.Equal(BatchSubtitleStatus.Pending, job.Status));
            }
            else
            {
                await processing.WaitAsync(timeout.Token);
                Assert.All(input.Jobs, job => Assert.Equal(BatchSubtitleStatus.Completed, job.Status));
            }
        }
        finally { first.Complete(); }
    }

    [Fact]
    public async Task Unconfirmed_stop_aborts_the_batch_and_retains_models_and_gpu_until_retry()
    {
        using var timeout = Deadline();
        CancellationToken token = timeout.Token;
        using var input = new SubtitleInput();
        var coordinator = new GpuWorkCoordinator();
        var resources = new LanguageResourceCoordinator();
        bool canStop = false;
        var renderer = new Renderer
        {
            Start = () => throw new EngineProcessDrainException("Child still running.", new IOException("Stop failed."),
                () => canStop ? ValueTask.CompletedTask : ValueTask.FromException(new IOException("Child still running.")))
        };
        var factory = new BatchRunFactory(() => AsrConfig("--device cpu"),
            createDubbingRenderer: _ => renderer,
            acquireRunLease: resources.AcquireUse,
            gpuCoordinator: coordinator);
        IBatchRun run = factory.Create(new BatchSettings { GenerateDubbing = true }, input.Jobs);
        await Assert.ThrowsAsync<EngineProcessDrainException>(
            () => run.ProcessAsync(new ProgressSink(), token).WaitAsync(token));
        Assert.Equal([input.Jobs[0].MediaPath], renderer.Files);
        Assert.Equal(BatchSubtitleStatus.Failed, input.Jobs[0].Status);
        Assert.Equal(BatchSubtitleStatus.Pending, input.Jobs[1].Status);
        await Assert.ThrowsAsync<IOException>(() => run.DisposeAsync().AsTask());

        Task<GpuWorkLease> waiting = coordinator.AcquireAsync("Next interactive ASR", token).AsTask();
        Assert.False(waiting.IsCompleted);
        Assert.NotNull(coordinator.PendingDrain);
        Assert.Throws<InvalidOperationException>(() => resources.AcquireMutation());
        canStop = true;
        Assert.True(await coordinator.RetryDrainAsync());
        (await waiting.WaitAsync(token)).Complete();
        using (resources.AcquireMutation()) Assert.Null(coordinator.PendingDrain);
    }

    static Config AsrConfig(string arguments)
    {
        FlyleafLib.Utils.IsTesting = true;
        Config config = new(true);
        config.Subtitles.Languages = [Language.English];
        config.Subtitles.ASREngine = SubASREngineType.FasterWhisper;
        config.Subtitles.FasterWhisperConfig.UseManualEngine = true;
        config.Subtitles.FasterWhisperConfig.ManualEnginePath = Environment.ProcessPath;
        config.Subtitles.FasterWhisperConfig.ExtraArguments = arguments;
        return config;
    }

    static CancellationTokenSource Deadline()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return timeout;
    }

    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    sealed class ProgressSink : IProgress<BatchSubtitleProgress>
    {
        public void Report(BatchSubtitleProgress value) { }
    }

    sealed class SubtitleInput : IDisposable
    {
        readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("llplayer-batch-gpu-");
        public BatchSubtitleJob[] Jobs { get; }

        public SubtitleInput()
        {
            Jobs = [new(Path.Combine(folder.FullName, "first.mkv")), new(Path.Combine(folder.FullName, "second.mkv"))];
            foreach (BatchSubtitleJob job in Jobs)
                File.WriteAllText(job.OutputPath, "1\n00:00:00,000 --> 00:00:01,000\nТест\n");
        }

        public void Dispose()
        {
            foreach (BatchSubtitleJob job in Jobs)
                File.Delete(job.OutputPath);
            folder.Delete();
        }
    }

    sealed class Renderer : IDubbingRenderer
    {
        public List<string> Files { get; } = [];
        public Func<Task> Start { get; init; } = () => Task.CompletedTask;
        public Func<ValueTask> Stop { get; init; } = () => ValueTask.CompletedTask;

        public Task RenderAsync(IReadOnlyList<SubtitleData> translatedSubtitles, string mediaPath, string outputPath,
            IProgress<DubbingProgress>? progress, CancellationToken token)
        {
            Files.Add(mediaPath);
            return Start();
        }

        public ValueTask DisposeAsync() => Stop();
    }
}
