namespace LLPlayer.Core.Services;

/// <summary>Coordinates short resource mutations with interactive recognition and app-owned batch runs.</summary>
public sealed class LanguageResourceCoordinator
{
    readonly object sync = new();
    int readers;
    bool mutation;

    public IDisposable AcquireUse()
    {
        lock (sync)
        {
            if (mutation) throw new InvalidOperationException("Wait for the model operation to finish before starting recognition or batch processing.");
            readers++;
            return new Lease(() => { lock (sync) readers--; });
        }
    }

    public IDisposable AcquireMutation()
    {
        lock (sync)
        {
            if (mutation || readers != 0)
                throw new InvalidOperationException("Stop recognition and batch processing before changing model files.");
            mutation = true;
            return new Lease(() => { lock (sync) mutation = false; });
        }
    }

    sealed class Lease(Action release) : IDisposable
    {
        Action? action = release;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
