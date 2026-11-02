using FlyleafLib;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController : IPlaybackSettingsController
{
    sealed record PlaybackSettingsSession(int Epoch, long Generation, PlaylistItem? Item,
        PlaybackAudioDelaySource? Audio, SubtitleCueSource? PrimarySubtitles, SubtitleCueSource? SecondarySubtitles);
    sealed record PlaybackAudioDelaySource(FlyleafPlaybackController Owner, AudioStream Source,
        object? Demuxer, string? NativeUrl, ExternalStream? External, string? ExternalUrl, Stream? ExternalIO,
        ExternalAudioStream? SelectedExternal);
    PlaybackSettings? savedPlaybackSettings;
    public PlaybackSettingsStore? PlaybackStore { get; private set; }

    public void ConfigurePlaybackSettings(PlaybackSettingsStore store, PlaybackSettings settings)
    {
        PlaybackStore = store;
        savedPlaybackSettings = settings.Clone();
    }

    public PlaybackSettings GetPlaybackSettings()
    {
        PlaybackSettings current = PlaybackSettings.FromConfig(player.Config, Engine.Config,
            savedPlaybackSettings?.SubtitleExportUtf8Bom ?? true);
        // These values describe the next start, which may differ from the running engine.
        if (savedPlaybackSettings is { } saved)
        {
            current.LogOutput = saved.LogOutput;
            current.LogLevel = saved.LogLevel;
            current.FFmpegLogLevel = saved.FFmpegLogLevel;
            current.LogCachedLines = saved.LogCachedLines;
            current.FFmpegLoadProfile = saved.FFmpegLoadProfile;
            if (current.LanguageFallbackSecondarySame) current.LanguageFallbackSecondary = saved.LanguageFallbackSecondary;
            if (Engine.Config.FFmpegLoadProfile == Flyleaf.FFmpeg.LoadProfile.Main)
                current.AudioFiltersEnabled = saved.AudioFiltersEnabled;
        }
        return current;
    }

    public PlaybackRuntimeSettings GetPlaybackRuntimeSettings()
    {
        AudioRuntimeSnapshot audio = CaptureAudioSettings();
        var video = player.Config.Video;
        return new()
        {
            SessionIdentity = new PlaybackSettingsSession(Volatile.Read(ref mediaOpenEpoch), player.MediaGeneration,
                player.Playlist?.Selected, CapturePlaybackAudioDelaySource(), CaptureSubtitleCueSource(0), CaptureSubtitleCueSource(1)),
            AudioDelayTicks = player.Config.Audio.Delay,
            AudioDevice = string.IsNullOrEmpty(audio.SelectedDeviceId) ? null : audio.SelectedDeviceId,
            DefaultAudioDevice = audio.DefaultDeviceId,
            AudioDevices = audio.Devices,
            AudioRecovering = audio.Recovering,
            AudioBackendName = AudioEngine.Backend.Name,
            AudioOutputDevice = audio.OutputDeviceId,
            AudioEnabled = player.Config.Audio.Enabled,
            AudioStreamOpened = player.Audio.IsOpened,
            PrimarySubtitleDelayTicks = player.Config.Subtitles[0].Delay,
            SecondarySubtitleDelayTicks = player.Config.Subtitles[1].Delay,
            ZoomPercent = video.Zoom, PanX = video.PanXOffset, PanY = video.PanYOffset,
            Rotation = video.Rotation, HFlip = video.HFlip, VFlip = video.VFlip,
            CropTop = video.Crop.Top, CropLeft = video.Crop.Left,
            CropBottom = video.Crop.Bottom, CropRight = video.Crop.Right,
            FiltersAvailable = Engine.Config.FFmpegLoadProfile != Flyleaf.FFmpeg.LoadProfile.Main
        };
    }

    public Task RefreshPlaybackDevicesAsync() => Recognition.MutateIfIdleAsync(token =>
    {
        CheckPlaybackSettingsAdmission(token);
        Engine.Audio.RefreshDevices(updatePlayers: false);
        CheckPlaybackSettingsAdmission(token);
        ApplyAudioDeviceSelection();
        PublishAudioSettings();
    });

    public Task SavePlaybackSettingsAsync(PlaybackSettings edited, PlaybackSettings baseline,
        PlaybackRuntimeSettings runtime, bool defaultVolumeEdited)
        => SavePlaybackSettingsAndRememberAsync(edited, baseline, runtime, defaultVolumeEdited, null);

    public Task SavePlaybackSettingsAndRememberAsync(PlaybackSettings edited, PlaybackSettings baseline,
        PlaybackRuntimeSettings runtime, bool defaultVolumeEdited, Action<int, bool>? rememberVolume)
    {
        PlaybackSettings draft = edited.Clone(), original = baseline.Clone();
        return MutateWithTranslationAsync(token =>
        {
            CheckPlaybackSettingsAdmission(token);
            ValidatePlaybackRuntime(runtime);
            PlaybackSettings merged = draft.MergeEdits(original, GetPlaybackSettings());
            PlaybackRuntimeSettings? previous = runtime.Baseline;
            bool sameSession = (previous ?? runtime).SessionIdentity is PlaybackSettingsSession session
                && session.Epoch == Volatile.Read(ref mediaOpenEpoch) && session.Generation == player.MediaGeneration;
            // Persist a complete validated candidate before changing any live field.
            int currentVolume = player.Audio.Volume;
            int desiredVolume = defaultVolumeEdited ? merged.AudioVolumeDefault : Math.Min(currentVolume, merged.AudioVolumeMax);
            bool desiredMute = defaultVolumeEdited ? desiredVolume == 0 : player.Audio.Mute;
            Action? persistVolume = rememberVolume != null && (defaultVolumeEdited || desiredVolume != currentVolume)
                ? () => rememberVolume(desiredVolume, desiredMute) : null;
            (PlaybackStore ?? throw new InvalidOperationException("Playback settings storage is unavailable.")).Save(merged, persistVolume);
            savedPlaybackSettings = merged.Clone();
            PublishLanguageConfiguration(() => merged.ApplyTo(player.Config, includeAudio: false));
            if (previous != null && (runtime.AudioDevice ?? "") != (previous.AudioDevice ?? ""))
                selectedAudioDeviceId = runtime.AudioDevice ?? "";
            ApplyAudioSettingsCore(new(merged.AudioEnabled, merged.AudioFiltersEnabled,
                merged.AudioVolumeMax, merged.AudioVolumeDefault, merged.AudioLanguages), defaultVolumeEdited);
            if (sameSession && previous != null && previous.SessionIdentity is PlaybackSettingsSession target)
            {
                player.TryMutateMedia(target.Generation, target.Item,
                    () => target.Epoch == Volatile.Read(ref mediaOpenEpoch), () =>
                {
                if (runtime.AudioDelayTicks != previous.AudioDelayTicks && target.Audio is { } audioSource
                    && IsCurrentPlaybackAudioDelaySource(audioSource)) player.Config.Audio.Delay = runtime.AudioDelayTicks;
                if (runtime.PrimarySubtitleDelayTicks != previous.PrimarySubtitleDelayTicks && target.PrimarySubtitles is { } primarySource
                    && IsCurrentSubtitleCueSource(primarySource)) ApplyPlaybackSubtitleDelay(0, runtime.PrimarySubtitleDelayTicks);
                if (runtime.SecondarySubtitleDelayTicks != previous.SecondarySubtitleDelayTicks && target.SecondarySubtitles is { } secondarySource
                    && IsCurrentSubtitleCueSource(secondarySource)) ApplyPlaybackSubtitleDelay(1, runtime.SecondarySubtitleDelayTicks);
                var video = player.Config.Video;
                if (runtime.ZoomPercent != previous.ZoomPercent) video.Zoom = runtime.ZoomPercent;
                if (runtime.PanX != previous.PanX) video.PanXOffset = runtime.PanX;
                if (runtime.PanY != previous.PanY) video.PanYOffset = runtime.PanY;
                if (runtime.Rotation != previous.Rotation) video.Rotation = runtime.Rotation;
                if (runtime.HFlip != previous.HFlip) video.HFlip = runtime.HFlip;
                if (runtime.VFlip != previous.VFlip) video.VFlip = runtime.VFlip;
                // Crop components merge independently so an unrelated editor field cannot restore stale geometry.
                video.Crop = new(
                    runtime.CropTop != previous.CropTop ? runtime.CropTop : video.Crop.Top,
                    runtime.CropLeft != previous.CropLeft ? runtime.CropLeft : video.Crop.Left,
                    runtime.CropBottom != previous.CropBottom ? runtime.CropBottom : video.Crop.Bottom,
                    runtime.CropRight != previous.CropRight ? runtime.CropRight : video.Crop.Right);
                });
            }
            PublishAudioSettings();
        }, requireIdle: true);
    }

    PlaybackAudioDelaySource? CapturePlaybackAudioDelaySource()
    {
        AudioStream? stream = player.decoder.AudioStream;
        if (!player.Audio.IsOpened || stream == null) return null;
        ExternalStream? external = stream.ExternalStream;
        return new(this, stream, stream.Demuxer, stream.Demuxer?.Url, external, external?.Url,
            external?.IOStream, player.Playlist?.Selected?.ExternalAudioStream);
    }

    bool IsCurrentPlaybackAudioDelaySource(PlaybackAudioDelaySource source)
    {
        AudioStream? stream = player.decoder.AudioStream;
        return ReferenceEquals(source.Owner, this) && player.Audio.IsOpened && ReferenceEquals(source.Source, stream)
            && ReferenceEquals(source.Demuxer, stream?.Demuxer) && source.NativeUrl == stream?.Demuxer.Url
            && ReferenceEquals(source.External, stream?.ExternalStream) && source.ExternalUrl == stream?.ExternalStream?.Url
            && ReferenceEquals(source.ExternalIO, stream?.ExternalStream?.IOStream)
            && ReferenceEquals(source.SelectedExternal, player.Playlist?.Selected?.ExternalAudioStream);
    }

    void ApplyPlaybackSubtitleDelay(int slot, long ticks)
    {
        int previousSlot = SubtitlesSelectedHelper.CurIndex;
        try { SubtitlesSelectedHelper.CurIndex = slot; player.Config.Subtitles[slot].Delay = ticks; }
        finally { SubtitlesSelectedHelper.CurIndex = previousSlot; }
    }

    void CheckPlaybackSettingsAdmission(CancellationToken token)
    {
        CheckAudioAdmission(token);
        if (player.Status == FlyleafLib.MediaPlayer.Status.Opening)
            throw new InvalidOperationException("Wait for media opening to finish before changing playback settings.");
    }

    internal static void ValidatePlaybackRuntime(PlaybackRuntimeSettings runtime)
    {
        if (!double.IsFinite(runtime.ZoomPercent) || runtime.ZoomPercent < 0
            || !double.IsFinite(runtime.PanX) || runtime.PanX is < -10 or > 10
            || !double.IsFinite(runtime.PanY) || runtime.PanY is < -10 or > 10
            || runtime.Rotation is not (0 or 90 or 180 or 270)
            || (ulong)runtime.CropLeft + runtime.CropRight > int.MaxValue
            || (ulong)runtime.CropTop + runtime.CropBottom > int.MaxValue)
            throw new ArgumentException("Viewport values are outside the software renderer's supported range.");
    }
}
