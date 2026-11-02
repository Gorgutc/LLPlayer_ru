using LLPlayer.Core.Services;

namespace FlyleafLibTests.Core;

public class RecognitionResourceAdmissionTests
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    // Reflection keeps this regression executable against the old constructor for intentional RED evidence.
    static RecognitionActions Create(Backend backend, LanguageResourceCoordinator resources)
    {
        var constructor = typeof(RecognitionActions).GetConstructor([typeof(IRecognitionBackend), typeof(Func<IDisposable>)]);
        return constructor == null ? new RecognitionActions(backend)
            : (RecognitionActions)constructor.Invoke([backend, (Func<IDisposable>)resources.AcquireUse]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Held_resource_mutation_rejects_direct_Core_start_or_ocr_before_any_backend_call(bool ocr)
    {
        LanguageResourceCoordinator resources = new(); Backend backend = new();
        await using RecognitionActions actions = Create(backend, resources);
        using (resources.AcquireMutation())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ocr ? actions.OcrAsync(0) : actions.StartAsync(0));
            Assert.Equal(0, backend.Calls);
        }
        await (ocr ? actions.OcrAsync(0) : actions.StartAsync(0));
        Assert.Equal(1, backend.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_Core_operation_owns_resource_lease_for_entire_blocking_backend_call(bool ocr)
    {
        LanguageResourceCoordinator resources = new(); Backend backend = new() { Hold = true };
        await using RecognitionActions actions = Create(backend, resources);
        Task command = ocr ? actions.OcrAsync(0) : actions.StartAsync(0);
        try
        {
            await backend.Entered.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            Assert.Throws<InvalidOperationException>(() => resources.AcquireMutation());
            Assert.Equal(1, backend.Calls);
        }
        finally { backend.Release.Set(); await command.WaitAsync(Limit, CancellationToken.None); }
        using IDisposable mutationAfterCompletion = resources.AcquireMutation();
    }

    [Fact]
    public async Task Queued_start_rechecks_resource_admission_after_preceding_player_work()
    {
        LanguageResourceCoordinator resources = new(); Backend backend = new();
        await using RecognitionActions actions = Create(backend, resources);
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = actions.MutateAsync(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(Limit)) throw new TimeoutException("Fixture player work was not released.");
        });
        Task? queued = null;
        try
        {
            await entered.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            queued = actions.StartAsync(0);
            using IDisposable mutation = resources.AcquireMutation();
            release.Set(); await first.WaitAsync(Limit, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => queued.WaitAsync(Limit, TestContext.Current.CancellationToken));
            Assert.Equal(0, backend.Calls);
        }
        finally
        {
            release.Set(); await first.WaitAsync(Limit, CancellationToken.None);
            if (queued != null) try { await queued.WaitAsync(Limit, CancellationToken.None); } catch (InvalidOperationException) { }
        }
        await actions.StartAsync(0); Assert.Equal(1, backend.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backend_failure_releases_the_resource_lease_and_does_not_poison_later_actions(bool ocr)
    {
        LanguageResourceCoordinator resources = new(); Backend backend = new() { Fail = true };
        await using RecognitionActions actions = Create(backend, resources);
        await Assert.ThrowsAsync<IOException>(() => ocr ? actions.OcrAsync(0) : actions.StartAsync(0));
        using (resources.AcquireMutation()) { }
        backend.Fail = false;
        await (ocr ? actions.OcrAsync(0) : actions.StartAsync(0));
        Assert.Equal(2, backend.Calls);
    }

    [Fact]
    public async Task Original_constructor_keeps_non_resource_host_compatibility()
    {
        Backend backend = new(); await using RecognitionActions actions = new(backend);
        await actions.StartAsync(0); await actions.OcrAsync(1);
        Assert.Equal(2, backend.Calls);
    }

    [Fact]
    public async Task Media_replacement_cancels_ocr_and_releases_its_resource_lease()
    {
        LanguageResourceCoordinator resources = new(); Backend backend = new() { Hold = true };
        await using RecognitionActions actions = Create(backend, resources);
        Task command = actions.OcrAsync(0);
        try
        {
            await backend.Entered.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            Assert.Throws<InvalidOperationException>(() => resources.AcquireMutation());
            actions.Invalidate();
            await Assert.ThrowsAsync<RecognitionActionCanceledException>(() => command.WaitAsync(Limit, TestContext.Current.CancellationToken));
            using IDisposable mutation = resources.AcquireMutation();
        }
        finally
        {
            backend.Release.Set();
            try { await command.WaitAsync(Limit, CancellationToken.None); } catch (RecognitionActionCanceledException) { }
        }
    }

    sealed class Backend : IRecognitionBackend
    {
        public int Calls;
        public bool Hold;
        public bool Fail;
        public ManualResetEventSlim Release { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasAudio => true;
        public bool IsLive => false;
        public bool IsBusy => false;
        public bool IsPaused => false;
        public void Start(int slot) => Run();
        public void Ocr(int slot) => Run();
        public void Ocr(int slot, CancellationToken cancellationToken) => Run(cancellationToken);
        void Run(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            if (Hold && !Release.Wait(Limit, cancellationToken)) throw new TimeoutException("Fixture backend was not released.");
            if (Fail) throw new IOException("Injected backend failure.");
        }
        public void Pause() { }
        public void Resume() { }
        public void Cancel(int slot) { }
        public void Apply(RecognitionSettings settings) { }
    }
}
