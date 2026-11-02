namespace LLPlayer.Avalonia.Services;

public sealed record AudioRuntimeOptions(bool Enabled, bool FiltersEnabled, int VolumeMax, int VolumeDefault,
    IReadOnlyList<string> Languages);
public sealed record AudioDeviceInfo(string Id, string Name);
public sealed record AudioRuntimeSnapshot(AudioRuntimeOptions Options, long DelayTicks, string SelectedDeviceId,
    string? DefaultDeviceId, string? OutputDeviceId, IReadOnlyList<AudioDeviceInfo> Devices, bool Recovering);

/// <summary>Detached audio settings and session-only output selection/delay, owned by the player's command queue.</summary>
public interface IAudioSettingsController
{
    AudioRuntimeSnapshot AudioSettings { get; }
    Task ApplyAudioSettingsAsync(AudioRuntimeOptions options, bool applyDefaultVolume = false);
    Task RefreshAudioDevicesAsync();
    Task SelectAudioDeviceAsync(string? deviceId);
    Task SetAudioDelayAsync(long ticks);
    Task StopAudioRecoveryAsync();
}
