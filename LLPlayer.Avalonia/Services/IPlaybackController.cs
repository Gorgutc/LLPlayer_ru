using System.ComponentModel;
using FlyleafLib.MediaPlayer;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>One subtitle cue as the UI shows it (primary/secondary sidebar, word popup context).</summary>
public sealed record CueInfo(int Index, TimeSpan Start, TimeSpan End, string Text, bool IsTranslated = false, string SourceLanguage = "", string TargetLanguage = "");

/// <summary>A selectable subtitle track (embedded stream or external file) with its current slot assignment.</summary>
public sealed record SubtitleTrack(string Label, bool IsExternal, bool IsPrimary, bool IsSecondary, object Source);

/// <summary>Bitmap subtitle placed in video coordinates (<see cref="VideoWidth"/> x <see cref="VideoHeight"/>).</summary>
public sealed record SubtitleImage(BgraBitmap Bitmap, int X, int Y, int Width, int Height, int VideoWidth, int VideoHeight);

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
    string? Title { get; }
    string? Url { get; }

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

    /// <summary>Index into <see cref="GetCues"/>(0) of the cue being shown, or -1.</summary>
    int PrimaryCueIndex { get; }

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
    IReadOnlyList<SubtitleTrack> GetSubtitleTracks();
    void SelectSubtitleTrack(int slot, SubtitleTrack? track);

    void Open(string url);

    /// <summary>Opens media and reports this particular request's result on the UI thread, even when URLs repeat.
    /// Requests superseded before engine dispatch need not complete.</summary>
    void Open(string url, Action<string?> completed);
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
}
