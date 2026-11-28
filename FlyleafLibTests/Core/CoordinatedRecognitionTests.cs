using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.Core;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RecognitionCommandQueueCollection
{
    public const string Name = "Recognition command queue";
}

[Collection(RecognitionCommandQueueCollection.Name)]
public class CoordinatedRecognitionTests
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    static Task Within(Task task) => task.WaitAsync(Limit, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Canceled_gpu_wait_releases_neither_the_owner_nor_model_files()
    {
        var coordinator = new GpuWorkCoordinator();
        var backend = new Backend();
        var resources = new LanguageResourceCoordinator();
        GpuWorkLease batch = await coordinator.AcquireAsync("batch", TestContext.Current.CancellationToken);
        await using var actions = new RecognitionActions(backend, resources.AcquireUse, coordinator);
        TaskCompletionSource waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        actions.GpuWaitChanged += () => { if (actions.IsWaitingForGpu) waiting.TrySetResult(); };

        try
        {
            Task pending = actions.StartAsync(0);
            Task entered = await Task.WhenAny(waiting.Task, pending).WaitAsync(Limit, TestContext.Current.CancellationToken);
            await entered;
            Assert.Same(waiting.Task, entered);
            using (resources.AcquireMutation()) await Within(actions.MutateAsync(() => { }));
            await Within(actions.CancelAsync(0));
            await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => Within(pending));
            Assert.Equal(0, backend.Starts);

            backend.UseCpu = true;
            await Within(actions.StartAsync(1));
            Assert.Equal(1, backend.Starts);
            Assert.False(backend.ConfirmProcessExit);
        }
        finally
        {
            backend.SignalStop();
            batch.Complete();
        }
    }

    [Fact]
    public async Task Second_slot_attaches_and_canceling_only_one_slot_does_not_release_gpu()
    {
        var coordinator = new GpuWorkCoordinator();
        var backend = new Backend();
        await using var actions = new RecognitionActions(backend, null, coordinator);
        try
        {
            await Within(actions.StartAsync(0));
            await Within(actions.StartAsync(1));
            Assert.Equal(1, backend.Starts);
            Assert.True(backend.ConfirmProcessExit);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            Task<GpuWorkLease> batch = coordinator.AcquireAsync("batch", wait.Token).AsTask();
            try
            {
                await Within(actions.CancelAsync(0));
                Assert.False(batch.IsCompleted);
                await Within(actions.CancelAsync(1));
                await Within(batch);
            }
            finally
            {
                wait.Cancel();
                backend.SignalStop();
                if (batch.IsCompletedSuccessfully) (await batch).Complete();
            }
        }
        finally { backend.SignalStop(); }
    }

    [Fact]
    public async Task Explicit_off_does_not_attach_to_a_shared_default_on_run()
    {
        var backend = new Backend { UseCpu = true };
        await using var actions = new RecognitionActions(backend, null, new GpuWorkCoordinator());
        try
        {
            await Within(actions.StartAsync(0));
            Assert.True(backend.DetectAsrOmissions);
            await Within(actions.StartAsync(1, detectAsrOmissions: false));
            Assert.False(backend.DetectAsrOmissions);
            Assert.Equal(2, backend.Starts);
        }
        finally { backend.SignalStop(); }
    }

    sealed class Backend : IRecognitionBackend, ICoordinatedRecognitionBackend
    {
        readonly HashSet<int> slots = [];
        TaskCompletionSource? worker;
        public bool UseCpu { get; set; }
        public bool ConfirmProcessExit { get; private set; }
        public bool DetectAsrOmissions { get; private set; }
        public int Starts { get; private set; }
        public bool HasAudio => true;
        public bool IsLive => false;
        public bool IsBusy => false;
        public bool IsPaused => false;

        public object CaptureInput() => this;
        public bool IsCurrentInput(object input) => ReferenceEquals(input, this);
        public Config CaptureSettings()
        {
            Utils.IsTesting = true;
            var config = new Config(true);
            config.Subtitles.ASREngine = SubASREngineType.FasterWhisper;
            config.Subtitles.FasterWhisperConfig.ExtraArguments = UseCpu ? "--device cpu" : "--device cuda";
            return config;
        }
        public bool CanAttach(int slot, bool detectAsrOmissions)
            => slots.Count != 0 && !slots.Contains(slot) && DetectAsrOmissions == detectAsrOmissions;
        public RecognitionAttempt Start(int slot, object input, Config settings, bool attachOnly, bool confirmProcessExit,
            CancellationToken cancellationToken, bool detectAsrOmissions)
        {
            if (attachOnly)
            {
                Assert.True(CanAttach(slot, detectAsrOmissions));
                slots.Add(slot);
                return new(Task.CompletedTask, Task.CompletedTask);
            }

            ConfirmProcessExit = confirmProcessExit;
            DetectAsrOmissions = detectAsrOmissions;
            Starts++;
            slots.Add(slot);
            worker = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new(Task.CompletedTask, worker.Task);
        }

        public void Cancel(int slot)
        {
            slots.Remove(slot);
            if (slots.Count == 0) SignalStop();
        }
        public void SignalStop() => worker?.TrySetResult();
        public void Start(int slot) => throw new InvalidOperationException("Run ownership was bypassed.");
        public void Pause() { }
        public void Resume() { }
        public void Ocr(int slot) { }
        public void Apply(RecognitionSettings settings) { }
    }
}
