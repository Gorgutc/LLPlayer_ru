#nullable enable

namespace FlyleafLib.MediaPlayer;

/// <summary>
/// The process boundary could not be confirmed empty. Keep ownership until the stop retry succeeds.
/// </summary>
public sealed class EngineProcessDrainException(string message, Exception cause, Func<ValueTask> retryStop)
    : IOException(message, cause)
{
    public Func<ValueTask> RetryStop { get; } = retryStop;
}
