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
                return sink == null || Mute ? _Volume : (int) ((decimal)sink.Volume * 100);
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
                    if (sink != null)
                        sink.Volume = Math.Max(0, value / 100.0f);
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
                if (sink == null)
                    return;

                sink.Volume = value ? 0 : _Volume / 100.0f;
            }

            Set(ref mute, value, false);
        }
    }
    private bool mute = false;

    /// <summary>The active output stream (null while audio is not initialized).</summary>
    internal IAudioSink sink;

    /// <summary>Applies Config.Audio.VolumeMax (the XAudio2 mastering-voice volume on Windows).</summary>
    internal void SetMasterVolume(float volume)
    {
        lock (locker)
            if (sink != null)
                sink.MasterVolume = volume;
    }

    internal void Initialize()
    {
        lock (locker)
        {
            if (Engine.Audio.Failed)
            {
                Task.Run(() => { Config.Audio.Enabled = false; }); // Deadlock during Stop (same thread with RunThread)
                return;
            }

            if (!isOpened || sampleRate <= 0)
                return;

            var backend = AudioEngine.Backend ?? NullAudioBackend.Instance;
            player.Log.Info($"Initialiazing audio at {sampleRate}Hz ({Device.Id}:{Device.Name}) [{backend.Name}]");

            Dispose();

            try
            {
                sink = backend.CreateSink(_Device == Engine.Audio.DefaultDevice ? null : _Device.Id, sampleRate, ChannelsOut);

                submittedSamples    = sink.SamplesPlayed;
                Timebase            = 1000 * 10000.0 / sampleRate;
                sink.MasterVolume   = Config.Audio.VolumeMax / 100.0f;
                sink.Volume         = mute ? 0 : Math.Max(0, _Volume / 100.0f);
                curSampleRate       = sampleRate;
            }
            catch (Exception e)
            {
                player.Log.Info($"Audio initialization failed ({e.Message})");
                sink?.Dispose();
                sink = null;
                Config.Audio.Enabled = false;
            }
        }
    }
    internal void Dispose()
    {
        lock (locker)
        {
            if (sink == null)
                return;

            sink.Dispose();
            sink = null;
        }
    }

    internal void AddSamples(AudioFrame aFrame)
    {
        lock (locker) // required for submittedSamples only? (ClearBuffer() can be called during audio decocder circular buffer reallocation)
        {
            try
            {
                if (CanTrace)
                    player.Log.Trace($"[A] Presenting {TicksToTime(player.aFrame.Timestamp)}");

                framesDisplayed++;

                submittedSamples += (ulong) (aFrame.dataLen / 4); // ASampleBytes
                SamplesAdded?.Invoke(this, aFrame);

                sink.Submit(aFrame.dataPtr, aFrame.dataLen);
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
            if (sink == null)
                return 0;

            ulong played = sink.SamplesPlayed;
            return played >= submittedSamples ? 0 : (long) ((submittedSamples - played) * Timebase);
        }
    }
    internal long GetDeviceDelay()
    {
        lock (locker)
        {
            if (sink == null)
                return 0;

            int latencySamples = sink.LatencySamples;
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
            if (submittedSamples == 0 || sink == null)
                return;

            sink.Flush();
            submittedSamples = sink.SamplesPlayed;
        }
    }
}
