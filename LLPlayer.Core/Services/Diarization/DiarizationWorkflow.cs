using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Diarization;

namespace LLPlayer.Core.Services.Diarization;

/// <summary>One detached text cue's half-open interval in the selected audio stream's media clock.</summary>
public readonly record struct DiarizationCue(TimeSpan Start, TimeSpan End);

/// <summary>Runs the manual diarization job independently of the player and its media locks.</summary>
public sealed class DiarizationWorkflow(GpuWorkCoordinator coordinator)
{
    const string Operation = "Diarization";

    int running;
    Func<ValueTask>? retryCpuStop;

    public bool HasPendingDrain => Volatile.Read(ref retryCpuStop) != null || coordinator.PendingDrain == Operation;

    public async Task<IReadOnlyList<string?>> RunAsync(IDiarizationEngine engine, DiarizationAudioSource source,
        IReadOnlyList<DiarizationCue> cues, bool useGpu, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            throw new InvalidOperationException("A diarization operation is already running.");

        try
        {
            if (HasPendingDrain)
                throw new InvalidOperationException("Retry diarization cleanup before starting a new job.");

            DiarizationCue[] snapshot = cues.ToArray();
            IReadOnlyList<DiarizationTurn> turns = await InferAsync(engine, source, useGpu, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return DiarizationCueMatcher.Match(turns, snapshot, cancellationToken);
        }
        finally { Volatile.Write(ref running, 0); }
    }

    async Task<IReadOnlyList<DiarizationTurn>> InferAsync(IDiarizationEngine engine, DiarizationAudioSource source,
        bool useGpu, CancellationToken cancellationToken)
    {
        GpuWorkLease? lease = useGpu ? await coordinator.AcquireAsync(Operation, cancellationToken).ConfigureAwait(false) : null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await engine.RunAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (EngineProcessDrainException exception)
        {
            if (lease != null) lease.RequireDrain(exception.RetryStop);
            else Volatile.Write(ref retryCpuStop, exception.RetryStop);
            throw;
        }
        finally { lease?.Complete(); }
    }

    /// <summary>Retries cleanup after the active run has exited. Returns false while a run or another retry owns this workflow.</summary>
    public async Task<bool> RetryDrainAsync()
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return false;

        try
        {
            if (retryCpuStop is { } stop)
            {
                await stop().ConfigureAwait(false);
                Volatile.Write(ref retryCpuStop, null);
            }
            return await coordinator.RetryDrainAsync(Operation).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref running, 0); }
    }
}
