namespace FlyleafLib;

#nullable enable

/// <summary>
/// Audio output stream used by the portable (non-Windows) player instead of an XAudio2 source voice.
/// <para>
/// Format: interleaved signed 16-bit PCM, <see cref="Channels"/> channels (the player always outputs 2), at
/// <see cref="SampleRate"/> (the decoded stream's sample rate). One "sample" below means one sample frame
/// (one value per channel, i.e. 4 bytes for 16-bit stereo).
/// </para>
/// <para>
/// Threading: every member may be called from the player's playback thread while the UI thread changes
/// <see cref="Volume"/> / <see cref="MasterVolume"/>; implementations must be thread-safe.
/// </para>
/// </summary>
public interface IAudioSink : IDisposable
{
    /// <summary>Sample rate (Hz) the sink was created with.</summary>
    int SampleRate { get; }

    /// <summary>Channel count the sink was created with.</summary>
    int Channels { get; }

    /// <summary>
    /// Queues <paramref name="byteCount"/> bytes of PCM starting at <paramref name="data"/> for playback.
    /// The memory is only valid during the call (the implementation must copy it).
    /// </summary>
    void Submit(nint data, int byteCount);

    /// <summary>
    /// Monotonic count of samples actually played since creation. Samples discarded by <see cref="Flush"/> are never
    /// counted. (Same semantics as XAudio2's <c>SamplesPlayed</c>, which the A/V sync logic relies on.)
    /// </summary>
    ulong SamplesPlayed { get; }

    /// <summary>Additional output latency of the device beyond the queued audio, in samples (0 when unknown).</summary>
    int LatencySamples { get; }

    /// <summary>Discards every queued (not yet played) sample.</summary>
    void Flush();

    /// <summary>Per-stream gain (1.0 = 100%, 0 = silent; values above 1 amplify).</summary>
    float Volume { get; set; }

    /// <summary>Master gain applied on top of <see cref="Volume"/> (Config.Audio.VolumeMax / 100).</summary>
    float MasterVolume { get; set; }
}

/// <summary>
/// Audio output backend of the portable build: enumerates devices and creates <see cref="IAudioSink"/>s.
/// Select it with <see cref="AudioEngine.Backend"/> before <see cref="Engine.Start"/> (default: <see cref="NullAudioBackend"/>).
/// </summary>
public interface IAudioBackend
{
    /// <summary>Backend name (for logs).</summary>
    string Name { get; }

    /// <summary>
    /// Enumerates the playback devices (excluding the virtual "Default" entry, which the engine adds itself).
    /// Returns an empty list when enumeration is not supported.
    /// </summary>
    IReadOnlyList<AudioEngine.AudioEndpoint> EnumerateDevices();

    /// <summary>Returns the system default playback device or null when unknown.</summary>
    AudioEngine.AudioEndpoint? GetDefaultDevice();

    /// <summary>
    /// Creates an output stream on <paramref name="deviceId"/> (null = system default device).
    /// Throws when the device cannot be opened (the player then disables audio, like the Windows build).
    /// </summary>
    IAudioSink CreateSink(string? deviceId, int sampleRate, int channels);
}
