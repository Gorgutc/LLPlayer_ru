using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Diarization;
using LLPlayer.Core.Services.Diarization;

namespace FlyleafLibTests.Core;

public sealed class DiarizationWorkflowTests
{
    static readonly DiarizationAudioSource Source = new("movie.mkv", 2);
    static readonly DiarizationCue[] Cues = [Cue(0, 3)];

    [Fact]
    public async Task Speakers_follow_media_order_and_only_unambiguous_cues_receive_labels()
    {
        var engine = new Engine((_, _) => Returns(
            Turn(8, 10, "A"), Turn(1, 4, "Z"), Turn(4, 5, "A"),
            Turn(2, 3, "Z"), Turn(9, 11, "Z"), Turn(5, 6, "Z")));
        var workflow = new DiarizationWorkflow(new());
        var cues = new[] { Cue(8, 9), Cue(1, 5), Cue(4, 6), Cue(6, 8), Cue(8, 10), Cue(9, 9), Cue(4, 5), Cue(-2, 2) };

        IReadOnlyList<string?> labels = await workflow.RunAsync(engine, Source, cues, useGpu: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(["Speaker 2", "Speaker 1", null, null, null, null, "Speaker 2", "Speaker 1"], labels);
    }

    [Fact]
    public async Task Duplicate_speaker_turns_do_not_count_the_same_speech_twice()
    {
        var engine = new Engine((_, _) => Returns(Turn(0, 1, "first"), Turn(0, 1, "first"), Turn(1, 2, "second")));
        var workflow = new DiarizationWorkflow(new());

        IReadOnlyList<string?> labels = await workflow.RunAsync(engine, Source, Cues, useGpu: false,
            TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(labels));
    }

    [Theory]
    [InlineData(-1, 1, "SPEAKER_00")]
    [InlineData(2, 1, "SPEAKER_00")]
    [InlineData(1, 1, "SPEAKER_00")]
    [InlineData(0, 1, " ")]
    public async Task Invalid_turn_rejects_the_complete_result(int start, int end, string speaker)
    {
        var engine = new Engine((_, _) => Returns(Turn(0, 1, "valid"), Turn(start, end, speaker)));
        var workflow = new DiarizationWorkflow(new());

        await Assert.ThrowsAsync<InvalidDataException>(() => workflow.RunAsync(engine, Source, Cues, useGpu: false,
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Interactive ASR")]
    [InlineData("Batch")]
    public async Task Gpu_run_waits_for_shared_admission_and_can_be_canceled_before_start(string owner)
    {
        using var deadline = Deadline();
        var coordinator = new GpuWorkCoordinator();
        var engine = new Engine((_, _) => throw new InvalidOperationException("Canceled work ran inference."));
        var workflow = new DiarizationWorkflow(coordinator);
        GpuWorkLease busy = await coordinator.AcquireAsync(owner, deadline.Token);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);

        try
        {
            Task<IReadOnlyList<string?>> queued = workflow.RunAsync(engine, Source, Cues, useGpu: true, request.Token);
            Assert.False(queued.IsCompleted);
            await request.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(deadline.Token));
        }
        finally { busy.Complete(); }
    }

    [Fact]
    public async Task Gpu_wait_uses_the_detached_cue_snapshot_taken_before_admission()
    {
        using var deadline = Deadline();
        var coordinator = new GpuWorkCoordinator();
        var engine = new Engine((_, _) => Returns(Turn(0, 3, "speaker")));
        var workflow = new DiarizationWorkflow(coordinator);
        GpuWorkLease busy = await coordinator.AcquireAsync("Batch", deadline.Token);
        var cues = new[] { Cue(1, 2) };

        Task<IReadOnlyList<string?>> queued = workflow.RunAsync(engine, Source, cues, useGpu: true, deadline.Token);
        cues[0] = Cue(5, 6);
        busy.Complete();

        Assert.Equal("Speaker 1", Assert.Single(await queued.WaitAsync(deadline.Token)));
    }

    [Fact]
    public async Task Cpu_run_bypasses_gpu_queue_and_preserves_selected_audio_source()
    {
        using var deadline = Deadline();
        var coordinator = new GpuWorkCoordinator();
        var engine = new Engine((source, _) =>
        {
            Assert.Equal(Source, source);
            return Returns(Turn(0, 3, "speaker"));
        });
        var workflow = new DiarizationWorkflow(coordinator);
        GpuWorkLease busy = await coordinator.AcquireAsync("Interactive ASR", deadline.Token);

        try
        {
            var labels = await workflow.RunAsync(engine, Source, Cues, useGpu: false, deadline.Token).WaitAsync(deadline.Token);
            Assert.Equal("Speaker 1", Assert.Single(labels));
            using var request = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            Task<GpuWorkLease> waiting = coordinator.AcquireAsync("Other GPU work", request.Token).AsTask();
            Assert.False(waiting.IsCompleted);
            await request.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(deadline.Token));
        }
        finally { busy.Complete(); }
    }

    [Fact]
    public async Task Gpu_lease_stays_held_after_cancel_until_engine_finishes_stopping()
    {
        using var deadline = Deadline();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var coordinator = new GpuWorkCoordinator();
        var stopping = NewSignal();
        var stopped = NewSignal();
        var started = NewSignal();
        var engine = new Engine(async (_, token) =>
        {
            using CancellationTokenRegistration registration = token.Register(() => stopping.TrySetResult());
            started.SetResult();
            await stopped.Task.WaitAsync(deadline.Token);
            return [Turn(0, 3, "speaker")];
        });
        var workflow = new DiarizationWorkflow(coordinator);
        Task<IReadOnlyList<string?>> running = workflow.RunAsync(engine, Source, Cues, useGpu: true, request.Token);
        await started.Task.WaitAsync(deadline.Token);

        await request.CancelAsync();
        await stopping.Task.WaitAsync(deadline.Token);
        Task<GpuWorkLease> next = coordinator.AcquireAsync("Batch", deadline.Token).AsTask();
        Assert.False(next.IsCompleted);

        stopped.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(deadline.Token));
        (await next.WaitAsync(deadline.Token)).Complete();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unconfirmed_stop_blocks_unsafe_work_until_retry_succeeds(bool useGpu)
    {
        using var deadline = Deadline();
        var coordinator = new GpuWorkCoordinator();
        bool canStop = false;
        var engine = new Engine((_, _) => throw new EngineProcessDrainException("Cannot stop", new IOException(),
            () => canStop ? ValueTask.CompletedTask : ValueTask.FromException(new IOException("Still running"))));
        var workflow = new DiarizationWorkflow(coordinator);
        await Assert.ThrowsAsync<EngineProcessDrainException>(() => workflow.RunAsync(engine, Source, Cues, useGpu, deadline.Token));
        Assert.True(workflow.HasPendingDrain);
        Task<GpuWorkLease> next = coordinator.AcquireAsync("Interactive ASR", deadline.Token).AsTask();
        Assert.Equal(useGpu, !next.IsCompleted);
        var followingEngine = new Engine((_, _) => Returns(Turn(0, 3, "speaker")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.RunAsync(followingEngine, Source, Cues, useGpu, deadline.Token));
        await Assert.ThrowsAsync<IOException>(() => workflow.RetryDrainAsync());
        Assert.True(workflow.HasPendingDrain);

        canStop = true;
        Assert.True(await workflow.RetryDrainAsync());
        Assert.False(workflow.HasPendingDrain);
        (await next.WaitAsync(deadline.Token)).Complete();
        Assert.Equal("Speaker 1", Assert.Single(await workflow.RunAsync(followingEngine, Source, Cues, useGpu, deadline.Token)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cpu_cleanup_does_not_retry_or_join_an_unrelated_pending_gpu_stop(bool retrying)
    {
        using var deadline = Deadline();
        var coordinator = new GpuWorkCoordinator();
        GpuWorkLease gpu = await coordinator.AcquireAsync("Interactive ASR", deadline.Token);
        var stopped = NewSignal();
        int gpuStopAttempts = 0;
        gpu.RequireDrain(async () =>
        {
            Interlocked.Increment(ref gpuStopAttempts);
            await stopped.Task.WaitAsync(deadline.Token);
        });
        gpu.Complete();
        Task<bool>? globalRetry = retrying ? coordinator.RetryDrainAsync() : null;

        var engine = new Engine((_, _) => throw new EngineProcessDrainException("Cannot stop CPU process",
            new IOException(), () => ValueTask.CompletedTask));
        var workflow = new DiarizationWorkflow(coordinator);
        await Assert.ThrowsAsync<EngineProcessDrainException>(() => workflow.RunAsync(engine, Source, Cues, useGpu: false, deadline.Token));

        Task<bool> ownRetry = workflow.RetryDrainAsync();
        try
        {
            Assert.True(ownRetry.IsCompletedSuccessfully);
            Assert.True(await ownRetry);
            Assert.False(workflow.HasPendingDrain);
            Assert.Equal("Interactive ASR", coordinator.PendingDrain);
            Assert.Equal(retrying ? 1 : 0, gpuStopAttempts);
        }
        finally
        {
            stopped.TrySetResult();
            await (globalRetry ?? coordinator.RetryDrainAsync()).WaitAsync(deadline.Token);
        }
    }

    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    static CancellationTokenSource Deadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        return deadline;
    }

    static DiarizationCue Cue(int start, int end) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));

    static DiarizationTurn Turn(int start, int end, string speaker) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), speaker);

    static Task<IReadOnlyList<DiarizationTurn>> Returns(params DiarizationTurn[] turns) => Task.FromResult<IReadOnlyList<DiarizationTurn>>(turns);

    sealed class Engine(Func<DiarizationAudioSource, CancellationToken, Task<IReadOnlyList<DiarizationTurn>>> run)
        : IDiarizationEngine
    {
        public Task<IReadOnlyList<DiarizationTurn>> RunAsync(DiarizationAudioSource source, CancellationToken token) => run(source, token);
    }
}
