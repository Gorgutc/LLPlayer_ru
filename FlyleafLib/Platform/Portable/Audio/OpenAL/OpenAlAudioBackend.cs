namespace FlyleafLib;

#nullable enable

/// <summary>
/// F-13 (Linux port): <see cref="IAudioBackend"/> over OpenAL Soft (which itself outputs to PipeWire, PulseAudio, ALSA,
/// OSS, JACK, ... per its configuration). Device ids are the OpenAL device names, which are stable across runs.
/// Obtain it with <see cref="TryCreate"/> or <see cref="AudioBackendFactory.CreateDefault()"/>, and assign it to
/// <see cref="AudioEngine.Backend"/> before <see cref="Engine.Start"/>.
/// </summary>
public sealed class OpenAlAudioBackend : IAudioBackend
{
    readonly IOpenAl al;

    internal OpenAlAudioBackend(IOpenAl al)
        => this.al = al ?? throw new ArgumentNullException(nameof(al));

    /// <summary>Loads the system OpenAL library; false (with the reason) when it is not installed or incomplete.</summary>
    public static bool TryCreate(out OpenAlAudioBackend? backend, out string? error)
    {
        backend = OpenAlNative.TryGet(out var api, out error) ? new(api) : null;
        return backend != null;
    }

    public string Name => "OpenAL";

    internal IOpenAl Api => al;

    /// <summary>
    /// Playback devices: ALC_ENUMERATE_ALL_EXT's full list (every output of every OpenAL Soft backend) when available,
    /// otherwise ALC_ENUMERATION_EXT's device list; empty when enumeration is not supported.
    /// </summary>
    public IReadOnlyList<AudioEngine.AudioEndpoint> EnumerateDevices()
    {
        IReadOnlyList<string> names;
        if (al.IsAlcExtensionPresent(0, "ALC_ENUMERATE_ALL_EXT"))
            names = al.AlcGetStringList(0, Al.ALC_ALL_DEVICES_SPECIFIER);
        else if (al.IsAlcExtensionPresent(0, "ALC_ENUMERATION_EXT"))
            names = al.AlcGetStringList(0, Al.ALC_DEVICE_SPECIFIER);
        else
            return [];

        List<AudioEngine.AudioEndpoint> devices = [];
        HashSet<string> seen = [];
        foreach (string name in names)
            if (!string.IsNullOrEmpty(name) && seen.Add(name))
                devices.Add(new() { Id = name, Name = name });

        return devices;
    }

    public AudioEngine.AudioEndpoint? GetDefaultDevice()
    {
        string? name = al.IsAlcExtensionPresent(0, "ALC_ENUMERATE_ALL_EXT")
            ? al.AlcGetString(0, Al.ALC_DEFAULT_ALL_DEVICES_SPECIFIER)
            : al.IsAlcExtensionPresent(0, "ALC_ENUMERATION_EXT")
                ? al.AlcGetString(0, Al.ALC_DEFAULT_DEVICE_SPECIFIER)
                : null;

        return string.IsNullOrEmpty(name) ? null : new() { Id = name, Name = name };
    }

    /// <summary>Opens <paramref name="deviceId"/> (an OpenAL device name; null = OpenAL's default device).</summary>
    public IAudioSink CreateSink(string? deviceId, int sampleRate, int channels)
        => new OpenAlAudioSink(al, deviceId, sampleRate, channels);

    /// <summary>Whether the default device can be opened right now (opens and closes it).</summary>
    internal bool CanOpenDefaultDevice()
    {
        nint device = al.AlcOpenDevice(null);
        if (device == 0)
            return false;

        al.AlcCloseDevice(device);
        return true;
    }
}
