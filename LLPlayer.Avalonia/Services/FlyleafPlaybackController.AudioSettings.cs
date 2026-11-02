using FlyleafLib;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController : IAudioSettingsController
{
    readonly object audioSettingsGate = new();
    readonly CancellationTokenSource audioRecoveryCancellation = new();
    Task audioRecoveryLoop = Task.CompletedTask;
    Task? audioRefresh;
    bool audioRecoveryStopped;
    string selectedAudioDeviceId = "";
    string? physicalAudioDefault;
    AudioRuntimeSnapshot? audioSettingsSnapshot;

    public AudioRuntimeSnapshot AudioSettings => Volatile.Read(ref audioSettingsSnapshot) ?? CaptureAudioSettings();

    void InitializeAudioSettings()
    {
        physicalAudioDefault = Engine.Audio.CurrentDevice.Id;
        PublishAudioSettings();
        audioRecoveryLoop = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), audioRecoveryCancellation.Token).ConfigureAwait(false);
                    if (player.Config.Audio.Enabled)
                        try { await RefreshAudioDevicesAsync().ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                        catch (Exception) { /* A failed enumeration remains retryable on the next bounded refresh. */ }
                }
            }
            catch (OperationCanceledException) when (audioRecoveryCancellation.IsCancellationRequested) { }
        });
    }

    public Task ApplyAudioSettingsAsync(AudioRuntimeOptions options, bool applyDefaultVolume = false)
    {
        AudioRuntimeOptions detached = options with { Languages = options.Languages.ToArray() };
        return MutateWithTranslationAsync(token =>
        {
            CheckAudioAdmission(token);
            ApplyAudioSettingsCore(detached, applyDefaultVolume);
        });
    }

    void ApplyAudioSettingsCore(AudioRuntimeOptions detached, bool applyDefaultVolume)
    {
        var config = player.Config.Audio;
        config.Languages = detached.Languages.Select(Language.Get).ToList();
        config.VolumeMax = detached.VolumeMax;
        config.VolumeDefault = detached.VolumeDefault;
        config.FiltersEnabled = detached.FiltersEnabled;
        config.Enabled = detached.Enabled;
        if (applyDefaultVolume) player.Audio.Volume = Math.Min(detached.VolumeDefault, detached.VolumeMax);
        else if (player.Audio.Volume > detached.VolumeMax)
        {
            bool muted = player.Audio.Mute;
            player.Audio.Volume = detached.VolumeMax;
            player.Audio.Mute = muted;
        }
        ApplyAudioDeviceSelection();
        PublishAudioSettings();
        Raise(nameof(VolumeMax)); Raise(nameof(Volume)); Raise(nameof(Mute));
    }

    public Task RefreshAudioDevicesAsync()
    {
        lock (audioSettingsGate)
        {
            if (audioRecoveryStopped || disposed) return Task.CompletedTask;
            if (audioRefresh is { IsCompleted: false }) return audioRefresh;
            return audioRefresh = Recognition.MutateAsync(token =>
            {
                CheckAudioAdmission(token);
                Engine.Audio.RefreshDevices(updatePlayers: false);
                CheckAudioAdmission(token);
                ApplyAudioDeviceSelection();
                PublishAudioSettings();
            });
        }
    }

    public Task SelectAudioDeviceAsync(string? deviceId)
    {
        string selected = deviceId ?? "";
        return Recognition.MutateAsync(token =>
        {
            CheckAudioAdmission(token);
            selectedAudioDeviceId = selected;
            ApplyAudioDeviceSelection();
            PublishAudioSettings();
        });
    }

    public Task SetAudioDelayAsync(long ticks) => MutateWithTranslationAsync(token =>
    {
        CheckAudioAdmission(token);
        player.Config.Audio.Delay = ticks;
        PublishAudioSettings();
    });

    void ApplyAudioDeviceSelection()
    {
        if (!player.Config.Audio.Enabled) return;
        string? nextDefault = Engine.Audio.CurrentDevice.Id;
        bool defaultChanged = physicalAudioDefault != nextDefault;
        physicalAudioDefault = nextDefault;
        // The requested explicit target survives removal; only the actual sink temporarily follows default.
        var target = bridge.Snapshot(Engine.Audio.Devices)
            .FirstOrDefault(device => selectedAudioDeviceId.Length != 0 && device.Id == selectedAudioDeviceId)
            ?? Engine.Audio.DefaultDevice;
        if (player.Audio.Device.Id != target.Id)
            player.Audio.Device = target;
        else if (player.Audio.OutputRecoveryRequired || (target == Engine.Audio.DefaultDevice && defaultChanged))
            player.Audio.RecoverOutput(force: target == Engine.Audio.DefaultDevice && defaultChanged);
    }

    AudioRuntimeSnapshot CaptureAudioSettings()
    {
        var config = player.Config.Audio;
        var options = new AudioRuntimeOptions(config.Enabled, config.FiltersEnabled, config.VolumeMax,
            config.VolumeDefault, config.Languages.Select(language => language.CultureName ?? language.ISO6391).ToArray());
        AudioDeviceInfo[] devices = bridge.Snapshot(Engine.Audio.Devices)
            .Select(device => new AudioDeviceInfo(device.Id == Engine.Audio.DefaultDevice.Id ? "" : device.Id, device.Name)).ToArray();
        string? defaultDevice = Engine.Audio.CurrentDevice.Id == Engine.Audio.DefaultDevice.Id ? null : Engine.Audio.CurrentDevice.Id;
        string? output = !config.Enabled || !player.Audio.IsOpened || player.Audio.OutputRecoveryRequired || AudioEngine.Backend.Name == "Null" ? null
            : player.Audio.Device == Engine.Audio.DefaultDevice ? defaultDevice : player.Audio.Device.Id;
        return new(options, config.Delay, selectedAudioDeviceId, defaultDevice, output, devices,
            config.Enabled && player.Audio.OutputRecoveryRequired);
    }

    void PublishAudioSettings()
    {
        if (disposed || audioRecoveryCancellation.IsCancellationRequested) return;
        Volatile.Write(ref audioSettingsSnapshot, CaptureAudioSettings());
        Raise(nameof(AudioSettings));
    }

    void CheckAudioAdmission(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        audioRecoveryCancellation.Token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed || player.IsDisposed, this);
    }

    void CancelAudioRecovery()
    {
        lock (audioSettingsGate)
        {
            if (audioRecoveryStopped) return;
            audioRecoveryStopped = true;
            audioRecoveryCancellation.Cancel();
        }
    }

    public async Task StopAudioRecoveryAsync()
    {
        Task loop, refresh;
        lock (audioSettingsGate)
        {
            CancelAudioRecovery();
            loop = audioRecoveryLoop;
            refresh = audioRefresh ?? Task.CompletedTask;
        }
        try { await Task.WhenAll(loop, refresh).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
