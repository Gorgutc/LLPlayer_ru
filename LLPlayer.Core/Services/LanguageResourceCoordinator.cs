namespace LLPlayer.Core.Services;

/// <summary>Coordinates short resource mutations with interactive recognition and app-owned batch runs.</summary>
public sealed class LanguageResourceCoordinator
{
    readonly object sync = new();
    int readers;
    bool mutation;
    TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsInUse { get { lock (sync) return mutation || readers > 0; } }

    public IDisposable AcquireUse()
    {
        lock (sync)
        {
            if (mutation) throw new InvalidOperationException("Wait for the model operation to finish before starting recognition or batch processing.");
            readers++;
            return new Lease(() =>
            {
                lock (sync)
                    if (--readers == 0) NotifyChanged();
            });
        }
    }

    public IDisposable AcquireMutation()
    {
        lock (sync)
        {
            if (mutation || readers != 0)
                throw new InvalidOperationException("Stop recognition and batch processing before changing model files.");
            return TakeMutation();
        }
    }

    public async ValueTask<IDisposable> WaitForMutationAsync(CancellationToken token)
    {
        while (true)
        {
            Task notification;
            lock (sync)
            {
                token.ThrowIfCancellationRequested();
                if (!mutation && readers == 0) return TakeMutation();
                notification = changed.Task;
            }
            await notification.WaitAsync(token).ConfigureAwait(false);
        }
    }

    Lease TakeMutation()
    {
        mutation = true;
        return new Lease(() =>
        {
            lock (sync)
            {
                mutation = false;
                NotifyChanged();
            }
        });
    }

    void NotifyChanged()
    {
        changed.TrySetResult();
        changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    sealed class Lease(Action release) : IDisposable
    {
        Action? action = release;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
