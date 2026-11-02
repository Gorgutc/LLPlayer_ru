using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaPlayer;

// F-13 portable (Linux) TFM: the XAudio2 output of MediaPlayer/Audio.cs (excluded there with #if WINDOWS) is replaced
// by an IAudioSink created from AudioEngine.Backend. Public API (Volume, Mute, Device, ...) and the internal contract
// used by the screamers (AddSamples / GetBufferedDuration / GetDeviceDelay / ClearBuffer / Timebase) are unchanged.
public partial class Audio
{
    /// <summary>
    /// Audio player's volume / amplifier (valid values 0 - no upper limit)
    /// </summary>
    public int Volume
    {
        get
        {
            lock (locker)
                return output == null || Mute ? _Volume : (int) ((decimal)output.Volume * 100);
        }
        set
        {
            if (value > Config.Audio.VolumeMax || value < 0)
                return;

            if (value == 0)
                Mute = true;
            else if (Mute)
            {
                _Volume = value;
                Mute = false;
            }
            else
            {
                lock (locker)
                    if (output != null)
                        output.Volume = Math.Max(0, value / 100.0f);
            }

            Set(ref _Volume, value, false);
        }
    }
    int _Volume;

    /// <summary>
    /// Audio player's mute
    /// </summary>
    public bool Mute
    {
        get => mute;
        set
        {
            lock (locker)
            {
                if (output != null)
                    output.Volume = value ? 0 : _Volume / 100.0f;
            }

            Set(ref mute, value, false);
        }
    }
    private bool mute = false;

    /// <summary>The active output stream (null while audio is not initialized).</summary>
    internal IAudioSink sink => output?.CurrentSink;
    PortableAudioOutput output;

    /// <summary>Output loss is independent from the user's enabled setting.</summary>
    public bool OutputRecoveryRequired
    {
        get
        {
            lock (locker)
            {
                bool recovering = output?.Recovering == true;
                submittedSamples = output?.SubmittedSamples ?? 0;
                return recovering;
            }
        }
    }

    /// <summary>The host calls this through its serialized command owner, at a bounded refresh interval.</summary>
    public void RecoverOutput(bool force = false)
    {
        lock (locker)
            if (Config.Audio.Enabled && isOpened)
            {
                output?.TryRecover(_Device == Engine.Audio.DefaultDevice ? null : _Device.Id, force);
                submittedSamples = output?.SubmittedSamples ?? 0;
            }
    }

    /// <summary>Applies Config.Audio.VolumeMax (the XAudio2 mastering-voice volume on Windows).</summary>
    internal void SetMasterVolume(float volume)
    {
        lock (locker)
            if (output != null)
                output.MasterVolume = volume;
    }

    internal void Initialize()
    {
        lock (locker)
        {
            if (Engine.Audio.Failed)
                return;

            if (!isOpened || sampleRate <= 0)
                return;

            var backend = AudioEngine.Backend ?? NullAudioBackend.Instance;
            player.Log.Info($"Initialiazing audio at {sampleRate}Hz ({Device.Id}:{Device.Name}) [{backend.Name}]");

            Dispose();

            output = new PortableAudioOutput(backend, _Device == Engine.Audio.DefaultDevice ? null : _Device.Id, sampleRate, ChannelsOut)
            {
                MasterVolume = Config.Audio.VolumeMax / 100.0f,
                Volume = mute ? 0 : Math.Max(0, _Volume / 100.0f)
            };
            submittedSamples = output.SubmittedSamples;
            Timebase = 1000 * 10000.0 / sampleRate;
            curSampleRate = sampleRate;
        }
    }
    internal void Dispose()
    {
        lock (locker)
        {
            output?.Dispose();
            output = null;
            submittedSamples = 0;
        }
    }

    internal void AddSamples(AudioFrame aFrame)
    {
        lock (locker) // Output replacement, buffer clear and the PCM clock must remain one atomic operation.
        {
            try
            {
                if (CanTrace)
                    player.Log.Trace($"[A] Presenting {TicksToTime(player.aFrame.Timestamp)}");

                framesDisplayed++;

                bool accepted = output?.Submit(aFrame.dataPtr, aFrame.dataLen) == true;
                submittedSamples = output?.SubmittedSamples ?? 0;
                if (accepted) SamplesAdded?.Invoke(this, aFrame);
            }
            catch (Exception e) // Happens on audio device changed/removed
            {
                if (CanDebug)
                    player.Log.Debug($"[Audio] Submitting samples failed ({e.Message})");

                ClearBuffer();
            }
        }
    }
    internal long GetBufferedDuration()
    {
        lock (locker)
        {
            if (output == null)
                return 0;
            ulong buffered = output.BufferedSamples;
            submittedSamples = output.SubmittedSamples;
            return (long)(buffered * Timebase);
        }
    }
    internal long GetDeviceDelay()
    {
        lock (locker)
        {
            if (output == null)
                return 0;

            int latencySamples = output.LatencySamples;
            submittedSamples = output.SubmittedSamples;
            if (latencySamples <= 0)
                return 0;

            var latency = (long) ((latencySamples * Timebase) - 8_0000);
            if (latency > TimeSpan.FromMilliseconds(500).Ticks)
                return TimeSpan.FromMilliseconds(40).Ticks;

            return Math.Max(0, latency);
        }
    }
    internal void ClearBuffer()
    {
        lock (locker)
        {
            output?.Flush();
            submittedSamples = output?.SubmittedSamples ?? 0;
        }
    }
}
