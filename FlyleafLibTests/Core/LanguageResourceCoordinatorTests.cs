using LLPlayer.Core.Services;

namespace FlyleafLibTests.Core;

public class LanguageResourceCoordinatorTests
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    [Fact]
    public void Interactive_recognition_and_batch_uses_each_block_resource_mutation_until_both_release()
    {
        LanguageResourceCoordinator coordinator = new();
        using IDisposable recognition = coordinator.AcquireUse();
        using IDisposable batch = coordinator.AcquireUse();
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireMutation());
        recognition.Dispose();
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireMutation());
        batch.Dispose();
        using IDisposable mutation = coordinator.AcquireMutation();
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireUse());
    }

    [Fact]
    public void Held_mutation_blocks_new_recognition_batch_and_another_resource_mutation()
    {
        LanguageResourceCoordinator coordinator = new();
        using IDisposable mutation = coordinator.AcquireMutation();
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireUse());
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireUse());
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireMutation());
        mutation.Dispose();
        using IDisposable recognition = coordinator.AcquireUse();
        using IDisposable batch = coordinator.AcquireUse();
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireMutation());
    }

    [Fact]
    public void Repeated_dispose_cannot_release_another_users_or_mutations_ownership()
    {
        LanguageResourceCoordinator coordinator = new();
        IDisposable previousUse = coordinator.AcquireUse();
        previousUse.Dispose();
        using IDisposable currentUse = coordinator.AcquireUse();
        previousUse.Dispose(); previousUse.Dispose();
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireMutation());
        currentUse.Dispose();
        IDisposable previousMutation = coordinator.AcquireMutation();
        previousMutation.Dispose();
        using IDisposable currentMutation = coordinator.AcquireMutation();
        previousMutation.Dispose(); previousMutation.Dispose();
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireUse());
        Assert.Throws<InvalidOperationException>(() => coordinator.AcquireMutation());
        currentMutation.Dispose();
        using IDisposable availableAgain = coordinator.AcquireUse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Caller_failure_releases_use_or_mutation_in_finally(bool resourceMutation)
    {
        LanguageResourceCoordinator coordinator = new();
        InvalidOperationException expected = new("Injected caller failure.");
        Exception? failure = Record.Exception((Action)(() =>
        {
            using IDisposable lease = resourceMutation ? coordinator.AcquireMutation() : coordinator.AcquireUse();
            throw expected;
        }));
        Assert.Same(expected, failure);
        using IDisposable mutation = coordinator.AcquireMutation();
        mutation.Dispose();
        using IDisposable use = coordinator.AcquireUse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_drains_finally_and_releases_admission(bool resourceMutation)
    {
        LanguageResourceCoordinator coordinator = new();
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Work()
        {
            using IDisposable lease = resourceMutation ? coordinator.AcquireMutation() : coordinator.AcquireUse();
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
        }
        Task work = Work();
        try
        {
            await entered.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);
            Assert.Throws<InvalidOperationException>(() => coordinator.AcquireMutation());
            if (resourceMutation) Assert.Throws<InvalidOperationException>(() => coordinator.AcquireUse());
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(Limit, TestContext.Current.CancellationToken));
            using IDisposable mutation = coordinator.AcquireMutation();
            mutation.Dispose();
            using IDisposable use = coordinator.AcquireUse();
        }
        finally
        {
            cancellation.Cancel();
            try { await work.WaitAsync(Limit, CancellationToken.None); }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Concurrent_start_and_mutation_admission_are_atomic_without_a_prior_busy_check()
    {
        LanguageResourceCoordinator coordinator = new();
        // Keep the winning lease alive until both attempts return; neither operation can win by outliving the other.
        for (int iteration = 0; iteration < 32; iteration++)
        {
            TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<(IDisposable? Lease, Exception? Failure)> Attempt(Func<IDisposable> acquire) => Task.Run(async () =>
            {
                await start.Task;
                try { return ((IDisposable?)acquire(), (Exception?)null); }
                catch (InvalidOperationException failure) { return ((IDisposable?)null, (Exception?)failure); }
            });
            Task<(IDisposable? Lease, Exception? Failure)> use = Attempt(coordinator.AcquireUse);
            Task<(IDisposable? Lease, Exception? Failure)> mutation = Attempt(coordinator.AcquireMutation);
            start.TrySetResult();
            var results = await Task.WhenAll(use, mutation).WaitAsync(Limit, TestContext.Current.CancellationToken);
            try
            {
                Assert.Single(results, result => result.Lease != null);
                Assert.Single(results, result => result.Failure is InvalidOperationException);
                Assert.Throws<InvalidOperationException>(() => coordinator.AcquireMutation());
            }
            finally { foreach (var result in results) result.Lease?.Dispose(); }
            using IDisposable availableAgain = coordinator.AcquireMutation();
        }
    }
}
