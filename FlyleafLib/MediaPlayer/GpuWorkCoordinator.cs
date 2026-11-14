#nullable enable

namespace FlyleafLib.MediaPlayer;

/// <summary>Admits one managed GPU workflow in an app process. A failed stop retains its admission.</summary>
public sealed class GpuWorkCoordinator
{
    readonly SemaphoreSlim slot = new(1, 1);
    readonly object sync = new();
    GpuWorkLease? pending;
    Task<bool>? retry;

    public event Action? Changed;

    public string? PendingDrain
    {
        get { lock (sync) return pending?.Operation; }
    }

    public async ValueTask<GpuWorkLease> AcquireAsync(string operation, CancellationToken token)
    {
        await slot.WaitAsync(token).ConfigureAwait(false);
        if (token.IsCancellationRequested)
        {
            slot.Release();
            token.ThrowIfCancellationRequested();
        }

        return new GpuWorkLease(this, operation);
    }

    internal void Complete(GpuWorkLease lease)
    {
        if (lease.RetryStop == null)
        {
            slot.Release();
            return;
        }

        lock (sync) pending = lease;
        NotifyChanged();
    }

    public Task<bool> RetryDrainAsync() => RetryDrainAsync(null);

    /// <summary>Retries a pending stop for the given operation. A null operation retries any owner.</summary>
    public Task<bool> RetryDrainAsync(string? operation)
    {
        GpuWorkLease owner;
        TaskCompletionSource<bool> completion;
        lock (sync)
        {
            if (pending == null || (operation != null && pending.Operation != operation)) return Task.FromResult(true);
            if (retry != null) return retry;
            owner = pending;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            retry = completion.Task;
        }

        _ = DrainAsync(owner, completion);
        return completion.Task;
    }

    async Task DrainAsync(GpuWorkLease owner, TaskCompletionSource<bool> completion)
    {
        try
        {
            await owner.RetryStop!().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (sync) retry = null;
            completion.SetException(exception);
            return;
        }

        lock (sync)
        {
            pending = null;
            retry = null;
        }
        slot.Release();
        completion.SetResult(true);
        NotifyChanged();
    }

    void NotifyChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception exception) { System.Diagnostics.Trace.TraceError(exception.ToString()); }
    }
}

/// <summary>Owned by one workflow from admission until its last managed process exits.</summary>
public sealed class GpuWorkLease
{
    readonly GpuWorkCoordinator coordinator;
    readonly object sync = new();
    bool completed;

    internal GpuWorkLease(GpuWorkCoordinator coordinator, string operation)
    {
        this.coordinator = coordinator;
        Operation = operation;
    }

    internal string Operation { get; }
    internal Func<ValueTask>? RetryStop { get; private set; }

    public void RequireDrain(Func<ValueTask> retryStop)
    {
        ArgumentNullException.ThrowIfNull(retryStop);
        lock (sync)
        {
            if (completed || RetryStop != null)
                throw new InvalidOperationException("The GPU job already has a completion decision.");
            RetryStop = retryStop;
        }
    }

    public void Complete()
    {
        lock (sync)
        {
            if (completed) return;
            completed = true;
        }
        coordinator.Complete(this);
    }
}
