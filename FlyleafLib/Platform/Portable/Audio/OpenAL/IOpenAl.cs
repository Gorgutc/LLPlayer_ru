namespace FlyleafLib;

#nullable enable

/// <summary>
/// F-13 (Linux port): the subset of the OpenAL 1.1 / ALC API (plus the OpenAL Soft extensions it optionally uses) that
/// <see cref="OpenAlAudioSink"/> and <see cref="OpenAlAudioBackend"/> need. <see cref="OpenAlNative"/> is the real
/// implementation (libopenal); tests substitute a fake to verify buffer accounting without a sound device.
/// <para>
/// Handles: devices and contexts are native pointers (<c>ALCdevice*</c> / <c>ALCcontext*</c>, 0 = null); sources and
/// buffers are AL names. Every <c>Al*</c> member acts on the calling thread's current context (see
/// <see cref="OpenAlContextScope"/>). Members never throw for AL errors; callers read <see cref="AlGetError"/>.
/// </para>
/// </summary>
internal interface IOpenAl
{
    // === ALC (device / context) ==================================================================================

    /// <summary><c>alcIsExtensionPresent</c> (<paramref name="device"/> 0 = enumeration-level extensions).</summary>
    bool IsAlcExtensionPresent(nint device, string name);

    /// <summary><c>alcGetString</c> for a single NUL-terminated string (UTF-8); null when the call returns NULL.</summary>
    string? AlcGetString(nint device, int param);

    /// <summary><c>alcGetString</c> for a double-NUL-terminated list (device specifier lists).</summary>
    IReadOnlyList<string> AlcGetStringList(nint device, int param);

    /// <summary><c>alcOpenDevice</c> (<paramref name="name"/> null = default device); 0 on failure.</summary>
    nint AlcOpenDevice(string? name);

    /// <summary><c>alcCloseDevice</c>.</summary>
    bool AlcCloseDevice(nint device);

    /// <summary><c>alcCreateContext</c> with a 0-terminated attribute list; 0 on failure.</summary>
    nint AlcCreateContext(nint device, ReadOnlySpan<int> attributes);

    /// <summary><c>alcDestroyContext</c>.</summary>
    void AlcDestroyContext(nint context);

    /// <summary><c>alcGetError</c>.</summary>
    int AlcGetError(nint device);

    /// <summary><c>alcGetIntegerv(ALC_CONNECTED)</c> (ALC_EXT_disconnect); true when the extension is missing.</summary>
    bool AlcIsConnected(nint device);

    /// <summary>
    /// <c>alcGetInteger64vSOFT(ALC_DEVICE_LATENCY_SOFT)</c> (ALC_SOFT_device_clock) in nanoseconds; -1 when unsupported.
    /// </summary>
    long AlcGetDeviceLatencyNs(nint device);

    /// <summary>Whether ALC_EXT_thread_local_context (<see cref="AlcSetThreadContext"/>) is available.</summary>
    bool SupportsThreadLocalContext { get; }

    /// <summary><c>alcSetThreadContext</c> (ALC_EXT_thread_local_context; 0 clears the thread's context).</summary>
    bool AlcSetThreadContext(nint context);

    /// <summary><c>alcMakeContextCurrent</c> (process-wide current context; 0 clears it).</summary>
    bool AlcMakeContextCurrent(nint context);

    // === AL (current context) ====================================================================================

    /// <summary><c>alIsExtensionPresent</c>.</summary>
    bool IsAlExtensionPresent(string name);

    /// <summary><c>alGetError</c> (returns and clears the context's error state).</summary>
    int AlGetError();

    /// <summary><c>alGenSources(1)</c>.</summary>
    uint AlGenSource();

    /// <summary><c>alDeleteSources(1)</c>.</summary>
    void AlDeleteSource(uint source);

    /// <summary><c>alGenBuffers(1)</c>.</summary>
    uint AlGenBuffer();

    /// <summary><c>alDeleteBuffers(1)</c>.</summary>
    void AlDeleteBuffer(uint buffer);

    /// <summary><c>alBufferData</c> (copies <paramref name="size"/> bytes from <paramref name="data"/>).</summary>
    void AlBufferData(uint buffer, int format, nint data, int size, int frequency);

    /// <summary><c>alSourceQueueBuffers(1)</c>.</summary>
    void AlSourceQueueBuffer(uint source, uint buffer);

    /// <summary><c>alSourceUnqueueBuffers</c>: removes <c>buffers.Length</c> processed buffers from the queue head.</summary>
    void AlSourceUnqueueBuffers(uint source, Span<uint> buffers);

    /// <summary><c>alGetSourcei</c>.</summary>
    int AlGetSourcei(uint source, int param);

    /// <summary>
    /// <c>alGetSourcei64vSOFT(AL_SAMPLE_OFFSET_LATENCY_SOFT)</c> (AL_SOFT_source_latency): the output latency in
    /// nanoseconds; false when unsupported.
    /// </summary>
    bool TryGetSourceLatencyNs(uint source, out long latencyNs);

    /// <summary><c>alSourcePlay</c>.</summary>
    void AlSourcePlay(uint source);

    /// <summary><c>alSourcePause</c>.</summary>
    void AlSourcePause(uint source);

    /// <summary><c>alSourceStop</c>.</summary>
    void AlSourceStop(uint source);

    /// <summary><c>alSourceRewind</c>.</summary>
    void AlSourceRewind(uint source);

    /// <summary><c>alSourcef</c>.</summary>
    void AlSourcef(uint source, int param, float value);

    /// <summary><c>alSourcei</c>.</summary>
    void AlSourcei(uint source, int param, int value);

    /// <summary><c>alListenerf</c>.</summary>
    void AlListenerf(int param, float value);
}

/// <summary>OpenAL / ALC enum values used by the portable audio backend (from al.h, alc.h and alext.h).</summary>
internal static class Al
{
    public const int NO_ERROR                       = 0;
    public const int TRUE                           = 1;

    public const int SOURCE_RELATIVE                = 0x202;
    public const int GAIN                           = 0x100A;
    public const int MAX_GAIN                       = 0x100E;
    public const int BUFFER                         = 0x1009;
    public const int SOURCE_STATE                   = 0x1010;
    public const int INITIAL                        = 0x1011;
    public const int PLAYING                        = 0x1012;
    public const int PAUSED                         = 0x1013;
    public const int STOPPED                        = 0x1014;
    public const int BUFFERS_QUEUED                 = 0x1015;
    public const int BUFFERS_PROCESSED              = 0x1016;
    public const int ROLLOFF_FACTOR                 = 0x1021;
    public const int SAMPLE_OFFSET                  = 0x1025;
    public const int FORMAT_MONO16                  = 0x1101;
    public const int FORMAT_STEREO16                = 0x1103;

    /// <summary>AL_SOFT_direct_channels: stereo buffers go straight to the matching output channels (no panning).</summary>
    public const int DIRECT_CHANNELS_SOFT           = 0x1033;

    /// <summary>AL_SOFT_source_latency.</summary>
    public const int SAMPLE_OFFSET_LATENCY_SOFT     = 0x1200;

    public const int ALC_NO_ERROR                   = 0;
    public const int ALC_CONNECTED                  = 0x313;
    public const int ALC_DEFAULT_DEVICE_SPECIFIER   = 0x1004;
    public const int ALC_DEVICE_SPECIFIER           = 0x1005;
    public const int ALC_FREQUENCY                  = 0x1007;
    public const int ALC_DEFAULT_ALL_DEVICES_SPECIFIER = 0x1012;
    public const int ALC_ALL_DEVICES_SPECIFIER      = 0x1013;

    /// <summary>ALC_SOFT_device_clock.</summary>
    public const int ALC_DEVICE_LATENCY_SOFT        = 0x1601;
}
