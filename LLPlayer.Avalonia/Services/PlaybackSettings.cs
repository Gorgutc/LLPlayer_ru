using System.Text.Json;
using System.Text.Json.Serialization;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace LLPlayer.Avalonia.Services;

/// <summary>Portable playback policy only. Device, delay and viewport edits belong to the current session.</summary>
public sealed class PlaybackSettings
{
    public const long MaxMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;
    public bool AudioEnabled { get; set; } = true;
    public bool AudioFiltersEnabled { get; set; } = true;
    public int AudioVolumeMax { get; set; } = 150;
    public int AudioVolumeDefault { get; set; } = 75;
    public List<string> AudioLanguages { get; set; } = DefaultLanguages();
    public string AspectRatio { get; set; } = "Keep";
    public string AspectRatioCustom { get; set; } = "16:9";
    public DeInterlace DeInterlace { get; set; } = DeInterlace.Auto;
    public bool DoubleRate { get; set; } = true;
    public bool AllowProfileMismatch { get; set; } = false;
    public bool VideoHardwareAccelerationEnabled { get; set; } = false;
    public PortableVideoDecodeMode VideoHardwareDecodeMode { get; set; } = PortableVideoDecodeMode.Automatic;
    public string VideoHardwareDevice { get; set; } = "";
    public PortableVideoPresentationMode VideoPresentationMode { get; set; } = PortableVideoPresentationMode.Software;
    public int VideoThreads { get; set; } = Environment.ProcessorCount;
    public int ZoomOffset { get; set; } = 10;
    public long MaxLatencyMs { get; set; } = 0;
    public bool ZeroLatency { get; set; } = false;
    public HDRtoSDRMethod HDRtoSDRMethod { get; set; } = HDRtoSDRMethod.Hable;
    public float SDRDisplayNits { get; set; } = 0;
    public int Brightness { get; set; } = 0;
    public int Contrast { get; set; } = 0;
    public int Hue { get; set; } = 0;
    public int Saturation { get; set; } = 0;
    public long MinBufferDurationMs { get; set; } = 500;
    public long BufferDurationMs { get; set; } = 30000;
    public int MaxAudioFrames { get; set; } = 5;
    public int MaxVideoFrames { get; set; } = 4;
    public int MaxVerticalResolution { get; set; } = 0;
    public long OpenTimeoutMs { get; set; } = 300000;
    public long ReadTimeoutMs { get; set; } = 10000;
    public long ReadLiveTimeoutMs { get; set; } = 20000;
    public long SeekTimeoutMs { get; set; } = 8000;
    public bool SeekAccurate { get; set; } = false;
    public long SeekAccurateFixMarginMs { get; set; } = 0;
    public bool SubtitlesEnabled { get; set; } = true;
    public bool SearchLocal { get; set; } = true;
    public string SearchLocalPaths { get; set; } = Config.SubtitlesConfig.DefaultSearchLocalPaths;
    public bool EnabledCached { get; set; } = true;
    public bool OpenAutomaticSubs { get; set; } = false;
    public List<string> SubtitleLanguages { get; set; } = DefaultLanguages();
    public bool LanguageAutoDetect { get; set; } = true;
    public string? LanguageFallbackPrimary { get; set; } = null;
    public string? LanguageFallbackSecondary { get; set; } = null;
    public bool LanguageFallbackSecondarySame { get; set; } = true;
    public bool SubtitleExportUtf8Bom { get; set; } = true;
    public bool ResegmentSubtitles { get; set; } = true;
    public bool WordTimestamps { get; set; } = true;
    public bool VadCueSnapping { get; set; } = true;
    public bool FixAllCaps { get; set; } = true;
    public bool ASRDenoise { get; set; } = false;
    public bool ASRPerSegmentLanguage { get; set; } = false;
    public int SubtitleMaxLinesPerCue { get; set; } = 3;
    public int SubtitleMaxCharsPerLine { get; set; } = 48;
    public int SubtitleMaxCjkCharsPerLine { get; set; } = 24;
    public double SubtitleMaxCueDurationSec { get; set; } = 7;
    public double SubtitleMinCueDurationSec { get; set; } = 1;
    public string LogOutput { get; set; } = "";
    public FlyleafLib.LogLevel LogLevel { get; set; } = FlyleafLib.LogLevel.Warn;
    public Flyleaf.FFmpeg.LogLevel FFmpegLogLevel { get; set; } = Flyleaf.FFmpeg.LogLevel.Warn;
    public int LogCachedLines { get; set; } = 20;
    public Flyleaf.FFmpeg.LoadProfile FFmpegLoadProfile { get; set; } = Flyleaf.FFmpeg.LoadProfile.Filters;

    static List<string> DefaultLanguages() => Utils.GetSystemLanguages().Select(language => language.CultureName).ToList();
    public PlaybackSettings Clone() => JsonSerializer.Deserialize<PlaybackSettings>(
        JsonSerializer.Serialize(this, PlaybackSettingsStore.JsonOptions), PlaybackSettingsStore.JsonOptions)!;

    /// <summary>Only actual draft edits replace fresh live settings; current transport and external edits survive.</summary>
    public PlaybackSettings MergeEdits(PlaybackSettings baseline, PlaybackSettings current)
    {
        PlaybackSettings result = current.Clone();
        if (AudioEnabled != baseline.AudioEnabled) result.AudioEnabled = AudioEnabled;
        if (AudioFiltersEnabled != baseline.AudioFiltersEnabled) result.AudioFiltersEnabled = AudioFiltersEnabled;
        if (AudioVolumeMax != baseline.AudioVolumeMax) result.AudioVolumeMax = AudioVolumeMax;
        if (AudioVolumeDefault != baseline.AudioVolumeDefault) result.AudioVolumeDefault = AudioVolumeDefault;
        if (!AudioLanguages.SequenceEqual(baseline.AudioLanguages, StringComparer.Ordinal)) result.AudioLanguages = [.. AudioLanguages];
        if (AspectRatio != baseline.AspectRatio) result.AspectRatio = AspectRatio;
        if (AspectRatioCustom != baseline.AspectRatioCustom) result.AspectRatioCustom = AspectRatioCustom;
        if (DeInterlace != baseline.DeInterlace) result.DeInterlace = DeInterlace;
        if (DoubleRate != baseline.DoubleRate) result.DoubleRate = DoubleRate;
        if (AllowProfileMismatch != baseline.AllowProfileMismatch) result.AllowProfileMismatch = AllowProfileMismatch;
        if (VideoHardwareAccelerationEnabled != baseline.VideoHardwareAccelerationEnabled) result.VideoHardwareAccelerationEnabled = VideoHardwareAccelerationEnabled;
        if (VideoHardwareDecodeMode != baseline.VideoHardwareDecodeMode) result.VideoHardwareDecodeMode = VideoHardwareDecodeMode;
        if (VideoHardwareDevice != baseline.VideoHardwareDevice) result.VideoHardwareDevice = VideoHardwareDevice;
        if (VideoPresentationMode != baseline.VideoPresentationMode) result.VideoPresentationMode = VideoPresentationMode;
        if (VideoThreads != baseline.VideoThreads) result.VideoThreads = VideoThreads;
        if (ZoomOffset != baseline.ZoomOffset) result.ZoomOffset = ZoomOffset;
        if (MaxLatencyMs != baseline.MaxLatencyMs) result.MaxLatencyMs = MaxLatencyMs;
        if (ZeroLatency != baseline.ZeroLatency) result.ZeroLatency = ZeroLatency;
        if (HDRtoSDRMethod != baseline.HDRtoSDRMethod) result.HDRtoSDRMethod = HDRtoSDRMethod;
        if (SDRDisplayNits != baseline.SDRDisplayNits) result.SDRDisplayNits = SDRDisplayNits;
        if (Brightness != baseline.Brightness) result.Brightness = Brightness;
        if (Contrast != baseline.Contrast) result.Contrast = Contrast;
        if (Hue != baseline.Hue) result.Hue = Hue;
        if (Saturation != baseline.Saturation) result.Saturation = Saturation;
        if (MinBufferDurationMs != baseline.MinBufferDurationMs) result.MinBufferDurationMs = MinBufferDurationMs;
        if (BufferDurationMs != baseline.BufferDurationMs) result.BufferDurationMs = BufferDurationMs;
        if (MaxAudioFrames != baseline.MaxAudioFrames) result.MaxAudioFrames = MaxAudioFrames;
        if (MaxVideoFrames != baseline.MaxVideoFrames) result.MaxVideoFrames = MaxVideoFrames;
        if (MaxVerticalResolution != baseline.MaxVerticalResolution) result.MaxVerticalResolution = MaxVerticalResolution;
        if (OpenTimeoutMs != baseline.OpenTimeoutMs) result.OpenTimeoutMs = OpenTimeoutMs;
        if (ReadTimeoutMs != baseline.ReadTimeoutMs) result.ReadTimeoutMs = ReadTimeoutMs;
        if (ReadLiveTimeoutMs != baseline.ReadLiveTimeoutMs) result.ReadLiveTimeoutMs = ReadLiveTimeoutMs;
        if (SeekTimeoutMs != baseline.SeekTimeoutMs) result.SeekTimeoutMs = SeekTimeoutMs;
        if (SeekAccurate != baseline.SeekAccurate) result.SeekAccurate = SeekAccurate;
        if (SeekAccurateFixMarginMs != baseline.SeekAccurateFixMarginMs) result.SeekAccurateFixMarginMs = SeekAccurateFixMarginMs;
        if (SubtitlesEnabled != baseline.SubtitlesEnabled) result.SubtitlesEnabled = SubtitlesEnabled;
        if (SearchLocal != baseline.SearchLocal) result.SearchLocal = SearchLocal;
        if (SearchLocalPaths != baseline.SearchLocalPaths) result.SearchLocalPaths = SearchLocalPaths;
        if (EnabledCached != baseline.EnabledCached) result.EnabledCached = EnabledCached;
        if (OpenAutomaticSubs != baseline.OpenAutomaticSubs) result.OpenAutomaticSubs = OpenAutomaticSubs;
        if (!SubtitleLanguages.SequenceEqual(baseline.SubtitleLanguages, StringComparer.Ordinal)) result.SubtitleLanguages = [.. SubtitleLanguages];
        if (LanguageAutoDetect != baseline.LanguageAutoDetect) result.LanguageAutoDetect = LanguageAutoDetect;
        if (LanguageFallbackPrimary != baseline.LanguageFallbackPrimary) result.LanguageFallbackPrimary = LanguageFallbackPrimary;
        if (LanguageFallbackSecondary != baseline.LanguageFallbackSecondary) result.LanguageFallbackSecondary = LanguageFallbackSecondary;
        if (LanguageFallbackSecondarySame != baseline.LanguageFallbackSecondarySame) result.LanguageFallbackSecondarySame = LanguageFallbackSecondarySame;
        if (SubtitleExportUtf8Bom != baseline.SubtitleExportUtf8Bom) result.SubtitleExportUtf8Bom = SubtitleExportUtf8Bom;
        if (ResegmentSubtitles != baseline.ResegmentSubtitles) result.ResegmentSubtitles = ResegmentSubtitles;
        if (WordTimestamps != baseline.WordTimestamps) result.WordTimestamps = WordTimestamps;
        if (VadCueSnapping != baseline.VadCueSnapping) result.VadCueSnapping = VadCueSnapping;
        if (FixAllCaps != baseline.FixAllCaps) result.FixAllCaps = FixAllCaps;
        if (ASRDenoise != baseline.ASRDenoise) result.ASRDenoise = ASRDenoise;
        if (ASRPerSegmentLanguage != baseline.ASRPerSegmentLanguage) result.ASRPerSegmentLanguage = ASRPerSegmentLanguage;
        if (SubtitleMaxLinesPerCue != baseline.SubtitleMaxLinesPerCue) result.SubtitleMaxLinesPerCue = SubtitleMaxLinesPerCue;
        if (SubtitleMaxCharsPerLine != baseline.SubtitleMaxCharsPerLine) result.SubtitleMaxCharsPerLine = SubtitleMaxCharsPerLine;
        if (SubtitleMaxCjkCharsPerLine != baseline.SubtitleMaxCjkCharsPerLine) result.SubtitleMaxCjkCharsPerLine = SubtitleMaxCjkCharsPerLine;
        if (SubtitleMaxCueDurationSec != baseline.SubtitleMaxCueDurationSec) result.SubtitleMaxCueDurationSec = SubtitleMaxCueDurationSec;
        if (SubtitleMinCueDurationSec != baseline.SubtitleMinCueDurationSec) result.SubtitleMinCueDurationSec = SubtitleMinCueDurationSec;
        if (LogOutput != baseline.LogOutput) result.LogOutput = LogOutput;
        if (LogLevel != baseline.LogLevel) result.LogLevel = LogLevel;
        if (FFmpegLogLevel != baseline.FFmpegLogLevel) result.FFmpegLogLevel = FFmpegLogLevel;
        if (LogCachedLines != baseline.LogCachedLines) result.LogCachedLines = LogCachedLines;
        if (FFmpegLoadProfile != baseline.FFmpegLoadProfile) result.FFmpegLoadProfile = FFmpegLoadProfile;
        result.Validate();
        return result;
    }

    public void Validate()
    {
        Require(AudioVolumeMax is >= 1 and <= 1000, "Maximum volume must be from 1 to 1000%.");
        Require(AudioVolumeDefault >= 0 && AudioVolumeDefault <= AudioVolumeMax, "Default volume must be from 0 to the maximum volume.");
        Require(MaxAudioFrames > 0 && MaxVideoFrames > 0, "Audio and video frame limits must be positive whole numbers.");
        Require(VideoThreads >= 0 && ZoomOffset > 0 && MaxVerticalResolution >= 0, "Threads and resolution must be nonnegative; zoom unit must be positive.");
        foreach (long value in new[] { MinBufferDurationMs, BufferDurationMs, MaxLatencyMs, OpenTimeoutMs, ReadTimeoutMs, ReadLiveTimeoutMs, SeekTimeoutMs, SeekAccurateFixMarginMs })
            Require(value >= 0 && value <= MaxMilliseconds, "Use nonnegative milliseconds that fit the engine timer.");
        Require(MinBufferDurationMs <= BufferDurationMs, "Minimum buffer duration must not exceed maximum buffer duration.");
        Require(MaxLatencyMs <= MaxMilliseconds / 2, "Maximum latency is too large for the engine buffer.");
        if (MaxLatencyMs > 0)
            Require(BufferDurationMs >= MaxLatencyMs * 2 && MinBufferDurationMs <= MaxLatencyMs / 10,
                $"With maximum latency enabled, maximum buffer duration must be at least {MaxLatencyMs * 2} ms and minimum buffer duration at most {MaxLatencyMs / 10} ms.");
        Require(Enum.IsDefined(DeInterlace) && Enum.IsDefined(HDRtoSDRMethod), "Choose a supported software video option.");
        Require(Enum.IsDefined(VideoHardwareDecodeMode) && VideoHardwareDecodeMode != PortableVideoDecodeMode.Software,
            "Choose Automatic, D3D11VA or VAAPI as the remembered hardware decoder.");
        Require(Enum.IsDefined(VideoPresentationMode), "Choose a supported video presentation mode.");
        Require(VideoHardwareDevice != null && !VideoHardwareDevice.Any(char.IsControl),
            "The video device selector must not contain control characters.");
        Require(Enum.IsDefined(LogLevel) && Enum.IsDefined(FFmpegLogLevel) && Enum.IsDefined(FFmpegLoadProfile), "Choose a valid logging level and FFmpeg profile.");
        Require(LogCachedLines >= 0, "Cached log lines must be a nonnegative whole number (0 flushes each line).");
        ValidateLogOutput(LogOutput);
        Require(SearchLocalPaths != null && !SearchLocalPaths.Any(char.IsControl), "Subtitle search paths must not contain control characters.");
        Require(IsAspectRatio(AspectRatio, allowMode: true) && IsAspectRatio(AspectRatioCustom, allowMode: false), "Use Keep, Fill, Custom or a positive aspect ratio such as 16:9.");
        Require(float.IsFinite(SDRDisplayNits) && (SDRDisplayNits == 0 || SDRDisplayNits is >= 25 and <= 400), "SDR display nits must be 0 (automatic), or from 25 to 400.");
        Require(Brightness is >= -100 and <= 100 && Contrast is >= -100 and <= 100 && Saturation is >= -100 and <= 100 && Hue is >= -180 and <= 180, "Colour filters are out of range.");
        ValidateLanguages(AudioLanguages); ValidateLanguages(SubtitleLanguages);
        Require(LanguageFallbackPrimary == null || IsLanguage(LanguageFallbackPrimary), "Choose a valid primary fallback language.");
        Require(LanguageFallbackSecondary == null || IsLanguage(LanguageFallbackSecondary), "Choose a valid secondary fallback language.");
        Require(SubtitleMaxLinesPerCue > 0 && SubtitleMaxCharsPerLine > 0 && SubtitleMaxCjkCharsPerLine > 0, "Cue line and character limits must be positive whole numbers.");
        Require(SubtitleMaxLinesPerCue <= int.MaxValue / Math.Max(SubtitleMaxCharsPerLine, SubtitleMaxCjkCharsPerLine),
            "The cue line and character limits exceed the engine's text budget.");
        Require(double.IsFinite(SubtitleMaxCueDurationSec) && double.IsFinite(SubtitleMinCueDurationSec)
            && SubtitleMinCueDurationSec >= 0 && SubtitleMaxCueDurationSec >= 0
            && SubtitleMinCueDurationSec <= MaxMilliseconds / 1000d && SubtitleMaxCueDurationSec <= MaxMilliseconds / 1000d
            && (SubtitleMaxCueDurationSec == 0 || SubtitleMaxCueDurationSec >= SubtitleMinCueDurationSec),
            "Cue durations must be nonnegative finite seconds; 0 disables that limit. With a maximum enabled, the minimum must not exceed it.");
    }

    static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
    public static void ValidateLogOutput(string? output)
    {
        if (output == null || output.Any(char.IsControl)) throw new ArgumentException("Log output must not contain control characters.");
        if (output.Length == 0 || output is ":console" or ":debug" or ":custom") return;
        if (output.StartsWith(':')) throw new ArgumentException("Use a log file path, :console, :debug or :custom for log output.");
        try
        {
            string full = System.IO.Path.GetFullPath(output);
            if (System.IO.Path.GetFileName(full).Length == 0 || full.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
                throw new ArgumentException("Choose a valid log output file path.");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ArgumentException("Choose a valid log output file path, :console, :debug or :custom.", exception); }
    }
    internal static bool IsLanguage(string code) => !string.IsNullOrWhiteSpace(code) &&
        Language.Get(code).Culture is { } culture && !string.IsNullOrEmpty(culture.Name);
    internal static string CanonicalLanguageCode(string code)
    {
        if (!IsLanguage(code)) return code;
        Language language = Language.Get(code);
        return Language.AllLanguages.FirstOrDefault(candidate => candidate == language)?.CultureName ?? language.CultureName;
    }
    internal static List<string> CanonicalLanguagePriorities(IEnumerable<string> languages)
    {
        var result = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string code in languages)
        {
            // Keep malformed input visible to validation; canonicalization must not turn bad input into a valid list.
            if (!IsLanguage(code)) { result.Add(code); continue; }
            string canonical = CanonicalLanguageCode(code);
            if (seen.Add(Language.Get(canonical).IdSubLanguage)) result.Add(canonical);
        }
        return result;
    }
    static void ValidateLanguages(List<string>? languages)
    {
        if (languages == null || !languages.All(IsLanguage))
            throw new ArgumentException("Choose valid language codes in priority order.");
        Require(languages.Select(code => Language.Get(code).IdSubLanguage).Distinct(StringComparer.OrdinalIgnoreCase).Count() == languages.Count,
            "A language may occur only once in each priority list.");
    }
    static bool IsAspectRatio(string? value, bool allowMode)
    {
        if (allowMode && value is "Keep" or "Fill" or "Custom") return true;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            FlyleafLib.AspectRatio ratio = new(value);
            return double.IsFinite(ratio.Num) && double.IsFinite(ratio.Den) && ratio.Num > 0 && ratio.Den > 0;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException) { return false; }
    }

    /// <summary>Called on a fresh engine config before engine startup. Empty log path retains the host's normal path.</summary>
    public void ApplyStartupTo(EngineConfig config)
    {
        Validate();
        if (LogOutput.Length > 0) config.LogOutput = LogOutput;
        // The existing environment override remains authoritative when explicitly configured.
        config.LogLevel = Enum.TryParse(Environment.GetEnvironmentVariable(EngineBootstrap.LogLevelVariable), true,
            out FlyleafLib.LogLevel configured) && Enum.IsDefined(configured) ? configured : LogLevel;
        config.FFmpegLogLevel = FFmpegLogLevel;
        config.LogCachedLines = LogCachedLines;
        config.FFmpegLoadProfile = FFmpegLoadProfile;
    }

    /// <summary>Apply within the controller mutation queue. Audio may be delegated to its device/recovery adapter.</summary>
    public void ApplyTo(Config config, bool includeAudio = true)
    {
        Validate();
        if (includeAudio)
        {
            config.Audio.VolumeMax = AudioVolumeMax;
            config.Audio.VolumeDefault = AudioVolumeDefault;
            config.Audio.Languages = AudioLanguages.Select(Language.Get).ToList();
            config.Audio.FiltersEnabled = AudioFiltersEnabled;
            config.Audio.Enabled = AudioEnabled;
        }
        config.Video.AspectRatioCustom = new(AspectRatioCustom);
        config.Video.AspectRatio = new(AspectRatio);
        config.Video.DeInterlace = DeInterlace;
        config.Video.DoubleRate = DoubleRate;
        config.Decoder.AllowProfileMismatch = AllowProfileMismatch;
        config.Video.PortableDecodeMode = VideoHardwareAccelerationEnabled ? VideoHardwareDecodeMode : PortableVideoDecodeMode.Software;
        config.Video.PortableHardwareDevice = VideoHardwareDevice;
        // Software is the portable off switch. Preserve the legacy allowance until a real hardware request enables it.
        if (VideoHardwareAccelerationEnabled) config.Video.VideoAcceleration = true;
        config.Decoder.VideoThreads = VideoThreads;
        config.Video.ZoomOffset = ZoomOffset;
        config.Video.HDRtoSDRMethod = HDRtoSDRMethod;
        config.Video.SDRDisplayNitsCustom = SDRDisplayNits;
        config.Player.MaxLatency = MaxLatencyMs * TimeSpan.TicksPerMillisecond;
        if (config.Player.ZeroLatency != ZeroLatency) config.Player.ZeroLatency = ZeroLatency;
        // Both engine setters own LowDelay. In particular, clearing MaxLatency must not turn it off while
        // ZeroLatency remains enabled, and clearing ZeroLatency must not defeat an enabled MaxLatency policy.
        config.Decoder.LowDelay = ZeroLatency || MaxLatencyMs > 0;
        config.Demuxer.BufferDuration = BufferDurationMs * TimeSpan.TicksPerMillisecond;
        config.Player.MinBufferDuration = MinBufferDurationMs * TimeSpan.TicksPerMillisecond;
        config.Decoder.MaxAudioFrames = MaxAudioFrames;
        config.Decoder.MaxVideoFrames = MaxVideoFrames;
        config.Video.MaxVerticalResolutionCustom = MaxVerticalResolution;
        config.Demuxer.OpenTimeout = OpenTimeoutMs * TimeSpan.TicksPerMillisecond;
        config.Demuxer.ReadTimeout = ReadTimeoutMs * TimeSpan.TicksPerMillisecond;
        config.Demuxer.ReadLiveTimeout = ReadLiveTimeoutMs * TimeSpan.TicksPerMillisecond;
        config.Demuxer.SeekTimeout = SeekTimeoutMs * TimeSpan.TicksPerMillisecond;
        config.Player.SeekAccurate = SeekAccurate;
        config.Player.SeekAccurateFixMargin = SeekAccurateFixMarginMs * TimeSpan.TicksPerMillisecond;
        SetFilter(config, FLFilters.Brightness, Brightness, -100, 100, -0.5f, 0.5f);
        SetFilter(config, FLFilters.Contrast, Contrast, -100, 100, 0, 2);
        SetFilter(config, FLFilters.Hue, Hue, -180, 180, -3.14f, 3.14f);
        SetFilter(config, FLFilters.Saturation, Saturation, -100, 100, 0, 2);
        config.Subtitles.Enabled = SubtitlesEnabled;
        config.Subtitles.SearchLocal = SearchLocal;
        config.Subtitles.SearchLocalPaths = SearchLocalPaths;
        config.Subtitles.EnabledCached = EnabledCached;
        config.Subtitles.OpenAutomaticSubs = OpenAutomaticSubs;
        config.Subtitles.Languages = SubtitleLanguages.Select(Language.Get).ToList();
        config.Subtitles.LanguageAutoDetect = LanguageAutoDetect;
        config.Subtitles.LanguageFallbackPrimary = LanguageFallbackPrimary == null ? null : Language.Get(LanguageFallbackPrimary);
        config.Subtitles.LanguageFallbackSecondary = LanguageFallbackSecondary == null ? null : Language.Get(LanguageFallbackSecondary);
        config.Subtitles.LanguageFallbackSecondarySame = LanguageFallbackSecondarySame;
        config.Subtitles.ResegmentSubtitles = ResegmentSubtitles;
        config.Subtitles.WordTimestamps = WordTimestamps;
        config.Subtitles.VadCueSnapping = VadCueSnapping;
        config.Subtitles.FixAllCaps = FixAllCaps;
        config.Subtitles.ASRDenoise = ASRDenoise;
        config.Subtitles.ASRPerSegmentLanguage = ASRPerSegmentLanguage;
        config.Subtitles.SubtitleMaxLinesPerCue = SubtitleMaxLinesPerCue;
        config.Subtitles.SubtitleMaxCharsPerLine = SubtitleMaxCharsPerLine;
        config.Subtitles.SubtitleMaxCjkCharsPerLine = SubtitleMaxCjkCharsPerLine;
        config.Subtitles.SubtitleMaxCueDurationSec = SubtitleMaxCueDurationSec;
        config.Subtitles.SubtitleMinCueDurationSec = SubtitleMinCueDurationSec;
    }

    static void SetFilter(Config config, FLFilters id, int value, int minimum, int maximum, float minimumPs, float maximumPs)
    {
        if (!config.Video.FLFilters.TryGetValue(id, out FLFilter? filter))
        {
            filter = new FLFilter { Filter = id, Minimum = minimum, Maximum = maximum, MinimumPS = minimumPs,
                MaximumPS = maximumPs, Default = 0, Step = 1 };
            config.Video.FLFilters.Add(id, filter);
        }
        filter.Value = value;
    }

    public static PlaybackSettings FromConfig(Config config, EngineConfig engine, bool subtitleExportUtf8Bom = true,
        PlaybackSettings? rememberedPreferences = null) => new()
    {
        AudioEnabled = config.Audio.Enabled, AudioFiltersEnabled = config.Audio.FiltersEnabled,
        AudioVolumeMax = config.Audio.VolumeMax, AudioVolumeDefault = config.Audio.VolumeDefault,
        AudioLanguages = config.Audio.Languages.Select(language => language.CultureName).ToList(),
        AspectRatio = config.Video.AspectRatio.ToString(), AspectRatioCustom = config.Video.AspectRatioCustom.ToString(),
        DeInterlace = config.Video.DeInterlace, DoubleRate = config.Video.DoubleRate,
        AllowProfileMismatch = config.Decoder.AllowProfileMismatch, VideoThreads = config.Decoder.VideoThreads,
        VideoHardwareAccelerationEnabled = config.Video.PortableDecodeMode != PortableVideoDecodeMode.Software,
        VideoHardwareDecodeMode = config.Video.PortableDecodeMode == PortableVideoDecodeMode.Software
            ? rememberedPreferences?.VideoHardwareDecodeMode ?? PortableVideoDecodeMode.Automatic : config.Video.PortableDecodeMode,
        VideoHardwareDevice = config.Video.PortableDecodeMode == PortableVideoDecodeMode.Software
            ? rememberedPreferences?.VideoHardwareDevice ?? config.Video.PortableHardwareDevice : config.Video.PortableHardwareDevice,
        VideoPresentationMode = rememberedPreferences?.VideoPresentationMode ?? PortableVideoPresentationMode.Software,
        ZoomOffset = config.Video.ZoomOffset, MaxLatencyMs = config.Player.MaxLatency / TimeSpan.TicksPerMillisecond,
        ZeroLatency = config.Player.ZeroLatency, HDRtoSDRMethod = config.Video.HDRtoSDRMethod,
        SDRDisplayNits = config.Video.SDRDisplayNitsCustom,
        Brightness = FilterValue(config, FLFilters.Brightness), Contrast = FilterValue(config, FLFilters.Contrast),
        Hue = FilterValue(config, FLFilters.Hue), Saturation = FilterValue(config, FLFilters.Saturation),
        MinBufferDurationMs = config.Player.MinBufferDuration / TimeSpan.TicksPerMillisecond,
        BufferDurationMs = config.Demuxer.BufferDuration / TimeSpan.TicksPerMillisecond,
        MaxAudioFrames = config.Decoder.MaxAudioFrames, MaxVideoFrames = config.Decoder.MaxVideoFrames,
        MaxVerticalResolution = config.Video.MaxVerticalResolutionCustom,
        OpenTimeoutMs = config.Demuxer.OpenTimeout / TimeSpan.TicksPerMillisecond,
        ReadTimeoutMs = config.Demuxer.ReadTimeout / TimeSpan.TicksPerMillisecond,
        ReadLiveTimeoutMs = config.Demuxer.ReadLiveTimeout / TimeSpan.TicksPerMillisecond,
        SeekTimeoutMs = config.Demuxer.SeekTimeout / TimeSpan.TicksPerMillisecond,
        SeekAccurate = config.Player.SeekAccurate, SeekAccurateFixMarginMs = config.Player.SeekAccurateFixMargin / TimeSpan.TicksPerMillisecond,
        SubtitlesEnabled = config.Subtitles.Enabled, SearchLocal = config.Subtitles.SearchLocal,
        SearchLocalPaths = config.Subtitles.SearchLocalPaths, EnabledCached = config.Subtitles.EnabledCached,
        OpenAutomaticSubs = config.Subtitles.OpenAutomaticSubs,
        SubtitleLanguages = config.Subtitles.Languages.Select(language => language.CultureName).ToList(),
        LanguageAutoDetect = config.Subtitles.LanguageAutoDetect,
        LanguageFallbackPrimary = config.Subtitles.LanguageFallbackPrimary?.CultureName,
        LanguageFallbackSecondary = config.Subtitles.LanguageFallbackSecondary?.CultureName,
        LanguageFallbackSecondarySame = config.Subtitles.LanguageFallbackSecondarySame,
        SubtitleExportUtf8Bom = subtitleExportUtf8Bom, ResegmentSubtitles = config.Subtitles.ResegmentSubtitles,
        WordTimestamps = config.Subtitles.WordTimestamps, VadCueSnapping = config.Subtitles.VadCueSnapping,
        FixAllCaps = config.Subtitles.FixAllCaps, ASRDenoise = config.Subtitles.ASRDenoise,
        ASRPerSegmentLanguage = config.Subtitles.ASRPerSegmentLanguage,
        SubtitleMaxLinesPerCue = config.Subtitles.SubtitleMaxLinesPerCue,
        SubtitleMaxCharsPerLine = config.Subtitles.SubtitleMaxCharsPerLine,
        SubtitleMaxCjkCharsPerLine = config.Subtitles.SubtitleMaxCjkCharsPerLine,
        SubtitleMaxCueDurationSec = config.Subtitles.SubtitleMaxCueDurationSec,
        SubtitleMinCueDurationSec = config.Subtitles.SubtitleMinCueDurationSec,
        LogOutput = engine.LogOutput, LogLevel = engine.LogLevel, FFmpegLogLevel = engine.FFmpegLogLevel,
        LogCachedLines = engine.LogCachedLines, FFmpegLoadProfile = engine.FFmpegLoadProfile
    };
    static int FilterValue(Config config, FLFilters id) => config.Video.FLFilters.TryGetValue(id, out var filter) ? filter.Value : 0;
}

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

public sealed class PlaybackSettingsStore(string path)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public string Path { get; } = path;
    public string? LoadWarning { get; private set; }
    public bool HasSavedSettings { get; private set; }

    public PlaybackSettings Load()
    {
        HasSavedSettings = false; LoadWarning = null;
        try
        {
            try { File.GetAttributes(Path); }
            catch (FileNotFoundException) { return new(); }
            catch (DirectoryNotFoundException) { return new(); }
            PlaybackSettings settings = JsonSerializer.Deserialize<PlaybackSettings>(File.ReadAllText(Path), JsonOptions)
                ?? throw new JsonException("Empty playback settings.");
            if (settings.AudioLanguages != null) settings.AudioLanguages = PlaybackSettings.CanonicalLanguagePriorities(settings.AudioLanguages);
            if (settings.SubtitleLanguages != null) settings.SubtitleLanguages = PlaybackSettings.CanonicalLanguagePriorities(settings.SubtitleLanguages);
            settings.Validate(); HasSavedSettings = true; return settings;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LoadWarning = "Playback settings could not be read. Defaults are used; the original file was preserved. Save explicitly to replace it.";
            return new();
        }
    }

    public void Save(PlaybackSettings settings) => Save(settings, null);

    /// <summary>Commit the playback file and an atomic secondary preferences write before the caller applies live changes.
    /// A failed secondary write restores the original raw file only while the candidate still belongs to this save.</summary>
    public void Save(PlaybackSettings settings, Action? commitSecondary)
    {
        settings.Validate();
        string full = System.IO.Path.GetFullPath(Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        byte[]? original;
        try { original = File.ReadAllBytes(full); }
        catch (FileNotFoundException) { original = null; }
        bool savedBefore = HasSavedSettings;
        string? warningBefore = LoadWarning;
        byte[] candidate = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, JsonOptions));
        WriteBytes(full, candidate);
        try { commitSecondary?.Invoke(); }
        catch (Exception secondaryFailure)
        {
            HasSavedSettings = savedBefore; LoadWarning = warningBefore;
            try
            {
                byte[]? current;
                try { current = File.ReadAllBytes(full); }
                catch (FileNotFoundException) { current = null; }
                // A concurrent writer owns a replacement. Never restore over it or delete it.
                if (current != null && current.AsSpan().SequenceEqual(candidate))
                {
                    if (original == null) File.Delete(full);
                    else WriteBytes(full, original);
                }
            }
            catch (Exception rollbackFailure) when (rollbackFailure is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Playback preferences could not be committed and their original file could not be restored.",
                    new AggregateException(secondaryFailure, rollbackFailure));
            }
            throw;
        }
        HasSavedSettings = true; LoadWarning = null;
    }

    static void WriteBytes(string path, byte[] bytes)
    {
        string temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, overwrite: true); }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
