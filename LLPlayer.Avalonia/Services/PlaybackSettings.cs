using FlyleafLib;

namespace LLPlayer.Avalonia.Services;

/// <summary>A detached session snapshot. It is never serialized by the playback preferences store.</summary>
public sealed record PlaybackRuntimeSettings
{
    /// <summary>Opaque media/session identity supplied by the controller; retained across detached edits.</summary>
    public object? SessionIdentity { get; init; }
    /// <summary>Original detached session values, supplied only with an editor Save for a fresh per-field merge.</summary>
    public PlaybackRuntimeSettings? Baseline { get; init; }
    public long AudioDelayTicks { get; init; }
    public string? AudioDevice { get; init; }
    public string? DefaultAudioDevice { get; init; }
    public IReadOnlyList<AudioDeviceInfo> AudioDevices { get; init; } = [];
    public bool AudioRecovering { get; init; }
    public string AudioBackendName { get; init; } = "";
    public string? AudioOutputDevice { get; init; }
    public bool AudioEnabled { get; init; } = true;
    public bool AudioStreamOpened { get; init; }
    public long PrimarySubtitleDelayTicks { get; init; }
    public long SecondarySubtitleDelayTicks { get; init; }
    public double ZoomPercent { get; init; } = 100;
    public double PanX { get; init; }
    public double PanY { get; init; }
    public uint Rotation { get; init; }
    public uint CropTop { get; init; }
    public uint CropLeft { get; init; }
    public uint CropBottom { get; init; }
    public uint CropRight { get; init; }
    public bool HFlip { get; init; }
    public bool VFlip { get; init; }
    public bool FiltersAvailable { get; init; } = true;
}

public interface IPlaybackSettingsController
{
    PlaybackSettings GetPlaybackSettings();
    PlaybackRuntimeSettings GetPlaybackRuntimeSettings();
    Task SavePlaybackSettingsAsync(PlaybackSettings edited, PlaybackSettings baseline, PlaybackRuntimeSettings runtime, bool defaultVolumeEdited);
    Task RefreshPlaybackDevicesAsync() => Task.CompletedTask;
}
