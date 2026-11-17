using FlyleafLib;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Core.Automation;

public sealed partial class ControlSettingsService
{
    readonly SettingsFamily<PdicSettings> pdic;
#if !WINDOWS
    readonly SettingsFamily<OnlineVideoSettings> onlineVideo;
    readonly SettingsFamily<PlaybackSettings> playback;
    readonly PlaybackSettingsStore playbackStore;

    public OnlineVideoSettings CaptureOnlineVideo() { lock (gate) return onlineVideo.Load().Settings; }
    public PlaybackSettings CapturePlayback() { lock (gate) return playback.Load().Settings; }

    void SavePlayback(PlaybackSettings candidate, PrefsStore prefsStore)
    {
        var previous = playbackStore.Load();
        var preferences = prefsStore.Load(preserveCorruptCopy: false);
        bool newDefault = candidate.AudioVolumeDefault != previous.AudioVolumeDefault;
        int volume = newDefault ? candidate.AudioVolumeDefault : Math.Min(preferences.Volume, candidate.AudioVolumeMax);
        bool mute = newDefault ? volume == 0 : preferences.Mute;
        Action? remember = newDefault || volume != preferences.Volume
            ? () => prefsStore.SaveRememberedVolume(volume, mute) : null;
        playbackStore.Save(candidate, remember);
    }

    public void ApplyPlaybackStartupTo(EngineConfig engineConfig)
    {
        lock (gate)
        {
            var saved = playback.Load().Settings;
            if (playbackStore.HasSavedSettings) saved.ApplyStartupTo(engineConfig);
            saved.LogOutput = engineConfig.LogOutput;
            saved.LogLevel = engineConfig.LogLevel;
            saved.FFmpegLogLevel = engineConfig.FFmpegLogLevel;
            saved.LogCachedLines = engineConfig.LogCachedLines;
            saved.FFmpegLoadProfile = engineConfig.FFmpegLoadProfile;
            playback.SetStartup(saved);
        }
    }

    public Config CaptureEngineConfig() => CaptureWorkflowSettings().Config;

    public (Config Config, string RecognitionIdentity, string TranslationIdentity, bool TranslationEnabled,
        string DubbingIdentity) CaptureWorkflowSettings()
    {
        lock (gate)
        {
            var recognitionSettings = recognition.Load().Settings;
            var playbackSettings = playback.Load().Settings;
            bool playbackSaved = playbackStore.HasSavedSettings;
            var translationSettings = translation.Load().Settings;
            var dubbingSettings = dubbing.Load().Settings;
            var config = new Config(true);
            recognitionSettings.Apply(config.Subtitles);
            if (playbackSaved) playbackSettings.ApplyTo(config);
            translationSettings.Apply(config.Subtitles);
            dubbingSettings.Apply(config.Subtitles);
            string recognitionIdentity = CombineIdentities([playback.GetIdentity(playbackSettings),
                recognition.GetIdentity(recognitionSettings), [playbackSaved ? (byte)1 : (byte)0]]);
            string translationIdentity = CombineIdentities([translation.GetIdentity(translationSettings)]);
            string dubbingIdentity = CombineIdentities([dubbing.GetIdentity(dubbingSettings)]);
            return (config, recognitionIdentity, translationIdentity, translationSettings.SentenceProvider != "Off", dubbingIdentity);
        }
    }

    static SettingsFields<PlaybackSettings> PlaybackFields()
    {
        var fields = new SettingsFields<PlaybackSettings>();
        fields.Add("audioEnabled", s => s.AudioEnabled, (s, value) => s.AudioEnabled = value);
        fields.Add("audioFiltersEnabled", s => s.AudioFiltersEnabled, (s, value) => s.AudioFiltersEnabled = value);
        fields.Add("audioVolumeMax", s => s.AudioVolumeMax, (s, value) => s.AudioVolumeMax = value);
        fields.Add("audioVolumeDefault", s => s.AudioVolumeDefault, (s, value) => s.AudioVolumeDefault = value);
        fields.Add("audioLanguages", s => s.AudioLanguages, (s, value) => s.AudioLanguages = value);
        fields.Add("aspectRatio", s => s.AspectRatio, (s, value) => s.AspectRatio = value);
        fields.Add("aspectRatioCustom", s => s.AspectRatioCustom, (s, value) => s.AspectRatioCustom = value);
        fields.Add("deInterlace", s => s.DeInterlace, (s, value) => s.DeInterlace = value);
        fields.Add("doubleRate", s => s.DoubleRate, (s, value) => s.DoubleRate = value);
        fields.Add("allowProfileMismatch", s => s.AllowProfileMismatch, (s, value) => s.AllowProfileMismatch = value);
        fields.Add("videoHardwareAccelerationEnabled", s => s.VideoHardwareAccelerationEnabled, (s, value) => s.VideoHardwareAccelerationEnabled = value);
        fields.Add("videoHardwareDecodeMode", s => s.VideoHardwareDecodeMode, (s, value) => s.VideoHardwareDecodeMode = value);
        fields.Add("videoHardwareDevice", s => s.VideoHardwareDevice, (s, value) => s.VideoHardwareDevice = value);
        fields.Add("videoPresentationMode", s => s.VideoPresentationMode, (s, value) => s.VideoPresentationMode = value);
        fields.Add("videoThreads", s => s.VideoThreads, (s, value) => s.VideoThreads = value);
        fields.Add("zoomOffset", s => s.ZoomOffset, (s, value) => s.ZoomOffset = value);
        fields.Add("maxLatencyMs", s => s.MaxLatencyMs, (s, value) => s.MaxLatencyMs = value);
        fields.Add("zeroLatency", s => s.ZeroLatency, (s, value) => s.ZeroLatency = value);
        fields.Add("hdRtoSDRMethod", s => s.HDRtoSDRMethod, (s, value) => s.HDRtoSDRMethod = value);
        fields.Add("sdrDisplayNits", s => s.SDRDisplayNits, (s, value) => s.SDRDisplayNits = value);
        fields.Add("brightness", s => s.Brightness, (s, value) => s.Brightness = value);
        fields.Add("contrast", s => s.Contrast, (s, value) => s.Contrast = value);
        fields.Add("hue", s => s.Hue, (s, value) => s.Hue = value);
        fields.Add("saturation", s => s.Saturation, (s, value) => s.Saturation = value);
        fields.Add("minBufferDurationMs", s => s.MinBufferDurationMs, (s, value) => s.MinBufferDurationMs = value);
        fields.Add("bufferDurationMs", s => s.BufferDurationMs, (s, value) => s.BufferDurationMs = value);
        fields.Add("maxAudioFrames", s => s.MaxAudioFrames, (s, value) => s.MaxAudioFrames = value);
        fields.Add("maxVideoFrames", s => s.MaxVideoFrames, (s, value) => s.MaxVideoFrames = value);
        fields.Add("maxVerticalResolution", s => s.MaxVerticalResolution, (s, value) => s.MaxVerticalResolution = value);
        fields.Add("openTimeoutMs", s => s.OpenTimeoutMs, (s, value) => s.OpenTimeoutMs = value);
        fields.Add("readTimeoutMs", s => s.ReadTimeoutMs, (s, value) => s.ReadTimeoutMs = value);
        fields.Add("readLiveTimeoutMs", s => s.ReadLiveTimeoutMs, (s, value) => s.ReadLiveTimeoutMs = value);
        fields.Add("seekTimeoutMs", s => s.SeekTimeoutMs, (s, value) => s.SeekTimeoutMs = value);
        fields.Add("seekAccurate", s => s.SeekAccurate, (s, value) => s.SeekAccurate = value);
        fields.Add("seekAccurateFixMarginMs", s => s.SeekAccurateFixMarginMs, (s, value) => s.SeekAccurateFixMarginMs = value);
        fields.Add("subtitlesEnabled", s => s.SubtitlesEnabled, (s, value) => s.SubtitlesEnabled = value);
        fields.Add("searchLocal", s => s.SearchLocal, (s, value) => s.SearchLocal = value);
        fields.Add("searchLocalPaths", s => s.SearchLocalPaths, (s, value) => s.SearchLocalPaths = value);
        fields.Add("enabledCached", s => s.EnabledCached, (s, value) => s.EnabledCached = value);
        fields.Add("openAutomaticSubs", s => s.OpenAutomaticSubs, (s, value) => s.OpenAutomaticSubs = value);
        fields.Add("subtitleLanguages", s => s.SubtitleLanguages, (s, value) => s.SubtitleLanguages = value);
        fields.Add("languageAutoDetect", s => s.LanguageAutoDetect, (s, value) => s.LanguageAutoDetect = value);
        fields.Add("languageFallbackPrimary", s => s.LanguageFallbackPrimary, (s, value) => s.LanguageFallbackPrimary = value, allowsNull: true);
        fields.Add("languageFallbackSecondary", s => s.LanguageFallbackSecondary, (s, value) => s.LanguageFallbackSecondary = value, allowsNull: true);
        fields.Add("languageFallbackSecondarySame", s => s.LanguageFallbackSecondarySame, (s, value) => s.LanguageFallbackSecondarySame = value);
        fields.Add("subtitleExportUtf8Bom", s => s.SubtitleExportUtf8Bom, (s, value) => s.SubtitleExportUtf8Bom = value);
        fields.Add("resegmentSubtitles", s => s.ResegmentSubtitles, (s, value) => s.ResegmentSubtitles = value);
        fields.Add("wordTimestamps", s => s.WordTimestamps, (s, value) => s.WordTimestamps = value);
        fields.Add("vadCueSnapping", s => s.VadCueSnapping, (s, value) => s.VadCueSnapping = value);
        fields.Add("fixAllCaps", s => s.FixAllCaps, (s, value) => s.FixAllCaps = value);
        fields.Add("asrDenoise", s => s.ASRDenoise, (s, value) => s.ASRDenoise = value);
        fields.Add("asrPerSegmentLanguage", s => s.ASRPerSegmentLanguage, (s, value) => s.ASRPerSegmentLanguage = value);
        fields.Add("subtitleMaxLinesPerCue", s => s.SubtitleMaxLinesPerCue, (s, value) => s.SubtitleMaxLinesPerCue = value);
        fields.Add("subtitleMaxCharsPerLine", s => s.SubtitleMaxCharsPerLine, (s, value) => s.SubtitleMaxCharsPerLine = value);
        fields.Add("subtitleMaxCjkCharsPerLine", s => s.SubtitleMaxCjkCharsPerLine, (s, value) => s.SubtitleMaxCjkCharsPerLine = value);
        fields.Add("subtitleMaxCueDurationSec", s => s.SubtitleMaxCueDurationSec, (s, value) => s.SubtitleMaxCueDurationSec = value);
        fields.Add("subtitleMinCueDurationSec", s => s.SubtitleMinCueDurationSec, (s, value) => s.SubtitleMinCueDurationSec = value);
        fields.Add("logOutput", s => s.LogOutput, (s, value) => s.LogOutput = value, savedForNextStart: true);
        fields.Add("logLevel", s => s.LogLevel, (s, value) => s.LogLevel = value, savedForNextStart: true);
        fields.Add("fFmpegLogLevel", s => s.FFmpegLogLevel, (s, value) => s.FFmpegLogLevel = value, savedForNextStart: true);
        fields.Add("logCachedLines", s => s.LogCachedLines, (s, value) => s.LogCachedLines = value, savedForNextStart: true);
        fields.Add("fFmpegLoadProfile", s => s.FFmpegLoadProfile, (s, value) => s.FFmpegLoadProfile = value, savedForNextStart: true);
        return fields;
    }

    static SettingsFields<OnlineVideoSettings> OnlineVideoFields()
    {
        var fields = new SettingsFields<OnlineVideoSettings>();
        fields.Add("ytDlpPath", s => s.YtDlpPath, (s, value) => s.YtDlpPath = value);
        fields.Add("denoPath", s => s.DenoPath, (s, value) => s.DenoPath = value);
        fields.Add("maxVideoHeight", s => s.MaxVideoHeight, (s, value) => s.MaxVideoHeight = value);
        fields.Add("preferVideoWithAudio", s => s.PreferVideoWithAudio, (s, value) => s.PreferVideoWithAudio = value);
        // The existing option list can contain passwords, cookie paths, and authentication headers.
        fields.AddCredential("extraArguments", s => s.ExtraArguments, (s, value) => s.ExtraArguments = value ?? "");
        return fields;
    }
#endif

    public PdicSettings CapturePdic() { lock (gate) return pdic.Load().Settings; }

    static SettingsFields<PdicSettings> PdicFields()
    {
        var fields = new SettingsFields<PdicSettings>();
        fields.Add("executablePath", s => s.ExecutablePath, (s, value) => s.ExecutablePath = value);
        return fields;
    }
}
