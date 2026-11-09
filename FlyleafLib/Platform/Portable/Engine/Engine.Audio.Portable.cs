using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

using FlyleafLib.MediaFramework.MediaDevice;

namespace FlyleafLib;

/// <summary>
/// F-13 portable (Linux) audio engine. Same public surface as the Windows (MMDevice/XAudio2) AudioEngine in
/// Engine/Engine.Audio.cs; devices come from the pluggable <see cref="Backend"/>.
/// </summary>
public class AudioEngine : INotifyPropertyChanged
{
    /// <summary>
    /// Audio output backend used by every player. Must be set before <see cref="Engine.Start"/>;
    /// defaults to <see cref="NullAudioBackend"/> (no sound device, real-time consumption for A/V sync).
    /// <para>
    /// Hosts that want sound set it from <see cref="AudioBackendFactory.CreateDefault(Action{string})"/>
    /// (<see cref="OpenAlAudioBackend"/> when OpenAL Soft and an output device are available, Null otherwise;
    /// <c>LLPLAYER_AUDIO_BACKEND=null|openal|auto</c> overrides the choice):
    /// <code>
    /// AudioEngine.Backend = AudioBackendFactory.CreateDefault();
    /// Engine.Start(engineConfig);
    /// </code>
    /// </para>
    /// </summary>
    public static IAudioBackend Backend { get; set; } = NullAudioBackend.Instance;

    #region Properties (Public)

    public AudioEndpoint DefaultDevice      { get; private set; } = new() { Id = "0", Name = "Default" };
    public AudioEndpoint CurrentDevice      { get; private set; } = new();

    /// <summary>
    /// Whether no audio devices were found or audio failed to initialize
    /// </summary>
    public bool         Failed              { get; private set; }

    /// <summary>
    /// Compatibility list of audio capture inputs (separate from playback endpoints).
    /// </summary>
    public ObservableCollection<AudioDevice>
                        CapDevices          { get; set; } = [];

    /// <summary>
    /// List of Audio Devices (the first entry is always <see cref="DefaultDevice"/>)
    /// </summary>
    public ObservableCollection<AudioEndpoint>
                        Devices             { get; private set; } = [];

    private readonly object lockDevices = new();
    private readonly object lockCapDevices = new();
    private readonly object lockCaptureRefresh = new();
    private readonly CaptureDeviceCatalog captureCatalog = new(new FFmpegCaptureDeviceBackend());
    private CaptureDeviceSnapshot captureSnapshot = CaptureDeviceSnapshot.Initial;

    public CaptureDeviceSnapshot CaptureSnapshot => Volatile.Read(ref captureSnapshot);
    #endregion

    public event PropertyChangedEventHandler PropertyChanged;

    public AudioEngine() // We consider from UI here
    {
        if (Engine.Config.DisableAudio)
        {
            Failed = true;
            return;
        }

        BindingOperations.EnableCollectionSynchronization(Devices, lockDevices);
        BindingOperations.EnableCollectionSynchronization(CapDevices, lockCapDevices);
        RefreshDevices();
    }

    /// <summary>
    /// Explicitly enumerates real audio inputs without changing startup profile or playback endpoints.
    /// </summary>
    public void RefreshCapDevices() => RefreshCapDevices(CancellationToken.None);

    /// <summary>Cancellation is observed before/after native calls; a running driver call must finish.</summary>
    public void RefreshCapDevices(CancellationToken cancellationToken)
    {
        lock (lockCaptureRefresh)
        {
            var result = captureCatalog.Refresh(CaptureDeviceKind.Audio,
                Engine.IsLoaded && Engine.FFmpeg?.LoadedProfile == LoadProfile.All,
                OperatingSystem.IsWindows() ? ["dshow"] : OperatingSystem.IsLinux() ? ["pulse", "alsa"] : [], cancellationToken);
            lock (lockCapDevices)
            {
                CapDevices.Clear();
                foreach (var device in result.Devices)
                    CapDevices.Add(new(device.Label, device.Id, device.Url));
                Volatile.Write(ref captureSnapshot, result);
            }
        }
    }

    /// <summary>
    /// Re-enumerates the playback devices of <see cref="Backend"/> (hosts call this on device hot-plug).
    /// Players whose device disappeared fall back to <see cref="DefaultDevice"/>, unless the host passes false
    /// and applies its retained output selection through its own serialized command owner.
    /// </summary>
    public void RefreshDevices(bool updatePlayers = true)
    {
        var backend = Backend ?? NullAudioBackend.Instance;
        List<AudioEndpoint> removed = [];

        try
        {
            var devices = backend.EnumerateDevices();

            lock (lockDevices)
            {
                foreach (var device in Devices)
                {
                    if (device.Id == DefaultDevice.Id)
                        continue;

                    bool exists = false;
                    foreach (var cur in devices)
                        if (cur.Id == device.Id)
                            { exists = true; break; }

                    if (!exists)
                        removed.Add(device);
                }

                foreach (var device in removed)
                    Devices.Remove(device);

                if (!Devices.Contains(DefaultDevice))
                    Devices.Insert(0, DefaultDevice);

                foreach (var cur in devices)
                {
                    bool exists = false;
                    foreach (var device in Devices)
                        if (cur.Id == device.Id)
                            { exists = true; break; }

                    if (!exists)
                        Devices.Add(cur);
                }
            }

            var defaultDevice = backend.GetDefaultDevice();
            string id   = defaultDevice?.Id   ?? DefaultDevice.Id;
            string name = defaultDevice?.Name ?? DefaultDevice.Name;
            if (CurrentDevice.Id != id || CurrentDevice.Name != name)
            {
                CurrentDevice.Id    = id;
                CurrentDevice.Name  = name;
                PropertyChanged?.Invoke(this, new(nameof(CurrentDevice)));
            }

            if (CanInfo)
            {
                string dump = "";
                lock (lockDevices)
                    foreach (var device in Devices)
                        dump += $"{device.Id} | {device.Name} {(CurrentDevice.Id == device.Id ? "*" : "")}\r\n";
                Engine.Log?.Info($"Audio Devices ({backend.Name})\r\n{dump}");
            }
        }
        catch (Exception e)
        {
            Engine.Log?.Error($"Audio device enumeration failed ({backend.Name}: {e.Message})");
            lock (lockDevices)
                if (!Devices.Contains(DefaultDevice))
                    Devices.Add(DefaultDevice);
        }

        // A host with its own serialized command owner passes false and applies its retained selection there.
        // Do not dispatch unowned work that can outlive player disposal or a new media generation.
        if (updatePlayers && removed.Count > 0)
            foreach (var player in Engine.Players.ToArray())
                if (removed.Any(device => player.Audio.Device.Id == device.Id))
                    player.Audio.Device = DefaultDevice;
    }

    public class AudioEndpoint
    {
        public string Id    { get; set; }
        public string Name  { get; set; }

        public override string ToString()
            => Name;
    }
}
