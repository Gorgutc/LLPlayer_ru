using System.Threading;
using System.Threading.Tasks;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// A cooperative, async pause gate. A worker awaits <see cref="PauseToken.WaitWhilePausedAsync"/> at a safe
/// boundary; the call completes immediately while running and only suspends (without blocking a thread) while
/// paused, completing when <see cref="Resume"/> is called — or throwing if the supplied
/// <see cref="CancellationToken"/> is cancelled.
/// </summary>
/// <remarks>
/// Used by interactive subtitle ASR to pause at a chunk boundary while keeping already-produced subtitles
/// (unlike cancellation, which clears them). The default <see cref="PauseToken"/> is never paused, so callers
/// that do not support pausing (e.g. the headless batch path) pass it and incur a no-op. Modelled on the
/// well-known PauseToken pattern; all members are thread-safe.
/// </remarks>
public sealed class PauseTokenSource
{
    // Non-null exactly while paused; its Task completes when the source is resumed.
    private volatile TaskCompletionSource<bool>? _paused;

    /// <summary>A token bound to this source; pass it to the worker that should pause.</summary>
    public PauseToken Token => new(this);

    /// <summary>Whether the source is currently paused. Setting it calls <see cref="Pause"/>/<see cref="Resume"/>.</summary>
    public bool IsPaused
    {
        get => _paused != null;
        set
        {
            if (value)
                Pause();
            else
                Resume();
        }
    }

    /// <summary>Enter the paused state. Idempotent (a second call while paused does nothing).</summary>
    public void Pause() =>
        Interlocked.CompareExchange(
            ref _paused,
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            null);

    /// <summary>Leave the paused state and release any awaiters. Idempotent (safe when not paused).</summary>
    public void Resume()
    {
        while (true)
        {
            TaskCompletionSource<bool>? tcs = _paused;
            if (tcs == null)
                return;

            // Only the caller that swaps the field back to null signals the awaiters, so Resume is race-free
            // against a concurrent Pause/Resume.
            if (Interlocked.CompareExchange(ref _paused, null, tcs) == tcs)
            {
                tcs.TrySetResult(true);
                return;
            }
        }
    }

    internal Task WaitWhilePausedAsync(CancellationToken token) =>
        _paused == null ? Task.CompletedTask : WaitWhilePausedCoreAsync(token);

    private async Task WaitWhilePausedCoreAsync(CancellationToken token)
    {
        while (true)
        {
            TaskCompletionSource<bool>? tcs = _paused;
            if (tcs == null)
                return; // resumed

            token.ThrowIfCancellationRequested();

            // Wake on either resume (tcs completes) or cancellation (cancelTcs completes via the registration),
            // without mutating the shared pause TCS on cancellation.
            TaskCompletionSource<bool> cancelTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration reg = token.Register(
                static s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), cancelTcs);
            try
            {
                await Task.WhenAny(tcs.Task, cancelTcs.Task).ConfigureAwait(false);
            }
            finally
            {
                reg.Dispose();
            }

            token.ThrowIfCancellationRequested();
            // Loop: re-read _paused. It may be null (resumed) or a brand-new TCS (paused → resumed → paused).
        }
    }
}

/// <summary>
/// A lightweight handle to a <see cref="PauseTokenSource"/>. The <c>default</c> value is never paused, so it can
/// be passed by callers that do not support pausing.
/// </summary>
public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    internal PauseToken(PauseTokenSource source) => _source = source;

    /// <summary>Whether the underlying source is currently paused (false for a default token).</summary>
    public bool IsPaused => _source?.IsPaused ?? false;

    /// <summary>
    /// Completes immediately while running (or for a default token); otherwise suspends until the source is
    /// resumed, or throws <see cref="System.OperationCanceledException"/> if <paramref name="token"/> is cancelled.
    /// </summary>
    public Task WaitWhilePausedAsync(CancellationToken token = default) =>
        _source?.WaitWhilePausedAsync(token) ?? Task.CompletedTask;
}
