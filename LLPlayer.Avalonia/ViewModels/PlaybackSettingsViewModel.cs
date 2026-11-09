using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>Raw text stays visible until explicit Save validates the complete detached settings draft.</summary>
public sealed partial class PlaybackSettingsViewModel : ObservableObject
{
    readonly PlaybackSettings baseline;
    readonly PlaybackRuntimeSettings runtime;
    readonly Func<PlaybackSettings, PlaybackSettings, PlaybackRuntimeSettings, bool, Task> save;
    readonly Func<Task<PlaybackRuntimeSettings>>? refreshDevices;
    readonly Dictionary<string, PlaybackNumberEditor> numbers = new(StringComparer.Ordinal);
    readonly List<Action<PlaybackSettings>> applyNumbers = [];
    public PlaybackSettings Draft { get; }
    public PlaybackLanguageEditor AudioLanguages { get; }
    public PlaybackLanguageEditor SubtitleLanguages { get; }
    public IReadOnlyList<string> AspectRatios { get; } = FlyleafLib.AspectRatio.AspectRatios.Select(value => value.ToString()).ToArray();
    public IReadOnlyList<DeInterlace> DeinterlaceOptions { get; } = Enum.GetValues<DeInterlace>();
    public IReadOnlyList<HDRtoSDRMethod> HdrOptions { get; } = Enum.GetValues<HDRtoSDRMethod>();
    public IReadOnlyList<FlyleafLib.LogLevel> LogLevels { get; } = Enum.GetValues<FlyleafLib.LogLevel>();
    public IReadOnlyList<Flyleaf.FFmpeg.LogLevel> FFmpegLogLevels { get; } = Enum.GetValues<Flyleaf.FFmpeg.LogLevel>();
    public IReadOnlyList<Flyleaf.FFmpeg.LoadProfile> FFmpegLoadProfiles { get; } = Enum.GetValues<Flyleaf.FFmpeg.LoadProfile>();
    public IReadOnlyList<PlaybackLanguageChoice> FallbackLanguages { get; }
    public ObservableCollection<PlaybackNumberEditor> AudioNumbers { get; } = [];
    public ObservableCollection<PlaybackNumberEditor> VideoNumbers { get; } = [];
    public ObservableCollection<PlaybackNumberEditor> ColorNumbers { get; } = [];
    public ObservableCollection<PlaybackNumberEditor> PlayerNumbers { get; } = [];
    public ObservableCollection<PlaybackNumberEditor> SubtitleNumbers { get; } = [];
    public ObservableCollection<PlaybackNumberEditor> SubtitleDelayNumbers { get; } = [];
    public ObservableCollection<PlaybackNumberEditor> StartupNumbers { get; } = [];
    public ObservableCollection<PlaybackNumberEditor> GeometryNumbers { get; } = [];
    public ObservableCollection<AudioDeviceInfo> AudioDeviceChoices { get; } = [];
    public string DefaultAudioDeviceName { get; private set; } = "System default";
    public string AudioRecoveryStatus { get; private set; } = "";
    public bool FiltersAvailable => runtime.FiltersAvailable;
    public bool CanRefreshDevices => refreshDevices != null && !Busy;
    public string FrameFiltersHelp => FiltersAvailable ? "Software deinterlacing and HDR tone mapping use the loaded FFmpeg filters."
        : "The current FFmpeg Main profile has no frame filters. Select Filters or All in Startup, then restart.";
    public string HardwareCapabilityReason => "Direct3D video processing, super resolution, noise reduction, edge enhancement and stereo adjustment are unavailable. VSync is managed by the desktop compositor.";
    [ObservableProperty] AudioDeviceInfo? selectedAudioDevice;
    [ObservableProperty] PlaybackLanguageChoice? primaryFallbackLanguage;
    [ObservableProperty] PlaybackLanguageChoice? secondaryFallbackLanguage;
    [ObservableProperty] bool horizontalFlip;
    [ObservableProperty] bool verticalFlip;
    [ObservableProperty] string? error;
    [ObservableProperty] string? warning;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand)), NotifyCanExecuteChangedFor(nameof(CloseCommand)),
        NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand)), NotifyCanExecuteChangedFor(nameof(RefreshVideoAdaptersCommand)),
        NotifyCanExecuteChangedFor(nameof(UpdateVideoBackendStatusCommand)), NotifyPropertyChangedFor(nameof(CanRefreshDevices))]
    bool busy;
    public event Action? CloseRequested;

    public PlaybackSettingsViewModel(PlaybackSettings current, PlaybackRuntimeSettings runtime,
        Func<PlaybackSettings, PlaybackSettings, PlaybackRuntimeSettings, bool, Task> save,
        string? warning = null, Func<Task<PlaybackRuntimeSettings>>? refreshDevices = null,
        Func<PortableVideoDecodeStatus>? decodeStatus = null,
        Func<PortableVideoPresentationStatus>? presentationStatus = null,
        Func<IReadOnlyList<PortableVideoAdapterInfo>>? videoAdapters = null,
        Func<Task<IReadOnlyList<PortableVideoAdapterInfo>>>? refreshVideoAdapters = null)
    {
        baseline = current.Clone(); Draft = current.Clone();
        this.runtime = runtime with { Baseline = null, AudioDevices = runtime.AudioDevices.ToArray() };
        this.save = save; this.warning = warning; this.refreshDevices = refreshDevices;
        InitializeVideoBackends(decodeStatus, presentationStatus, videoAdapters, refreshVideoAdapters);
        AudioLanguages = new(Draft.AudioLanguages); SubtitleLanguages = new(Draft.SubtitleLanguages);
        // An unchanged legacy locale/alias is the same draft identity as its neutral catalog row. Comparing this
        // normalized baseline prevents an unrelated Save from overwriting a fresher language priority list.
        baseline.AudioLanguages = AudioLanguages.SelectedLanguages.Select(language => language.Code).ToList();
        baseline.SubtitleLanguages = SubtitleLanguages.SelectedLanguages.Select(language => language.Code).ToList();
        Draft.AudioLanguages = [.. baseline.AudioLanguages]; Draft.SubtitleLanguages = [.. baseline.SubtitleLanguages];
        var fallbacks = new List<PlaybackLanguageChoice> { new("", "Use first preferred language") };
        fallbacks.AddRange(PlaybackLanguageEditor.Catalog);
        foreach (string? code in new[] { Draft.LanguageFallbackPrimary, Draft.LanguageFallbackSecondary })
            if (code != null && !fallbacks.Any(item => item.Code == code))
                fallbacks.Add(new PlaybackLanguageChoice(code, Language.Get(code).DisplayName + " [" + code + "]"));
        FallbackLanguages = fallbacks;
        PrimaryFallbackLanguage = fallbacks.FirstOrDefault(value => value.Code == (Draft.LanguageFallbackPrimary ?? "")) ?? fallbacks[0];
        SecondaryFallbackLanguage = fallbacks.FirstOrDefault(value => value.Code == (Draft.LanguageFallbackSecondary ?? "")) ?? fallbacks[0];
        HorizontalFlip = runtime.HFlip; VerticalFlip = runtime.VFlip;
        UpdateDevices(runtime, preserveSelection: false);
        AddWhole(AudioNumbers, "AudioVolumeMax", "Maximum volume (1–1000%)", Draft.AudioVolumeMax, 1, 1000, (target, value) => target.AudioVolumeMax = (int)value);
        AddWhole(AudioNumbers, "AudioVolumeDefault", "Default volume (0–maximum %)", Draft.AudioVolumeDefault, 0, 1000, (target, value) => target.AudioVolumeDefault = (int)value);
        AddSession(AudioNumbers, "AudioDelayMs", "Audio delay (ms, current stream)", TicksText(runtime.AudioDelayTicks), signed: true);
        AddWhole(VideoNumbers, "VideoThreads", "Decoder threads (0 = automatic; applies on next open)", Draft.VideoThreads, 0, int.MaxValue, (target, value) => target.VideoThreads = (int)value);
        AddWhole(VideoNumbers, "ZoomOffset", "Zoom unit (%)", Draft.ZoomOffset, 1, int.MaxValue, (target, value) => target.ZoomOffset = (int)value);
        AddWhole(VideoNumbers, "MaxLatencyMs", "Maximum live latency (ms; 0 = disabled)", Draft.MaxLatencyMs, 0, PlaybackSettings.MaxMilliseconds / 2, (target, value) => target.MaxLatencyMs = value);
        AddDecimal(VideoNumbers, "SDRDisplayNits", "SDR display nits (0 = automatic; otherwise 25–400)", Draft.SDRDisplayNits, 0, 400, (target, value) => target.SDRDisplayNits = (float)value);
        AddWhole(ColorNumbers, "Brightness", "Brightness (−100–100)", Draft.Brightness, -100, 100, (target, value) => target.Brightness = (int)value, reset: true);
        AddWhole(ColorNumbers, "Contrast", "Contrast (−100–100)", Draft.Contrast, -100, 100, (target, value) => target.Contrast = (int)value, reset: true);
        AddWhole(ColorNumbers, "Hue", "Hue (−180–180°)", Draft.Hue, -180, 180, (target, value) => target.Hue = (int)value, reset: true);
        AddWhole(ColorNumbers, "Saturation", "Saturation (−100–100)", Draft.Saturation, -100, 100, (target, value) => target.Saturation = (int)value, reset: true);
        AddWhole(PlayerNumbers, "MinBufferDurationMs", "Minimum buffer duration (ms)", Draft.MinBufferDurationMs, 0, PlaybackSettings.MaxMilliseconds, (target, value) => target.MinBufferDurationMs = value);
        AddWhole(PlayerNumbers, "BufferDurationMs", "Maximum buffer duration (ms)", Draft.BufferDurationMs, 0, PlaybackSettings.MaxMilliseconds, (target, value) => target.BufferDurationMs = value);
        AddWhole(PlayerNumbers, "MaxAudioFrames", "Maximum audio frames", Draft.MaxAudioFrames, 1, int.MaxValue, (target, value) => target.MaxAudioFrames = (int)value);
        AddWhole(PlayerNumbers, "MaxVideoFrames", "Maximum video frames", Draft.MaxVideoFrames, 1, int.MaxValue, (target, value) => target.MaxVideoFrames = (int)value);
        AddWhole(PlayerNumbers, "MaxVerticalResolution", "Maximum vertical resolution (0 = automatic)", Draft.MaxVerticalResolution, 0, int.MaxValue, (target, value) => target.MaxVerticalResolution = (int)value);
        AddWhole(PlayerNumbers, "OpenTimeoutMs", "Open timeout (ms)", Draft.OpenTimeoutMs, 0, PlaybackSettings.MaxMilliseconds, (target, value) => target.OpenTimeoutMs = value);
        AddWhole(PlayerNumbers, "ReadTimeoutMs", "Read timeout (ms)", Draft.ReadTimeoutMs, 0, PlaybackSettings.MaxMilliseconds, (target, value) => target.ReadTimeoutMs = value);
        AddWhole(PlayerNumbers, "ReadLiveTimeoutMs", "Live read timeout (ms)", Draft.ReadLiveTimeoutMs, 0, PlaybackSettings.MaxMilliseconds, (target, value) => target.ReadLiveTimeoutMs = value);
        AddWhole(PlayerNumbers, "SeekTimeoutMs", "Seek timeout (ms)", Draft.SeekTimeoutMs, 0, PlaybackSettings.MaxMilliseconds, (target, value) => target.SeekTimeoutMs = value);
        AddWhole(PlayerNumbers, "SeekAccurateFixMarginMs", "Accurate-seek fix margin (ms)", Draft.SeekAccurateFixMarginMs, 0, PlaybackSettings.MaxMilliseconds, (target, value) => target.SeekAccurateFixMarginMs = value);
        AddWhole(SubtitleNumbers, "SubtitleMaxLinesPerCue", "Maximum lines per generated cue", Draft.SubtitleMaxLinesPerCue, 1, int.MaxValue, (target, value) => target.SubtitleMaxLinesPerCue = (int)value);
        AddWhole(SubtitleNumbers, "SubtitleMaxCharsPerLine", "Maximum characters per line", Draft.SubtitleMaxCharsPerLine, 1, int.MaxValue, (target, value) => target.SubtitleMaxCharsPerLine = (int)value);
        AddWhole(SubtitleNumbers, "SubtitleMaxCjkCharsPerLine", "Maximum CJK characters per line", Draft.SubtitleMaxCjkCharsPerLine, 1, int.MaxValue, (target, value) => target.SubtitleMaxCjkCharsPerLine = (int)value);
        AddDecimal(SubtitleNumbers, "SubtitleMaxCueDurationSec", "Maximum cue duration (seconds; 0 = disabled)", Draft.SubtitleMaxCueDurationSec, 0, PlaybackSettings.MaxMilliseconds / 1000d, (target, value) => target.SubtitleMaxCueDurationSec = value);
        AddDecimal(SubtitleNumbers, "SubtitleMinCueDurationSec", "Minimum cue duration (seconds; 0 = disabled)", Draft.SubtitleMinCueDurationSec, 0, PlaybackSettings.MaxMilliseconds / 1000d, (target, value) => target.SubtitleMinCueDurationSec = value);
        AddSession(SubtitleDelayNumbers, "PrimarySubtitleDelayMs", "Primary subtitle delay (ms, current track)", TicksText(runtime.PrimarySubtitleDelayTicks), signed: true);
        AddSession(SubtitleDelayNumbers, "SecondarySubtitleDelayMs", "Secondary subtitle delay (ms, current track)", TicksText(runtime.SecondarySubtitleDelayTicks), signed: true);
        AddWhole(StartupNumbers, "LogCachedLines", "Cached log lines (0 = flush each line)", Draft.LogCachedLines, 0, int.MaxValue, (target, value) => target.LogCachedLines = (int)value);
        AddSession(GeometryNumbers, "ZoomPercent", "Zoom (%, current video)", runtime.ZoomPercent.ToString(CultureInfo.CurrentCulture), 0.1, 10000);
        AddSession(GeometryNumbers, "PanX", "Horizontal pan (−10–10, current video)", runtime.PanX.ToString(CultureInfo.CurrentCulture), -10, 10);
        AddSession(GeometryNumbers, "PanY", "Vertical pan (−10–10, current video)", runtime.PanY.ToString(CultureInfo.CurrentCulture), -10, 10);
        AddSession(GeometryNumbers, "Rotation", "Rotation (0, 90, 180 or 270°, current video)", runtime.Rotation.ToString(CultureInfo.CurrentCulture), 0, 270);
        AddSession(GeometryNumbers, "CropTop", "Crop top (pixels, current video)", runtime.CropTop.ToString(CultureInfo.CurrentCulture), 0, uint.MaxValue);
        AddSession(GeometryNumbers, "CropLeft", "Crop left (pixels, current video)", runtime.CropLeft.ToString(CultureInfo.CurrentCulture), 0, uint.MaxValue);
        AddSession(GeometryNumbers, "CropBottom", "Crop bottom (pixels, current video)", runtime.CropBottom.ToString(CultureInfo.CurrentCulture), 0, uint.MaxValue);
        AddSession(GeometryNumbers, "CropRight", "Crop right (pixels, current video)", runtime.CropRight.ToString(CultureInfo.CurrentCulture), 0, uint.MaxValue);
    }

    public PlaybackNumberEditor FindNumber(string key) => numbers[key];
    void AddWhole(ObservableCollection<PlaybackNumberEditor> group, string key, string label, long value,
        long minimum, long maximum, Action<PlaybackSettings, long> assign, bool reset = false)
    {
        var row = new PlaybackNumberEditor(key, label, value.ToString(CultureInfo.CurrentCulture), minimum, maximum, whole: true, reset);
        numbers.Add(key, row); group.Add(row); applyNumbers.Add(target => assign(target, row.ReadWhole()));
    }
    void AddDecimal(ObservableCollection<PlaybackNumberEditor> group, string key, string label, double value,
        double minimum, double maximum, Action<PlaybackSettings, double> assign)
    {
        var row = new PlaybackNumberEditor(key, label, value.ToString(CultureInfo.CurrentCulture), minimum, maximum, whole: false);
        numbers.Add(key, row); group.Add(row); applyNumbers.Add(target => assign(target, row.ReadDecimal()));
    }
    void AddSession(ObservableCollection<PlaybackNumberEditor> group, string key, string label, string text,
        double minimum = 0, double maximum = PlaybackSettings.MaxMilliseconds, bool signed = false)
    {
        var row = new PlaybackNumberEditor(key, label, text, signed ? -PlaybackSettings.MaxMilliseconds : minimum, maximum,
            whole: key is "Rotation" or "CropTop" or "CropLeft" or "CropBottom" or "CropRight");
        numbers.Add(key, row); group.Add(row);
    }
    static string TicksText(long ticks) => ((decimal)ticks / TimeSpan.TicksPerMillisecond).ToString(CultureInfo.CurrentCulture);

    void UpdateDevices(PlaybackRuntimeSettings state, bool preserveSelection)
    {
        string? selected = preserveSelection ? SelectedAudioDevice?.Id : state.AudioDevice;
        AudioDeviceChoices.Clear(); AudioDeviceChoices.Add(new AudioDeviceInfo("", "System default"));
        foreach (AudioDeviceInfo device in state.AudioDevices)
            if (!string.IsNullOrEmpty(device.Id) && !AudioDeviceChoices.Any(item => item.Id == device.Id)) AudioDeviceChoices.Add(device);
        if (!string.IsNullOrEmpty(selected) && !AudioDeviceChoices.Any(item => item.Id == selected))
            AudioDeviceChoices.Add(new AudioDeviceInfo(selected, selected + " (unavailable; reconnect when available)"));
        SelectedAudioDevice = AudioDeviceChoices.FirstOrDefault(item => item.Id == (selected ?? "")) ?? AudioDeviceChoices[0];
        DefaultAudioDeviceName = state.AudioDevices.FirstOrDefault(item => item.Id == state.DefaultAudioDevice)?.Name
            ?? state.DefaultAudioDevice ?? "System default";
        string backend = string.IsNullOrEmpty(state.AudioBackendName) ? "Unknown backend" : state.AudioBackendName;
        string output = state.AudioDevices.FirstOrDefault(device => device.Id == state.AudioOutputDevice)?.Name
            ?? state.AudioOutputDevice ?? "System default";
        AudioRecoveryStatus = !state.AudioEnabled ? $"Audio disabled. Backend: {backend}."
            : state.AudioRecovering ? $"Audio recovery is active; output uses a silent clock. Backend: {backend}."
            : backend.Equals("Null", StringComparison.OrdinalIgnoreCase) ? "Silent output (Null backend)."
            : !state.AudioStreamOpened ? $"No audio stream is open. Backend: {backend}."
            : $"Backend: {backend}. Actual output: {output}."
                + (!string.IsNullOrEmpty(state.AudioDevice) && state.AudioDevice != state.AudioOutputDevice
                    ? " The selected device is unavailable; the actual output is a temporary fallback." : "");
        OnPropertyChanged(nameof(DefaultAudioDeviceName)); OnPropertyChanged(nameof(AudioRecoveryStatus));
    }

    bool CanEdit() => !Busy;
    [RelayCommand(CanExecute = nameof(CanEdit))]
    async Task SaveAsync()
    {
        if (Busy) return;
        Error = null;
        PlaybackSettings candidate;
        PlaybackRuntimeSettings session;
        try
        {
            candidate = Draft.Clone();
            foreach (Action<PlaybackSettings> apply in applyNumbers) apply(candidate);
            candidate.AudioLanguages = AudioLanguages.SelectedLanguages.Select(language => language.Code).ToList();
            candidate.SubtitleLanguages = SubtitleLanguages.SelectedLanguages.Select(language => language.Code).ToList();
            candidate.LanguageFallbackPrimary = string.IsNullOrEmpty(PrimaryFallbackLanguage?.Code) ? null : PrimaryFallbackLanguage.Code;
            candidate.LanguageFallbackSecondary = string.IsNullOrEmpty(SecondaryFallbackLanguage?.Code) ? null : SecondaryFallbackLanguage.Code;
            candidate.Validate();
            session = BuildRuntime();
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or FormatException)
        { Error = exception.Message; return; }
        Busy = true;
        bool completed = false;
        try
        {
            await save(candidate, baseline.Clone(), session, candidate.AudioVolumeDefault != baseline.AudioVolumeDefault);
            Warning = null; completed = true;
        }
        catch (Exception exception)
        {
            Error = exception is ArgumentException or InvalidOperationException ? exception.Message
                : "Playback settings could not be saved completely. Check the destination and retry.";
        }
        finally { Busy = false; }
        if (completed) CloseRequested?.Invoke();
    }

    PlaybackRuntimeSettings BuildRuntime()
    {
        uint rotation = (uint)FindNumber("Rotation").ReadWhole();
        if (rotation is not (0 or 90 or 180 or 270)) throw new ArgumentException("Rotation must be 0, 90, 180 or 270 degrees.");
        return runtime with
        {
            Baseline = runtime, AudioDevice = string.IsNullOrEmpty(SelectedAudioDevice?.Id) ? null : SelectedAudioDevice.Id,
            AudioDelayTicks = ReadDelay("AudioDelayMs"), PrimarySubtitleDelayTicks = ReadDelay("PrimarySubtitleDelayMs"),
            SecondarySubtitleDelayTicks = ReadDelay("SecondarySubtitleDelayMs"), ZoomPercent = FindNumber("ZoomPercent").ReadDecimal(),
            PanX = FindNumber("PanX").ReadDecimal(), PanY = FindNumber("PanY").ReadDecimal(), Rotation = rotation,
            CropTop = (uint)FindNumber("CropTop").ReadWhole(), CropLeft = (uint)FindNumber("CropLeft").ReadWhole(),
            CropBottom = (uint)FindNumber("CropBottom").ReadWhole(), CropRight = (uint)FindNumber("CropRight").ReadWhole(),
            HFlip = HorizontalFlip, VFlip = VerticalFlip
        };
    }

    long ReadDelay(string key)
    {
        var row = FindNumber(key);
        if (!decimal.TryParse(row.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.CurrentCulture, out decimal value) || value < -PlaybackSettings.MaxMilliseconds || value > PlaybackSettings.MaxMilliseconds)
            throw new ArgumentException(row.Label + ": enter finite milliseconds that fit the engine timer.");
        decimal ticks = value * TimeSpan.TicksPerMillisecond;
        if (ticks != decimal.Truncate(ticks)) throw new ArgumentException(row.Label + ": precision is limited to one engine tick (0.0001 ms).");
        return checked((long)ticks);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))] void Close() => CloseRequested?.Invoke();
    [RelayCommand] void ResetFilters() { foreach (PlaybackNumberEditor row in ColorNumbers) row.ResetCommand.Execute(null); }
    [RelayCommand] void ResetSearchLocalPaths()
    {
        Draft.SearchLocalPaths = Config.SubtitlesConfig.DefaultSearchLocalPaths;
        OnPropertyChanged(nameof(Draft));
    }
    // This resets only the existing viewport values. It does not reset audio, policies, filters or startup options.
    [RelayCommand] void ResetViewport()
    {
        FindNumber("ZoomPercent").Text = "100"; FindNumber("PanX").Text = "0"; FindNumber("PanY").Text = "0";
        FindNumber("Rotation").Text = "0";
        foreach (string key in new[] { "CropTop", "CropLeft", "CropBottom", "CropRight" }) FindNumber(key).Text = "0";
        HorizontalFlip = false; VerticalFlip = false; Draft.AspectRatio = "Keep";
        OnPropertyChanged(nameof(Draft));
    }
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    async Task RefreshDevicesAsync()
    {
        if (Busy || refreshDevices == null) return;
        Busy = true; Error = null;
        try { UpdateDevices(await refreshDevices(), preserveSelection: true); }
        catch (Exception) { Error = "Audio devices could not be refreshed. The current selection was preserved."; }
        finally { Busy = false; }
    }
    bool CanRefresh() => !Busy && refreshDevices != null;
}

public sealed partial class PlaybackNumberEditor(string key, string label, string initialText, double minimum, double maximum, bool whole, bool reset = false) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public bool CanReset { get; } = reset;
    [ObservableProperty] string text = initialText;

    public long ReadWhole()
    {
        if (!long.TryParse(Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out long value) || value < minimum || value > maximum)
            throw new ArgumentException(Label + ": enter a whole number in the displayed range.");
        return value;
    }
    public double ReadDecimal()
    {
        if (!double.TryParse(Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.CurrentCulture, out double value) || !double.IsFinite(value) || value < minimum || value > maximum
            || whole && value != Math.Truncate(value))
            throw new ArgumentException(Label + ": enter a finite number in the displayed range using your local decimal separator.");
        return value;
    }
    [RelayCommand] void Reset() { if (CanReset) Text = "0"; }
}

public sealed record PlaybackLanguageChoice(string Code, string Label);

/// <summary>Full source-language catalog and a separately owned ordered priority list.</summary>
public sealed partial class PlaybackLanguageEditor : ObservableObject
{
    public static IReadOnlyList<PlaybackLanguageChoice> Catalog { get; } = Language.AllLanguages
        .Select(language => new PlaybackLanguageChoice(language.CultureName, language.DisplayName)).ToArray();
    public ObservableCollection<PlaybackLanguageChoice> AvailableLanguages { get; } = [];
    public ObservableCollection<PlaybackLanguageChoice> SelectedLanguages { get; } = [];
    [ObservableProperty] string searchText = "";
    [ObservableProperty] PlaybackLanguageChoice? selectedAvailable;
    [ObservableProperty] PlaybackLanguageChoice? selected;
    public PlaybackLanguageEditor(IEnumerable<string> selected)
    {
        foreach (string code in PlaybackSettings.CanonicalLanguagePriorities(selected)) SelectedLanguages.Add(Choice(code));
        RefreshAvailable();
    }
    public static PlaybackLanguageChoice Choice(string code)
    {
        string canonical = PlaybackSettings.CanonicalLanguageCode(code);
        return Catalog.FirstOrDefault(item => item.Code == canonical)
            ?? new(canonical, Language.Get(canonical).DisplayName + " [" + canonical + "]");
    }
    static bool SameLanguage(PlaybackLanguageChoice left, PlaybackLanguageChoice right)
        => Language.Get(left.Code) == Language.Get(right.Code);
    partial void OnSearchTextChanged(string value) => RefreshAvailable();
    void RefreshAvailable()
    {
        AvailableLanguages.Clear();
        foreach (PlaybackLanguageChoice language in Catalog)
            if (!SelectedLanguages.Any(item => SameLanguage(item, language)) &&
                (SearchText.Length == 0 || language.Label.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    || language.Code.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)))
                AvailableLanguages.Add(language);
    }
    [RelayCommand] void Add()
    {
        if (SelectedAvailable is not { } language || !AvailableLanguages.Contains(language)
            || SelectedLanguages.Any(item => SameLanguage(item, language))) return;
        SelectedLanguages.Add(language); Selected = language; SelectedAvailable = null; RefreshAvailable();
    }
    [RelayCommand] void Remove()
    {
        if (Selected is not { } language) return;
        SelectedLanguages.Remove(language); Selected = null; RefreshAvailable();
    }
    [RelayCommand] void MoveUp()
    {
        int index = Selected == null ? -1 : SelectedLanguages.IndexOf(Selected);
        if (index > 0) SelectedLanguages.Move(index, index - 1);
    }
    [RelayCommand] void MoveDown()
    {
        int index = Selected == null ? -1 : SelectedLanguages.IndexOf(Selected);
        if (index >= 0 && index < SelectedLanguages.Count - 1) SelectedLanguages.Move(index, index + 1);
    }
}
