using System.Collections.Concurrent;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.MediaPlayer.Batch;

public sealed class GpuCoordinatorProofTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_file_batch_excludes_other_gpu_work_through_normal_or_canceled_shutdown(bool cancelSecondFile)
    {
        using CancellationTokenSource test = CreateDeadline();
        using CancellationTokenSource batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(test.Token);
        CancellationToken token = test.Token;
        DirectoryInfo folder = Directory.CreateTempSubdirectory("llplayer-gpu-proof-");
        var events = new ConcurrentQueue<string>();
        TaskCompletionSource secondAsr = Checkpoint();
        TaskCompletionSource finishSecondAsr = Checkpoint();
        TaskCompletionSource stopping = Checkpoint();
        TaskCompletionSource stopped = Checkpoint();
        TaskCompletionSource recognitionStopping = Checkpoint();
        TaskCompletionSource recognitionStopped = Checkpoint();
        using var admission = new ProofAdmission();
        var recognizer = new RecognitionBackend(events);
        await using var recognition = new RecognitionActions(recognizer);
        var tts = new TtsBackend(events)
        {
            Stop = async () =>
            {
                stopping.SetResult();
                await stopped.Task.WaitAsync(token);
                events.Enqueue("tts-exit");
            }
        };
        var renderer = new DubbingRenderer(new DubbingConfig(), () => tts);
        BatchSubtitleJob[] jobs =
        [
            new(Path.Combine(folder.FullName, "first.mkv")),
            new(Path.Combine(folder.FullName, "second.mkv"))
        ];
        var batchStages = new BatchStages(events, async (name, cancellationToken) =>
        {
            if (name == "second")
            {
                secondAsr.SetResult();
                await finishSecondAsr.Task.WaitAsync(cancellationToken);
            }
        });
        var batch = new BatchSubtitleProcessor(batchStages, batchStages, batchStages,
            new BatchSubtitleOptions { GenerateDubbing = true }, dubber: renderer);

        async Task RunBatchAsync()
        {
            ProofLease lease = (await admission.AdmitAsync(useGpu: true, token))!;
            try { await batch.ProcessAsync(jobs, batchCancellation.Token); }
            finally { await lease.ReleaseAfterStopAsync(renderer.DisposeAsync); }
        }

        Task run = RunBatchAsync();
        Task waiting = Task.CompletedTask;
        try
        {
            await secondAsr.Task.WaitAsync(token);
            waiting = StartRecognitionAsync(admission, recognition, slot: 0, useGpu: true, token, async () =>
            {
                recognitionStopping.SetResult();
                await recognitionStopped.Task.WaitAsync(token);
            });
            Assert.False(waiting.IsCompleted);
            await StartRecognitionAsync(admission, recognition, slot: 1, useGpu: false, token).WaitAsync(token);
            Assert.Equal([1], recognizer.Started);

            if (cancelSecondFile)
                await batchCancellation.CancelAsync();
            else
                finishSecondAsr.SetResult();
            await stopping.Task.WaitAsync(token);
            Assert.Equal(BatchSubtitleStatus.Completed, jobs[0].Status);
            Assert.Equal(cancelSecondFile ? BatchSubtitleStatus.Canceled : BatchSubtitleStatus.Completed, jobs[1].Status);
            Assert.True(File.Exists(DubPath(jobs[0])));
            Assert.Equal(!cancelSecondFile, File.Exists(DubPath(jobs[1])));
            Assert.False(waiting.IsCompleted);
            Assert.Equal([1], recognizer.Started);

            stopped.SetResult();
            await run.WaitAsync(token);
            await recognitionStopping.Task.WaitAsync(token);
            Task<ProofLease?> nextAdmission = admission.AdmitAsync(useGpu: true, token).AsTask();
            Assert.False(nextAdmission.IsCompleted);
            recognitionStopped.SetResult();
            await waiting.WaitAsync(token);
            ProofLease next = (await nextAdmission.WaitAsync(token))!;
            await next.ReleaseAfterStopAsync(() => ValueTask.CompletedTask);
            string[] expected = [
                "asr:first", "translate:first", "write:first", "tts-start", "dub:first",
                "asr:second", "recognition:1"
            ];
            if (!cancelSecondFile)
                expected = [.. expected, "translate:second", "write:second", "dub:second"];
            Assert.Equal([.. expected, "tts-exit", "recognition:0"], events);
        }
        finally
        {
            finishSecondAsr.TrySetResult();
            stopped.TrySetResult();
            recognitionStopped.TrySetResult();
            try { await Task.WhenAll(run, waiting).WaitAsync(token); }
            finally
            {
                foreach (BatchSubtitleJob job in jobs)
                    File.Delete(DubPath(job));
                folder.Delete();
            }
        }
    }

    [Fact]
    public async Task Waiting_recognition_can_be_canceled_after_media_change_and_shutdown_can_drain()
    {
        using CancellationTokenSource test = CreateDeadline();
        using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(test.Token);
        using var admission = new ProofAdmission();
        ProofLease owner = (await admission.AdmitAsync(useGpu: true, test.Token))!;
        var recognizer = new RecognitionBackend(new());
        await using var recognition = new RecognitionActions(recognizer);

        Task waiting = StartRecognitionAsync(admission, recognition, slot: 0, useGpu: true, request.Token);
        try
        {
            Assert.False(waiting.IsCompleted);
            bool mediaChanged = false;
            await recognition.MutateAsync(() => mediaChanged = true, mediaChange: true).WaitAsync(test.Token);
            Assert.True(mediaChanged);
            await request.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(test.Token));
            await recognition.DisposeAsync().AsTask().WaitAsync(test.Token);
            Assert.Empty(recognizer.Started);
        }
        finally
        {
            await request.CancelAsync();
            await owner.ReleaseAfterStopAsync(() => ValueTask.CompletedTask);
        }
    }

    [Fact]
    public async Task Failed_stop_after_partial_start_retains_admission_until_verified_cleanup()
    {
        using CancellationTokenSource test = CreateDeadline();
        using var admission = new ProofAdmission();
        ProofLease owner = (await admission.AdmitAsync(useGpu: true, test.Token))!;
        TaskCompletionSource exited = Checkpoint();
        int stopAttempts = 0;
        var tts = new TtsBackend(new())
        {
            Start = _ => throw new TimeoutException("Model startup timed out after child start."),
            Stop = () =>
            {
                stopAttempts++;
                if (!exited.Task.IsCompletedSuccessfully)
                    throw new InvalidOperationException("Child exit could not be confirmed.");
                return ValueTask.CompletedTask;
            }
        };
        await using var renderer = new DubbingRenderer(new DubbingConfig(), () => tts);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.RenderAsync(
            [Cue("line")], "movie.mkv", "unused.ru.dub.flac", progress: null, test.Token));
        Assert.Contains("Child exit could not be confirmed", failure.Message);
        Assert.Equal(1, stopAttempts);

        // The failed start never published a host to the renderer; the lifecycle owner must retain it.
        // A successful renderer.DisposeAsync alone cannot confirm exit in this case.
        await renderer.DisposeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ReleaseAfterStopAsync(tts.DisposeAsync).AsTask());
        Task<ProofLease?> waiting = admission.AdmitAsync(useGpu: true, test.Token).AsTask();
        Assert.False(waiting.IsCompleted);
        Assert.Equal(2, stopAttempts);

        exited.SetResult();
        await owner.ReleaseAfterStopAsync(tts.DisposeAsync);
        ProofLease next = (await waiting.WaitAsync(test.Token))!;
        await owner.ReleaseAfterStopAsync(tts.DisposeAsync);
        Assert.Equal(3, stopAttempts);
        Task<ProofLease?> later = admission.AdmitAsync(useGpu: true, test.Token).AsTask();
        Assert.False(later.IsCompleted);
        await next.ReleaseAfterStopAsync(() => ValueTask.CompletedTask);
        ProofLease last = (await later.WaitAsync(test.Token))!;
        await last.ReleaseAfterStopAsync(() => ValueTask.CompletedTask);
    }

    // Wait at the caller boundary before submitting a short command to the existing recognition queue.
    private static async Task StartRecognitionAsync(ProofAdmission admission, RecognitionActions recognition,
        int slot, bool useGpu, CancellationToken token, Func<ValueTask>? confirmStop = null)
    {
        ProofLease? lease = await admission.AdmitAsync(useGpu, token);
        try
        {
            token.ThrowIfCancellationRequested();
            await recognition.StartAsync(slot);
        }
        finally
        {
            if (lease is not null)
                await lease.ReleaseAfterStopAsync(confirmStop ?? (() => ValueTask.CompletedTask));
        }
    }

    private static CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        return deadline;
    }

    private static TaskCompletionSource Checkpoint() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string DubPath(BatchSubtitleJob job) =>
        DubbingOutputPathBuilder.BuildRussianDubPath(job.MediaPath, DubbingOutputPathBuilder.DefaultExtension);

    private static SubtitleData Cue(string text) => new()
    {
        Text = text, TranslatedText = text, StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(1)
    };

    private sealed class BatchStages(ConcurrentQueue<string> events, Func<string, CancellationToken, Task> transcribe)
        : IBatchAsrTranscriber, IBatchSubtitleTranslator, IBatchSubtitleWriter
    {
        public async Task<BatchAsrResult> TranscribeAsync(string path, CancellationToken token, IProgress<BatchAsrProgress>? progress)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            events.Enqueue($"asr:{name}");
            await transcribe(name, token);
            return new([Cue(name)], Language.English);
        }

        public Task TranslateAsync(IList<SubtitleData> subtitles, Language language, CancellationToken token)
        {
            string? text = subtitles[0].Text;
            events.Enqueue($"translate:{text}");
            subtitles[0].TranslatedText = $"translated {text}";
            return Task.CompletedTask;
        }

        public Task WriteAsync(IReadOnlyList<SubtitleData> subtitles, string outputPath, bool overwrite, CancellationToken token)
        {
            events.Enqueue($"write:{subtitles[0].Text}");
            return Task.CompletedTask;
        }
    }

    private sealed class TtsBackend(ConcurrentQueue<string> events) : IDubbingTtsService
    {
        public Func<CancellationToken, Task<bool>> Start { get; init; } = _ => Task.FromResult(true);
        public Func<ValueTask> Stop { get; init; } = () => ValueTask.CompletedTask;

        public Task<bool> TryInitializeAsync(CancellationToken token)
        {
            events.Enqueue("tts-start");
            return Start(token);
        }

        public Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken token)
            => Task.FromResult(new TtsResult("clip.wav", 1000));

        public Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken token)
            => Task.FromResult(VoiceBankResolver.BuiltIn);

        public Task AssembleAsync(AssembleRequest request, CancellationToken token)
        {
            File.WriteAllText(request.OutputPath, "fake dub");
            events.Enqueue($"dub:{Path.GetFileNameWithoutExtension(request.MediaPath)}");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => Stop();
    }

    private sealed class RecognitionBackend(ConcurrentQueue<string> events) : IRecognitionBackend
    {
        public ConcurrentQueue<int> Started { get; } = new();
        public bool HasAudio => true;
        public bool IsLive => false;
        public bool IsBusy => false;
        public bool IsPaused => false;
        public void Start(int slot)
        {
            Started.Enqueue(slot);
            events.Enqueue($"recognition:{slot}");
        }

        public void Pause() => throw new NotSupportedException();
        public void Resume() => throw new NotSupportedException();
        public void Cancel(int slot) => throw new NotSupportedException();
        public void Ocr(int slot) => throw new NotSupportedException();
        public void Apply(RecognitionSettings settings) => throw new NotSupportedException();
    }

    // Executable reference model of the ADR only. No production caller takes this permit.
    private sealed class ProofAdmission : IDisposable
    {
        private readonly SemaphoreSlim _slot = new(1, 1);

        public async ValueTask<ProofLease?> AdmitAsync(bool useGpu, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!useGpu)
                return null;

            await _slot.WaitAsync(token);
            return new ProofLease(() => _slot.Release());
        }

        public void Dispose() => _slot.Dispose();
    }

    private sealed class ProofLease(Action release)
    {
        // Lifecycle owner serializes its cleanup attempts. A failed stop can be retried by that same owner.
        private bool _released;

        public async ValueTask ReleaseAfterStopAsync(Func<ValueTask> confirmStop)
        {
            if (_released)
                return;
            await confirmStop();
            _released = true;
            release();
        }
    }
}
