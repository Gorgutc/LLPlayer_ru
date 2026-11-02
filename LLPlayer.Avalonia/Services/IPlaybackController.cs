using System.ComponentModel;
using FlyleafLib.MediaPlayer;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>One subtitle cue as the UI shows it (primary/secondary sidebar, word popup context).</summary>
/// <param name="Text">Display text (translated when translation display is on, otherwise the source text).</param>
/// <param name="OriginalText">Source text snapshot, used by the sidebar's show-original choice; empty when unknown.</param>
public sealed record CueInfo(int Index, TimeSpan Start, TimeSpan End, string Text, bool IsTranslated = false, string SourceLanguage = "", string TargetLanguage = "", string OriginalText = "", SubtitleCueIdentity? Identity = null, bool IsBitmap = false, string? AssignedVoiceId = null);

/// <summary>Opaque identity of a live cue's media, subtitle source and slot. Contains no public native object.</summary>
public sealed class SubtitleCueIdentity : IEquatable<SubtitleCueIdentity>
{
    internal object Source { get; }
    internal SubtitleCueIdentity(object source) => Source = source;
    public bool Equals(SubtitleCueIdentity? other) => other != null && Equals(Source, other.Source);
    public override bool Equals(object? obj) => obj is SubtitleCueIdentity other && Equals(other);
    public override int GetHashCode() => Source.GetHashCode();
}

/// <summary>A selectable subtitle track (embedded stream or external file) with its current slot assignment.</summary>
public sealed record SubtitleTrack(string Label, bool IsExternal, bool IsPrimary, bool IsSecondary, object Source);

/// <summary>A detached audio/video choice. Token is an opaque, media-scoped adapter handle.</summary>
public sealed record PlaybackStreamTrack(string Label, bool IsSelected, object Token);

/// <summary>A detached chapter choice. Token is an opaque, media-scoped adapter handle.</summary>
public sealed record PlaybackChapter(string Title, TimeSpan Start, object Token);

/// <summary>A detached playlist choice; the adapter validates its opaque media-scoped token before opening.</summary>
public sealed record PlaybackPlaylistItem(int Index, string Title, bool IsCurrent, object Token);

/// <summary>Bitmap subtitle placed in video coordinates (<see cref="VideoWidth"/> x <see cref="VideoHeight"/>).</summary>
public sealed record SubtitleImage(BgraBitmap Bitmap, int X, int Y, int Width, int Height, int VideoWidth, int VideoHeight);

/// <summary>
/// Live bitmap-subtitle layout state: engine runtime values, not persisted preferences. They mirror the WPF
/// Position / Size page controls and reset when a media opens.
/// </summary>
public readonly record struct BitmapSubtitleRuntimeState(double PrimaryScale, double SecondaryScale, double PrimaryPosition);

/// <summary>
/// What the Avalonia UI needs from the FlyleafLib player. <see cref="FlyleafPlaybackController"/> adapts a real
/// <see cref="Player"/>; tests use a fake. All members are used on the UI thread and
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> / the events are raised on the UI thread.
/// </summary>
public interface IPlaybackController : INotifyPropertyChanged
{
    /// <summary>A media is open and can play (Flyleaf <c>CanPlay</c>).</summary>
    bool IsOpened { get; }
    bool IsPlaying { get; }
    bool IsOpening { get; }
    bool CanCancelOpen { get; }
    string? Title { get; }
    string? Url { get; }
    string? CurrentMediaPath => Url;

    /// <summary>Playback position / duration in ticks.</summary>
    long CurTime { get; }
    long Duration { get; }

    double Speed { get; set; }
    int Volume { get; set; }
    int VolumeMax { get; }
    bool Mute { get; set; }

    /// <summary>Text currently displayed for the primary (0) / secondary (1) slot ("" when none).</summary>
    bool PrimaryTranslationEnabled => false;
    bool SecondaryTranslationEnabled => false;
    Task ToggleTranslationAsync(int slot) => Task.CompletedTask;
    bool PrimaryTextTranslated => false;
    bool SecondaryTextTranslated => false;
    string PrimaryText { get; }
    string SecondaryText { get; }
    SubtitleImage? PrimaryImage { get; }
    SubtitleImage? SecondaryImage { get; }
    // Live session-only bitmap layout (engine BitmapPosition), not persisted preferences.
    double PrimaryBitmapScale { get; set; }
    double SecondaryBitmapScale { get; set; }
    double PrimaryBitmapPosition { get; set; }
    /// <summary>A subtitle track is loaded in the primary/secondary slot (Flyleaf <c>Subtitles[i].Enabled</c>).</summary>
    bool PrimarySlotEnabled { get; }
    bool SecondarySlotEnabled { get; }

    /// <summary>Index into <see cref="GetCues"/>(0) of the cue being shown, or -1.</summary>
    int PrimaryCueIndex { get; }
    /// <summary>Index into <see cref="GetCues"/>(1) of the current secondary cue, or -1.</summary>
    int SecondaryCueIndex { get; }

    /// <summary>Both slots visible (Flyleaf <c>Config.Subtitles[i].Visible</c>).</summary>
    bool SubtitlesVisible { get; set; }

    /// <summary>Raised (UI thread, coalesced) when the cue list of a slot changes.</summary>
    event Action<int>? CuesChanged;

    /// <summary>Raised (UI thread) after an open finishes: (url, isSubtitles, error or null).</summary>
    event Action<string?, bool, string?>? OpenCompleted;

    /// <summary>Raised on the UI thread when an engine/native media opening supersedes app-owned requests.</summary>
    event Action? ExternalMediaOpening;

    /// <summary>Raised (UI thread) for engine errors the user should see.</summary>
    event Action<string>? ErrorOccurred;

    IReadOnlyList<CueInfo> GetCues(int slot);
    /// <summary>Copies a live bitmap cue into owned, bounded BGRA data; stale or text cues return null.</summary>
    Task<BgraBitmap?> GetCueBitmapAsync(CueInfo cue, int slot, CancellationToken cancellationToken = default)
        => Task.FromResult<BgraBitmap?>(null);
    /// <summary>Whether this detached row still refers to the live media, slot, source and raw cue timing.</summary>
    bool IsCueCurrent(CueInfo cue, int slot);
    /// <summary>Seeks only when the detached cue still belongs to this slot's live source.</summary>
    void SeekToCue(CueInfo cue, int slot);
    /// <summary>Aligns a live cue to the current playhead by changing only its slot's runtime delay.</summary>
    Task SynchronizeSubtitleToCurrentTimeAsync(CueInfo cue, int slot, CancellationToken cancellationToken = default);
    IReadOnlyList<SubtitleTrack> GetSubtitleTracks();
    IReadOnlyList<PlaybackStreamTrack> GetAudioTracks() => [];
    IReadOnlyList<PlaybackStreamTrack> GetVideoTracks() => [];
    IReadOnlyList<PlaybackChapter> GetChapters() => [];
    IReadOnlyList<PlaybackPlaylistItem> GetPlaylistItems() => [];
    string PlaylistTitle => Title ?? "Playlist";
    int PlaylistExpectedCount => 0;
    event Action? PlaylistChanged { add { } remove { } }
    Task SelectPlaylistItemAsync(PlaybackPlaylistItem item) => Task.CompletedTask;
    Task SelectAudioTrackAsync(PlaybackStreamTrack track) => Task.CompletedTask;
    Task SelectVideoTrackAsync(PlaybackStreamTrack track) => Task.CompletedTask;
    Task SeekToChapterAsync(PlaybackChapter chapter) => Task.CompletedTask;
    void SelectSubtitleTrack(int slot, SubtitleTrack? track);

    void Open(string url);

    /// <summary>Opens media and reports this particular request's result on the UI thread, even when URLs repeat.
    /// Requests superseded before engine dispatch need not complete.</summary>
    void Open(string url, Action<string?> completed, bool onlineVideo = false);
    /// <summary>Invalidates the current app-owned open. A stale click cannot cancel an engine-native request.</summary>
    bool CancelOpen();
    void OpenSubtitles(string path);
    void Play();
    void Pause();
    void TogglePlayPause();
    void SeekTo(TimeSpan position);

    /// <summary>Seeks to a subtitle cue using its slot's delay and the engine's fractional timestamp rounding.</summary>
    void SeekToSubtitle(TimeSpan position, int slot);

    /// <summary>Routes a key to the engine's key bindings (Player.KeyDown / KeyUp). Returns true when handled.</summary>
    bool KeyDown(WpfKey key);
    bool KeyUp(WpfKey key);

    /// <summary>ISO 639-1 language of a subtitle slot (null when unknown).</summary>
    string? GetSubtitleLanguage(int slot);
    string? GetSubtitleTargetLanguage() => null;

    // ---- engine key bindings (Avalonia editor) ------------------------------------------------------------------
    // The engine owns the default list; the app persists an optional full replacement list and applies it here.

    /// <summary>Snapshot of the engine's current key bindings (editor rows), in engine config order.</summary>
    IReadOnlyList<EngineKeyBindingInfo> GetEngineKeyBindings();

    /// <summary>Replaces the engine key bindings with the given list; callers pass validated canonical entries.</summary>
    void ApplyEngineKeyBindings(IReadOnlyList<EngineKeyBindingInfo> bindings);

    // ---- F-12 A-B repeat: transient runtime state shared by the WPF app and this one ---------------------------------
    // The engine owns the truth table (both points set AND A < B => enabled; points reset when a new file opens) and
    // raises PropertyChanged for all six properties below, so the view models only mirror it.

    /// <summary>A-B repeat point A in 100ns ticks, or -1 when unset. Never persisted.</summary>
    long ABLoopA => -1;
    /// <summary>A-B repeat point B in 100ns ticks, or -1 when unset. Never persisted.</summary>
    long ABLoopB => -1;
    /// <summary>True when point A is set (drives the A marker).</summary>
    bool HasABLoopA => false;
    /// <summary>True when point B is set (drives the B marker).</summary>
    bool HasABLoopB => false;
    /// <summary>True when both points are set with A &lt; B and playback loops.</summary>
    bool ABLoopEnabled => false;
    /// <summary>True when at least one point is set (drives the seek-bar marker overlay visibility).</summary>
    bool ABLoopActive => false;

    /// <summary>Sets point A to the current position; drops B when it would no longer follow A.</summary>
    void SetABLoopStart() { }
    /// <summary>Sets point B to the current position; when A is set the position must fall after A, otherwise the
    /// engine keeps any previously accepted B and reports the rejection. With A unset, B is accepted on its own.</summary>
    void SetABLoopEnd() { }
    /// <summary>Clears both points.</summary>
    void ClearABLoop() { }
    /// <summary>Single-key cycle: no A → set A; A set / no B → set B; both set → clear.</summary>
    void ToggleABLoop() { }

    // ---- F-12 audio waveform: engine-owned peaks and the off-by-default decode switch -------------------------------

    /// <summary>Per-bucket 0..1 peaks of the currently opened file, or null while the waveform is off/not built.</summary>
    float[]? WaveformPeaks => null;
    /// <summary>True once the engine has built peaks for the current media; drives the overlay's visible state.</summary>
    bool WaveformActive => false;
    /// <summary>
    /// Master switch: while false the engine must not decode audio for the waveform. Not transient: the view model
    /// applies the persisted preference on startup and saves it when the user toggles the transport button.
    /// </summary>
    bool WaveformEnabled
    {
        get => false;
        set { }
    }
}
