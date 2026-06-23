namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Per-file access to a TTS backend. Cheap to create — a local implementation holds a reference to a
/// run-scoped <see cref="DubSidecarHost"/> (the heavy model is loaded once by the host, not here),
/// a cloud implementation holds an HTTP client. Synthesizes one line at a time to a temp WAV.
/// </summary>
public interface ITtsService : IAsyncDisposable
{
    /// <summary>Ensure the backend is ready (start/await the sidecar, validate the cloud key, etc.).</summary>
    Task<bool> TryInitializeAsync(CancellationToken token);

    /// <summary>Synthesize a single line to a temp WAV file.</summary>
    Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken token);

    /// <summary>Available voices for the voice-bank / override UI.</summary>
    Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken token);
}
