namespace FlyleafLib;

#nullable enable

/// <summary>
/// F-13 (Linux port): <see cref="IAudioSink"/> that streams interleaved s16 PCM to one OpenAL source on its own
/// device + context (the portable counterpart of the Windows build's XAudio2 mastering + source voice per player).
/// <para>
/// Streaming: every <see cref="Submit"/> copies the PCM into one AL buffer (processed buffers are unqueued and reused)
/// and queues it on the source; at most <see cref="MaxQueuedBuffers"/> buffers may be queued (like XAudio2's
/// XAUDIO2_MAX_QUEUED_BUFFERS, beyond it Submit throws and the player flushes). When the source ran dry and stopped
/// (underrun) the next Submit drops the finished buffers and restarts playback with the new one.
/// </para>
/// <para>
/// Clock: <see cref="SamplesPlayed"/> = samples of fully played (unqueued) buffers + <c>AL_SAMPLE_OFFSET</c> inside the
/// queue; it is monotonic and never exceeds the samples submitted. <see cref="Flush"/> pauses the source to read the
/// exact position, counts only what was played and discards the rest (same semantics as <see cref="NullAudioSink"/>).
/// </para>
/// <para>Thread-safe: every member takes the sink's lock (then the AL context scope).</para>
/// </summary>
public sealed class OpenAlAudioSink : IAudioSink
{
    /// <summary>Maximum AL buffers queued at once (XAudio2 has the same limit of 64 queued source buffers).</summary>
    public const int MaxQueuedBuffers = 64;

    /// <summary>
    /// Output latency assumed when neither AL_SOFT_source_latency nor ALC_SOFT_device_clock is available: OpenAL Soft's
    /// default device buffer is 3 periods of 1024 sample frames and the mixer runs up to ~2 periods ahead of the
    /// speakers, i.e. ~40 ms at 48 kHz.
    /// </summary>
    public const int FallbackLatencyMs = 40;

    readonly IOpenAl    al;
    readonly Lock       locker = new();
    readonly int        format;
    readonly int        bytesPerFrame;
    readonly bool       hasSourceLatency;

    readonly nint       device;
    readonly nint       context;
    readonly uint       source;

    readonly Queue<(uint buffer, int samples)>
                        queued = new();     // AL queue order (head = playing / next to play)
    readonly Stack<uint>
                        free = new();       // unqueued buffers ready for reuse

    ulong   playedBase;     // samples of buffers removed from the queue as played (incl. the played part at Flush)
    ulong   queuedSamples;  // samples currently in the AL queue
    ulong   lastReported;   // monotonic guard for SamplesPlayed
    float   volume = 1, masterVolume = 1;
    bool    disposed;
    bool    faulted;

    /// <summary>Opens <paramref name="deviceName"/> (null = default device) with the system OpenAL library.</summary>
    /// <exception cref="InvalidOperationException">OpenAL is unavailable or the device/context cannot be created.</exception>
    public OpenAlAudioSink(string? deviceName, int sampleRate, int channels)
        : this(OpenAlNative.TryGet(out var api, out string? error) ? api : throw new InvalidOperationException(error), deviceName, sampleRate, channels)
    {
    }

    internal OpenAlAudioSink(IOpenAl al, string? deviceName, int sampleRate, int channels)
    {
        ArgumentNullException.ThrowIfNull(al);
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        format = channels switch
        {
            1 => Al.FORMAT_MONO16,
            2 => Al.FORMAT_STEREO16,
            _ => throw new ArgumentOutOfRangeException(nameof(channels), channels, "OpenAL sink supports 1 or 2 channels")
        };

        this.al         = al;
        SampleRate      = sampleRate;
        Channels        = channels;
        bytesPerFrame   = channels * 2;
        DeviceName      = deviceName;

        device = al.AlcOpenDevice(deviceName);
        if (device == 0)
            throw new InvalidOperationException($"OpenAL: cannot open device '{deviceName ?? "(default)"}'");

        try
        {
            // Ask the device to mix at the stream's rate (no resampling when the backend honours it).
            context = al.AlcCreateContext(device, [Al.ALC_FREQUENCY, sampleRate, 0]);
            if (context == 0)
                throw new InvalidOperationException($"OpenAL: cannot create a context (ALC error 0x{al.AlcGetError(device):X})");

            using (new OpenAlContextScope(al, context))
            {
                al.AlGetError(); // clear

                source = al.AlGenSource();
                Check("alGenSources");

                // Plain stereo playback: no 3D panning / distance attenuation.
                al.AlSourcei(source, Al.SOURCE_RELATIVE, Al.TRUE);
                al.AlSourcef(source, Al.ROLLOFF_FACTOR, 0);
                if (al.IsAlExtensionPresent("AL_SOFT_direct_channels"))
                    al.AlSourcei(source, Al.DIRECT_CHANNELS_SOFT, Al.TRUE);

                hasSourceLatency = al.IsAlExtensionPresent("AL_SOFT_source_latency");
                ApplyGains();
                Check("source setup");
            }
        }
        catch
        {
            DestroyNative();
            throw;
        }
    }

    /// <summary>Device name the sink was opened with (null = default device).</summary>
    public string?  DeviceName  { get; }

    public int      SampleRate  { get; }
    public int      Channels    { get; }

    /// <summary>Times playback was restarted after the source ran out of queued audio (diagnostics).</summary>
    public int      UnderrunRestarts { get; private set; }

    /// <summary>Why the sink became unusable (null while healthy); see <see cref="Fault"/>.</summary>
    public string?  FaultReason { get; private set; }

    /// <summary>AL buffers currently queued on the source (diagnostics/tests).</summary>
    public int QueuedBuffers
    {
        get { lock (locker) return queued.Count; }
    }

    /// <summary>AL buffers allocated by this sink (queued + reusable).</summary>
    public int AllocatedBuffers
    {
        get { lock (locker) return queued.Count + free.Count; }
    }

    public float Volume
    {
        get { lock (locker) return volume; }
        set
        {
            lock (locker)
            {
                volume = SanitizeGain(value);
                ApplyGainsIfOpen();
            }
        }
    }

    public float MasterVolume
    {
        get { lock (locker) return masterVolume; }
        set
        {
            lock (locker)
            {
                masterVolume = SanitizeGain(value);
                ApplyGainsIfOpen();
            }
        }
    }

    public ulong SamplesPlayed
    {
        get
        {
            lock (locker)
            {
                if (disposed || faulted)
                    return lastReported;

                try
                {
                    using (new OpenAlContextScope(al, context))
                        return UpdatePlayed();
                }
                catch (Exception e)
                {
                    Fault(e);
                    return lastReported;
                }
            }
        }
    }

    public int LatencySamples
    {
        get
        {
            lock (locker)
            {
                if (disposed)
                    return 0;

                long ns = -1;
                if (!faulted)
                {
                    try
                    {
                        if (hasSourceLatency)
                            using (new OpenAlContextScope(al, context))
                                if (al.TryGetSourceLatencyNs(source, out long srcNs))
                                    ns = srcNs;

                        if (ns < 0)
                            ns = al.AlcGetDeviceLatencyNs(device);
                    }
                    catch (Exception) { ns = -1; }
                }

                if (ns < 0)
                    return FallbackLatencyMs * SampleRate / 1000;

                return (int)Math.Min(int.MaxValue, ns * SampleRate / 1_000_000_000L);
            }
        }
    }

    public void Submit(nint data, int byteCount)
    {
        int samples = byteCount / bytesPerFrame;
        if (samples <= 0)
            return;

        lock (locker)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
                throw new InvalidOperationException($"OpenAL sink is unusable ({FaultReason})");

            if (!al.AlcIsConnected(device))
                throw new InvalidOperationException($"OpenAL: device '{DeviceName ?? "(default)"}' was disconnected");

            using (new OpenAlContextScope(al, context))
            {
                al.AlGetError(); // clear
                Reclaim();

                if (queued.Count >= MaxQueuedBuffers)
                    throw new InvalidOperationException($"OpenAL: too many queued buffers ({queued.Count})");

                uint buffer;
                if (free.Count > 0)
                    buffer = free.Pop();
                else
                {
                    buffer = al.AlGenBuffer();
                    Check("alGenBuffers");
                }

                al.AlBufferData(buffer, format, data, samples * bytesPerFrame, SampleRate);
                int err = al.AlGetError();
                if (err == Al.NO_ERROR)
                {
                    al.AlSourceQueueBuffer(source, buffer);
                    err = al.AlGetError();
                }

                if (err != Al.NO_ERROR)
                {
                    free.Push(buffer);
                    throw new InvalidOperationException($"OpenAL: queueing audio failed (AL error 0x{err:X})");
                }

                queued.Enqueue((buffer, samples));
                queuedSamples += (ulong)samples;

                int state = al.AlGetSourcei(source, Al.SOURCE_STATE);
                if (state != Al.PLAYING)
                {
                    if (state == Al.STOPPED)
                    {
                        // Underrun: the source played everything queued before this buffer and stopped. A stopped
                        // source restarts from its queue head, so drop the finished buffers (counted as played) first.
                        UnqueueHead(queued.Count - 1, countAsPlayed: true);
                        UnderrunRestarts++;
                    }

                    al.AlSourcePlay(source);
                    Check("alSourcePlay");
                }
            }
        }
    }

    public void Flush()
    {
        lock (locker)
        {
            if (disposed || faulted)
                return;

            try
            {
                using (new OpenAlContextScope(al, context))
                {
                    al.AlGetError(); // clear

                    // Freeze the position so the played part is exact, count it, then drop everything still queued.
                    if (al.AlGetSourcei(source, Al.SOURCE_STATE) == Al.PLAYING)
                        al.AlSourcePause(source);

                    ulong played = UpdatePlayed();

                    al.AlSourceStop(source);    // every queued buffer becomes processed
                    UnqueueHead(queued.Count, countAsPlayed: false);
                    al.AlSourceRewind(source);  // back to AL_INITIAL: the next Submit is a fresh start, not an underrun
                    al.AlGetError();

                    playedBase      = played;
                    lastReported    = played;
                }
            }
            catch (Exception e)
            {
                Fault(e);
            }
        }
    }

    public void Dispose()
    {
        lock (locker)
        {
            if (disposed)
                return;

            disposed = true;
            DestroyNative();
        }
    }

    // === Internals (caller holds locker; AL calls need the context scope) =========================================

    /// <summary>Unqueues the processed buffers (counted as played) and returns the monotonic played position.</summary>
    ulong UpdatePlayed()
    {
        Reclaim();

        ulong offset = 0;
        if (queuedSamples > 0)
        {
            int state = al.AlGetSourcei(source, Al.SOURCE_STATE);
            if (state == Al.PLAYING || state == Al.PAUSED)
            {
                int sampleOffset = al.AlGetSourcei(source, Al.SAMPLE_OFFSET);
                if (sampleOffset > 0)
                    offset = Math.Min((ulong)sampleOffset, queuedSamples);
            }
            // AL_STOPPED: everything queued has played but a buffer finished between Reclaim and the state query;
            // the next call unqueues it (reporting slightly less meanwhile keeps the value monotonic and safe).
        }

        ulong value = playedBase + offset;
        if (value < lastReported)
            value = lastReported;

        lastReported = value;
        return value;
    }

    /// <summary>Unqueues every processed buffer, counts it as played and keeps it for reuse.</summary>
    void Reclaim()
    {
        if (queued.Count == 0)
            return;

        int processed = al.AlGetSourcei(source, Al.BUFFERS_PROCESSED);
        if (processed > 0)
            UnqueueHead(Math.Min(processed, queued.Count), countAsPlayed: true);
    }

    void UnqueueHead(int count, bool countAsPlayed)
    {
        if (count <= 0)
            return;

        Span<uint> ids = count <= MaxQueuedBuffers ? stackalloc uint[count] : new uint[count];
        al.AlSourceUnqueueBuffers(source, ids);

        int err = al.AlGetError();
        if (err != Al.NO_ERROR)
            throw new InvalidOperationException($"OpenAL: alSourceUnqueueBuffers failed (AL error 0x{err:X})");

        for (int i = 0; i < count; i++)
        {
            var (buffer, samples) = queued.Dequeue();
            queuedSamples -= (ulong)samples;
            if (countAsPlayed)
                playedBase += (ulong)samples;

            free.Push(buffer);
        }
    }

    void ApplyGainsIfOpen()
    {
        if (disposed || faulted)
            return;

        try
        {
            using (new OpenAlContextScope(al, context))
                ApplyGains();
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// Marks the sink unusable after an unexpected AL failure (broken context): the clock freezes at the last reported
    /// value with nothing queued, Submit throws (the player then flushes, as after an XAudio2 device error) and the
    /// read paths never throw on the playback thread.
    /// </summary>
    void Fault(Exception e)
    {
        faulted         = true;
        FaultReason     = e.Message;
        playedBase      = lastReported;
        queuedSamples   = 0;
    }

    static float SanitizeGain(float value)
        => float.IsFinite(value) ? Math.Max(0, value) : 0;

    void ApplyGains()
    {
        // AL_GAIN is clamped to AL_MAX_GAIN (1 by default); raise it so volumes above 100% amplify like XAudio2.
        al.AlSourcef(source, Al.MAX_GAIN, Math.Max(1, volume));
        al.AlSourcef(source, Al.GAIN, volume);
        al.AlListenerf(Al.GAIN, masterVolume);
    }

    void Check(string operation)
    {
        int err = al.AlGetError();
        if (err != Al.NO_ERROR)
            throw new InvalidOperationException($"OpenAL: {operation} failed (AL error 0x{err:X})");
    }

    /// <summary>Releases source, buffers, context and device; never throws (used by Dispose and failed construction).</summary>
    void DestroyNative()
    {
        if (context != 0)
        {
            try
            {
                using (new OpenAlContextScope(al, context))
                {
                    if (source != 0)
                    {
                        al.AlSourceStop(source);
                        al.AlSourcei(source, Al.BUFFER, 0); // detaches every queued buffer
                        al.AlDeleteSource(source);
                    }

                    while (queued.Count > 0)
                        free.Push(queued.Dequeue().buffer);
                    queuedSamples = 0;

                    while (free.Count > 0)
                        al.AlDeleteBuffer(free.Pop());

                    al.AlGetError();
                }
            }
            catch (Exception) { } // context unusable: destroying it below releases its objects anyway

            if (!al.SupportsThreadLocalContext)
            {
                lock (OpenAlContextScope.ProcessWideLock)
                {
                    al.AlcMakeContextCurrent(0);
                    al.AlcDestroyContext(context);
                }
            }
            else
                al.AlcDestroyContext(context);
        }

        if (device != 0)
            al.AlcCloseDevice(device);
    }
}
