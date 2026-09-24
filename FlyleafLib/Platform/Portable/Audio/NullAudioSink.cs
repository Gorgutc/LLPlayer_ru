using System.Diagnostics;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Backend with no real output device: its sinks discard audio but consume it at wall-clock (real-time) rate, so the
/// player's audio-driven A/V sync, buffering and statistics behave exactly as with a real device (headless / CI).
/// </summary>
public sealed class NullAudioBackend : IAudioBackend
{
    public static readonly NullAudioBackend Instance = new();

    public string Name => "Null";

    public IReadOnlyList<AudioEngine.AudioEndpoint> EnumerateDevices() => [];

    public AudioEngine.AudioEndpoint? GetDefaultDevice() => null;

    public IAudioSink CreateSink(string? deviceId, int sampleRate, int channels) => new NullAudioSink(sampleRate, channels);
}

/// <summary>
/// <see cref="IAudioSink"/> that drops samples while advancing <see cref="SamplesPlayed"/> in real time: queued audio
/// "plays" at <see cref="SampleRate"/> samples per second while the queue is non-empty; the clock does not run on an
/// empty queue (no phantom underrun).
/// </summary>
public sealed class NullAudioSink : IAudioSink
{
    readonly object     locker = new();
    readonly Stopwatch  clock  = Stopwatch.StartNew();
    readonly int        bytesPerSample;

    ulong   submitted;      // samples queued since creation (minus flushed)
    double  played;         // samples played since creation
    long    lastTicks;      // Stopwatch ticks of the last advance
    bool    disposed;

    public NullAudioSink(int sampleRate, int channels, int bitsPerSample = 16)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels   <= 0) throw new ArgumentOutOfRangeException(nameof(channels));

        SampleRate      = sampleRate;
        Channels        = channels;
        bytesPerSample  = channels * bitsPerSample / 8;
    }

    public int      SampleRate      { get; }
    public int      Channels        { get; }
    public int      LatencySamples  => 0;
    public float    Volume          { get; set; } = 1;
    public float    MasterVolume    { get; set; } = 1;

    public ulong SamplesPlayed
    {
        get
        {
            lock (locker)
            {
                Advance();
                return (ulong)played;
            }
        }
    }

    public void Submit(nint data, int byteCount)
    {
        if (byteCount <= 0)
            return;

        lock (locker)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NullAudioSink));

            Advance();
            submitted += (ulong)(byteCount / bytesPerSample);
        }
    }

    public void Flush()
    {
        lock (locker)
        {
            Advance();
            submitted = (ulong)played;
            played    = submitted;
        }
    }

    public void Dispose()
    {
        lock (locker)
            disposed = true;
    }

    void Advance()
    {
        long now    = clock.ElapsedTicks;
        double secs = (now - lastTicks) / (double)Stopwatch.Frequency;
        lastTicks   = now;

        if (played < submitted)
            played = Math.Min(submitted, played + secs * SampleRate);
    }
}
