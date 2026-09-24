using System.ComponentModel;
using FlyleafLib.MediaPlayer;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>One subtitle cue as the UI shows it (primary/secondary sidebar, word popup context).</summary>
public sealed record CueInfo(int Index, TimeSpan Start, TimeSpan End, string Text);

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

    /// <summary>Raised (UI thread) for engine errors the user should see.</summary>
    event Action<string>? ErrorOccurred;

    IReadOnlyList<CueInfo> GetCues(int slot);
    IReadOnlyList<SubtitleTrack> GetSubtitleTracks();
    void SelectSubtitleTrack(int slot, SubtitleTrack? track);

    void Open(string url);
    void OpenSubtitles(string path);
    void Play();
    void Pause();
    void TogglePlayPause();
    void SeekTo(TimeSpan position);

    /// <summary>Routes a key to the engine's key bindings (Player.KeyDown / KeyUp). Returns true when handled.</summary>
    bool KeyDown(WpfKey key);
    bool KeyUp(WpfKey key);

    /// <summary>ISO 639-1 language of a subtitle slot (null when unknown).</summary>
    string? GetSubtitleLanguage(int slot);
}
