namespace FlyleafLib;

#nullable enable

/// <summary>A temporary full queue is backpressure, rather than loss of the output device.</summary>
public sealed class AudioQueueFullException(string message) : InvalidOperationException(message);

/// <summary>
/// Owns a single player's output and accepted-PCM clock. Output loss drops queued PCM and uses a silent clock;
/// only the host's serialized, bounded refresh may attempt to open a device again.
/// </summary>
internal sealed class PortableAudioOutput : IDisposable
{
    readonly object gate = new();
    readonly IAudioBackend backend;
    readonly int sampleRate, channels;
    IAudioSink current;
    ulong submitted;
    float volume = 1, masterVolume = 1;
    bool disposed, recovering;

    internal PortableAudioOutput(IAudioBackend backend, string? deviceId, int sampleRate, int channels)
    {
        this.backend = backend;
        this.sampleRate = sampleRate;
        this.channels = channels;
        try { current = backend.CreateSink(deviceId, sampleRate, channels); }
        catch { current = new NullAudioSink(sampleRate, channels); }
        recovering = current is NullAudioSink && backend != NullAudioBackend.Instance;
        try { submitted = current.SamplesPlayed; RetireUnhealthyOutput(); }
        catch { UseSilentClock(); }
    }

    internal IAudioSink CurrentSink { get { lock (gate) return current; } }
    internal ulong SubmittedSamples { get { lock (gate) return submitted; } }
    internal bool Recovering { get { lock (gate) { RetireUnhealthyOutput(); return recovering; } } }
    internal ulong BufferedSamples
    {
        get
        {
            lock (gate)
            {
                if (disposed) return 0;
                try
                {
                    ulong played = current.SamplesPlayed;
                    return RetireUnhealthyOutput() || played >= submitted ? 0 : submitted - played;
                }
                catch { UseSilentClock(); return 0; }
            }
        }
    }
    internal int LatencySamples
    {
        get
        {
            lock (gate)
            {
                if (disposed) return 0;
                try { int latency = current.LatencySamples; return RetireUnhealthyOutput() ? 0 : latency; }
                catch { UseSilentClock(); return 0; }
            }
        }
    }
    internal float Volume
    {
        get { lock (gate) return volume; }
        set
        {
            lock (gate)
            {
                volume = value;
                if (disposed) return;
                try { current.Volume = value; RetireUnhealthyOutput(); }
                catch { UseSilentClock(); }
            }
        }
    }
    internal float MasterVolume
    {
        set
        {
            lock (gate)
            {
                masterVolume = value;
                if (disposed) return;
                try { current.MasterVolume = value; RetireUnhealthyOutput(); }
                catch { UseSilentClock(); }
            }
        }
    }

    internal bool Submit(nint data, int byteCount)
    {
        lock (gate)
        {
            if (disposed) return false;
            try
            {
                current.Submit(data, byteCount);
                submitted += (ulong)(Math.Max(0, byteCount) / (channels * 2));
                return true;
            }
            catch (AudioQueueFullException)
            {
                Flush();
                return false;
            }
            catch
            {
                UseSilentClock();
                return false;
            }
        }
    }

    internal void Flush()
    {
        lock (gate)
        {
            if (disposed) return;
            try { current.Flush(); submitted = current.SamplesPlayed; RetireUnhealthyOutput(); }
            catch { UseSilentClock(); }
        }
    }

    internal bool TryRecover(string? deviceId, bool force = false)
    {
        lock (gate)
        {
            if (disposed || backend == NullAudioBackend.Instance) return false;
            RetireUnhealthyOutput();
            if (!recovering && !force) return false;
            IAudioSink? replacement = null;
            ulong replacementSamples;
            try
            {
                replacement = backend.CreateSink(deviceId, sampleRate, channels);
                if (replacement is NullAudioSink)
                {
                    replacement.Dispose();
                    return false;
                }
                replacement.Volume = volume;
                replacement.MasterVolume = masterVolume;
                replacementSamples = replacement.SamplesPlayed;
                if (!replacement.IsOperational) throw new InvalidOperationException("The replacement audio output is unusable.");
            }
            catch
            {
                replacement?.Dispose();
                // A default-device replacement can fail while the old device still accepts samples.
                // Retire it now so the next bounded refresh retries, without replaying the old queue.
                if (force && !recovering) UseSilentClock();
                return false;
            }
            current.Dispose();
            current = replacement;
            submitted = replacementSamples;
            recovering = false;
            return true;
        }
    }

    bool RetireUnhealthyOutput()
    {
        if (disposed) return false;
        try { if (current.IsOperational) return false; }
        catch { /* A failed health read is itself output loss. */ }
        UseSilentClock();
        return true;
    }

    void UseSilentClock()
    {
        current.Dispose();
        current = new NullAudioSink(sampleRate, channels) { Volume = volume, MasterVolume = masterVolume };
        submitted = 0;
        recovering = backend != NullAudioBackend.Instance;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            current.Dispose();
        }
    }
}
