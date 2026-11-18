namespace LLPlayer.ControlApi;

public sealed record ControlGuiOperationSchema(ControlOperation Operation, Type Arguments, Type Result);

public enum ControlStreamKind { Audio, Video, Subtitle }

public sealed record ControlMediaOpen(string Path, bool OnlineVideo = false);
public sealed record ControlPosition(long PositionTicks);
public sealed record ControlSpeed(double Speed);
public sealed record ControlVolume(int Volume);
public sealed record ControlMute(bool Mute);
public sealed record ControlEnabled(bool Enabled);
public sealed record ControlChoice(Guid Id);
public sealed record ControlStreamList(ControlStreamKind Kind);
public sealed record ControlStreamSelection(ControlStreamKind Kind, Guid Id, int? Slot = null);
public sealed record ControlSubtitleLoad(int Slot, string Path);
public sealed record ControlSubtitleSlot(int Slot);
public sealed record ControlSubtitlePageRequest(int Slot, int Cursor = 0, int Limit = 100, Guid? Revision = null);
public sealed record ControlSubtitleCueRequest(int Slot, Guid CueId, Guid Revision);
public sealed record ControlSubtitleDelay(int Slot, long DelayTicks);
public sealed record ControlSubtitleTranslation(int Slot, bool Enabled);
public enum ControlUiTheme { Dark, Light }
public sealed record ControlGuiUiSet(bool? Fullscreen = null, bool? Sidebar = null, bool? AlwaysOnTop = null,
    ControlUiTheme? Theme = null);
public sealed record ControlClipboardText(string Text);
public sealed record ControlOpenUri(string Uri);
public sealed record ControlRevealPath(string Path);
public sealed record ControlDeviceSelection(string? DeviceId = null);
public enum ControlCaptureKind { Audio, Video }
public sealed record ControlCaptureDevices(ControlCaptureKind Kind, bool Refresh = false);
public sealed record ControlCaptureFormats(string DeviceId, long Epoch);
public sealed record ControlCaptureOpen(ControlCaptureKind Kind, string DeviceId, long Epoch, string? FormatId = null);
public sealed record ControlSnapshotRequest(string Path, bool Overwrite = false, uint Width = 0, uint Height = 0);
public sealed record ControlRecordingRequest(string Path, bool Overwrite = false);

public sealed record ControlGuiAction(bool Applied);
public sealed record ControlPlaybackSnapshot(Guid MediaId, long MediaRevision, string? Title,
    bool Opened, bool Playing, bool Opening, long PositionTicks, long DurationTicks, double Speed,
    int Volume, int MaximumVolume, bool Mute);
public sealed record ControlStreamSnapshot(Guid Id, string Label, bool Selected, bool? Primary = null, bool? Secondary = null);
public sealed record ControlChapterSnapshot(Guid Id, string Title, long StartTicks);
public sealed record ControlPlaylistSnapshot(Guid Id, int Index, string Title, bool Current);
public sealed record ControlSubtitleSlotSnapshot(int Slot, Guid Revision, bool Enabled, string? Language,
    long DelayTicks, int CueCount);
public sealed record ControlGuiCue(Guid Id, long StartTicks, long EndTicks, string Text, string Original,
    string? SourceLanguage, string? TargetLanguage, bool Translated, bool Bitmap, string? AssignedVoiceId,
    string? SpeakerId);
public sealed record ControlGuiSubtitlePage(Guid Revision, int TotalCount, int? NextCursor, IReadOnlyList<ControlGuiCue> Cues);
public sealed record ControlWaveformSnapshot(bool Enabled, bool Active, IReadOnlyList<float>? Peaks);
public sealed record ControlAbLoopSnapshot(long? StartTicks, long? EndTicks, bool Enabled);
public sealed record ControlGuiUiSnapshot(bool Fullscreen, bool Sidebar, bool AlwaysOnTop, ControlUiTheme? Theme = null,
    bool SidebarSearchFocused = false);
public sealed record ControlGuiDiagnostic(string Session, string Runtime, ControlPlaybackSnapshot Playback);
public sealed record ControlCommandDescription(string Id, string Name, string Description,
    string Group, IReadOnlyList<string> Keys, bool Available, bool AutomationSupported = false);
public sealed record ControlAudioDevice(string Id, string Name);
public sealed record ControlAudioDeviceSnapshot(bool Enabled, string SelectedDeviceId, string? DefaultDeviceId,
    string? OutputDeviceId, bool Recovering, IReadOnlyList<ControlAudioDevice> Devices);
public sealed record ControlVideoBackendSnapshot(string RequestedDecode, string EffectiveDecode, string? RequestedDevice,
    bool DecodePending, string? DecodeReason, string RequestedPresentation, string EffectivePresentation,
    bool PresentationPending, string? PresentationReason);
public sealed record ControlCaptureDevice(string Id, string Label, string Backend);
public sealed record ControlCaptureDevicesSnapshot(long Epoch, string Availability, string? Reason, IReadOnlyList<ControlCaptureDevice> Devices);
public sealed record ControlCaptureFormat(string Id, int Width, int Height, string Description);
public sealed record ControlCaptureFormatsSnapshot(long Epoch, string DeviceId, string Availability, string? Reason,
    IReadOnlyList<ControlCaptureFormat> Formats);
public sealed record ControlGuiAbout(string Name, string Version, string Runtime);
public sealed record ControlSnapshotResult(string Path, long Bytes);
public sealed record ControlRecordingSnapshot(bool Active, string? Path, bool Published);

public static class ControlGuiOperations
{
    public sealed record Empty { }

    public static IReadOnlyList<ControlGuiOperationSchema> All { get; } =
    [
        Define<ControlMediaOpen, ControlPlaybackSnapshot>("media.open", "media", "Open a source in the attached player and wait for the open result.", true),
        Define<Empty, ControlGuiAction>("media.cancel_open", "media", "Cancel the current opening in the attached player.", true),
        Define<Empty, ControlPlaybackSnapshot>("playback.get", "playback", "Read playback state of the selected window."),
        Define<Empty, ControlPlaybackSnapshot>("playback.play", "playback", "Play the opened media.", true),
        Define<Empty, ControlPlaybackSnapshot>("playback.pause", "playback", "Pause the opened media.", true),
        Define<ControlPosition, ControlPlaybackSnapshot>("playback.seek", "playback", "Set the current media position in ticks.", true),
        Define<ControlSpeed, ControlPlaybackSnapshot>("playback.set_speed", "playback", "Set playback speed.", true),
        Define<ControlVolume, ControlPlaybackSnapshot>("playback.set_volume", "playback", "Set playback volume.", true),
        Define<ControlMute, ControlPlaybackSnapshot>("playback.set_mute", "playback", "Mute or unmute playback.", true),
        Define<ControlStreamList, ControlStreamSnapshot[]>("streams.list", "streams", "List current selectable tracks and their media-scoped IDs."),
        Define<ControlStreamSelection, ControlGuiAction>("streams.select", "streams", "Select a track by its current ID.", true),
        Define<Empty, ControlChapterSnapshot[]>("chapters.list", "chapters", "List current chapters and their media-scoped IDs."),
        Define<ControlChoice, ControlGuiAction>("chapters.seek", "chapters", "Seek to a chapter by its current ID.", true),
        Define<Empty, ControlPlaylistSnapshot[]>("playlist.list", "playlist", "List playlist entries and their IDs."),
        Define<ControlChoice, ControlGuiAction>("playlist.select", "playlist", "Open a current playlist entry.", true),
        Define<ControlSubtitleLoad, ControlGuiAction>("subtitles.load", "subtitles", "Load subtitles in the requested slot.", true),
        Define<Empty, ControlSubtitleSlotSnapshot[]>("subtitles.list", "subtitles", "Describe both visible subtitle slots."),
        Define<ControlSubtitlePageRequest, ControlGuiSubtitlePage>("subtitles.read", "subtitles", "Read a page of detached cue values from a slot."),
        Define<ControlEnabled, ControlGuiAction>("subtitles.set_visibility", "subtitles", "Show or hide subtitles in the selected player.", true),
        Define<ControlSubtitleCueRequest, ControlGuiAction>("subtitles.seek_to_cue", "subtitles", "Seek to a cue from the current subtitle revision.", true),
        Define<ControlSubtitleCueRequest, ControlGuiAction>("subtitles.synchronize", "subtitles", "Align a cue from the current subtitle revision with the playhead.", true),
        Define<ControlSubtitleDelay, ControlGuiAction>("subtitles.set_delay", "subtitles", "Set the selected slot's playback delay in ticks.", true),
        Define<Empty, ControlWaveformSnapshot>("waveform.get", "waveform", "Get waveform availability and current peaks."),
        Define<ControlEnabled, ControlGuiAction>("waveform.set_enabled", "waveform", "Turn waveform display on or off.", true),
        Define<Empty, ControlAbLoopSnapshot>("ab_loop.get", "ab_loop", "Read playback loop markers."),
        Define<Empty, ControlGuiAction>("ab_loop.clear", "ab_loop", "Clear playback loop markers.", true),
        Define<ControlSubtitleTranslation, ControlGuiAction>("translation.slot.set_enabled", "translation", "Set interactive translation for a slot.", true),
        Define<Empty, ControlCommandDescription[]>("commands.list", "commands", "Read the active command palette and key bindings."),
        Define<Empty, ControlAudioDeviceSnapshot>("devices.audio.list", "devices", "Read the requested and active audio output choices."),
        Define<Empty, ControlAudioDeviceSnapshot>("devices.audio.refresh", "devices", "Refresh audio output devices."),
        Define<ControlDeviceSelection, ControlAudioDeviceSnapshot>("devices.audio.select", "devices", "Select a current audio output device.", true),
        Define<Empty, ControlVideoBackendSnapshot>("backends.get", "backends", "Read requested and effective video backends."),
        Define<ControlCaptureDevices, ControlCaptureDevicesSnapshot>("capture.devices", "capture", "Read or refresh current capture devices."),
        Define<ControlCaptureFormats, ControlCaptureFormatsSnapshot>("capture.formats", "capture", "Read selectable formats for a current video capture device."),
        Define<ControlCaptureOpen, ControlPlaybackSnapshot>("capture.open", "capture", "Open a current capture device and wait for the native operation.", true),
        Define<Empty, ControlRecordingSnapshot>("recording.status", "recording", "Read the current recording state."),
        Define<ControlRecordingRequest, ControlRecordingSnapshot>("recording.start", "recording", "Start recording to the requested output path.", true),
        Define<Empty, ControlRecordingSnapshot>("recording.stop", "recording", "Stop recording and publish the completed file.", true),
        Define<ControlSnapshotRequest, ControlSnapshotResult>("snapshot.take", "snapshot", "Save an image of the current video frame.", true),
        Define<Empty, ControlGuiAbout>("app.about", "app", "Read the player version and runtime."),
        Define<Empty, ControlGuiDiagnostic>("app.diagnostics", "app", "Read detached player diagnostics."),
        Define<Empty, ControlGuiUiSnapshot>("ui.get", "ui", "Read the selected window's state."),
        Define<ControlGuiUiSet, ControlGuiUiSnapshot>("ui.set", "ui", "Set fullscreen, sidebar visibility, always-on-top or theme.", true),
        Define<ControlClipboardText, ControlGuiAction>("ui.clipboard.set", "ui", "Write text to the desktop clipboard.", true),
        Define<ControlOpenUri, ControlGuiAction>("ui.browser.open", "ui", "Open an HTTP or HTTPS URL on this desktop.", true),
        Define<ControlRevealPath, ControlGuiAction>("ui.path.reveal", "ui", "Reveal a local file or directory on this desktop.", true),
        ..ControlGuiWordsOperations.All,
        ..ControlGuiDubbingOperations.All,
        ..ControlGuiLifecycleOperations.All,
    ];

    static ControlGuiOperationSchema Define<TArguments, TResult>(string id, string family, string description, bool mutates = false)
        => new(new(id, family, description, mutates, Context: ControlOperationContext.Gui), typeof(TArguments), typeof(TResult));
}
