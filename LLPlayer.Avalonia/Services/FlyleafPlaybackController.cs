using System.ComponentModel;
using System.Runtime.CompilerServices;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Services;

/// <summary>
/// <see cref="IPlaybackController"/> over a real FlyleafLib <see cref="Player"/>. Engine property changes arrive on
/// arbitrary threads and are re-raised on the UI thread (through <see cref="IUIDispatcher"/>); cross-thread engine
/// collections are read through <see cref="CollectionSyncBridge"/>.
/// </summary>
public sealed class FlyleafPlaybackController : IPlaybackController, IDisposable
{
    readonly Player player;
    readonly IUIDispatcher? ui;
    readonly CollectionSyncBridge bridge;
    readonly IDisposable?[] cueWatchers = new IDisposable?[2];
    readonly PropertyChangedEventHandler playerChanged, audioChanged, primaryDataChanged, secondaryDataChanged, primaryManagerChanged, subConfigChanged;
    bool disposed;

    public FlyleafPlaybackController(Player player, IUIDispatcher? ui, CollectionSyncBridge bridge)
    {
        this.player = player;
        this.ui = ui;
        this.bridge = bridge;

        playerChanged = (_, e) => OnPlayerChanged(e.PropertyName);
        audioChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(FlyleafLib.MediaPlayer.Audio.Volume) or nameof(FlyleafLib.MediaPlayer.Audio.Mute))
                Raise(e.PropertyName);
        };
        primaryDataChanged = (_, e) => OnSubsDataChanged(0, e.PropertyName);
        secondaryDataChanged = (_, e) => OnSubsDataChanged(1, e.PropertyName);
        primaryManagerChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(SubManager.CurrentIndex))
                Raise(nameof(PrimaryCueIndex));
        };
        subConfigChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(Config.SubConfig.Visible))
                Raise(nameof(SubtitlesVisible));
        };

        player.PropertyChanged += playerChanged;
        player.Audio.PropertyChanged += audioChanged;
        player.Subtitles[0].Data.PropertyChanged += primaryDataChanged;
        player.Subtitles[1].Data.PropertyChanged += secondaryDataChanged;
        player.SubtitlesManager[0].PropertyChanged += primaryManagerChanged;
        player.Config.Subtitles[0].PropertyChanged += subConfigChanged;
        player.OpenCompleted += OnOpenCompleted;
        player.KnownErrorOccurred += OnKnownError;
        player.UnknownErrorOccurred += OnUnknownError;

        for (int i = 0; i < cueWatchers.Length; i++)
        {
            int slot = i;
            cueWatchers[i] = bridge.Watch(player.SubtitlesManager[slot].Subs, () => CuesChanged?.Invoke(slot));
        }
    }

    public Player Player => player;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<int>? CuesChanged;
    public event Action<string?, bool, string?>? OpenCompleted;
    public event Action<string>? ErrorOccurred;

    public bool IsOpened => player.CanPlay;
    public bool IsPlaying => player.Status == Status.Playing;
    public bool IsOpening => player.Status == Status.Opening;
    public string? Title
    {
        get
        {
            string? title = player.Playlist?.Selected?.Title;
            if (!string.IsNullOrWhiteSpace(title))
                return title;
            string? url = Url;
            return url == null ? null : Path.GetFileName(url);
        }
    }
    public string? Url => player.Playlist?.Url;
    public long CurTime => player.CurTime;
    public long Duration => player.Duration;

    public double Speed
    {
        get => player.Speed;
        set => player.Speed = value;
    }

    public int VolumeMax => player.Config.Audio.VolumeMax;

    public int Volume
    {
        get => player.Audio.Volume;
        set => player.Audio.Volume = Math.Clamp(value, 0, VolumeMax);
    }

    public bool Mute
    {
        get => player.Audio.Mute;
        set => player.Audio.Mute = value;
    }

    public string PrimaryText => player.Subtitles[0].Data.Text ?? "";
    public string SecondaryText => player.Subtitles[1].Data.Text ?? "";
    public SubtitleImage? PrimaryImage => ToImage(0);
    public SubtitleImage? SecondaryImage => ToImage(1);
    public int PrimaryCueIndex => player.SubtitlesManager[0].CurrentIndex;

    public bool SubtitlesVisible
    {
        get => player.Config.Subtitles[0].Visible;
        set
        {
            player.Config.Subtitles[0].Visible = value;
            player.Config.Subtitles[1].Visible = value;
        }
    }

    public IReadOnlyList<CueInfo> GetCues(int slot)
    {
        List<SubtitleData> subs = bridge.Snapshot(player.SubtitlesManager[slot].Subs);
        List<CueInfo> cues = new(subs.Count);
        foreach (SubtitleData s in subs)
            cues.Add(new CueInfo(s.Index, s.StartTime, s.EndTime, s.DisplayText ?? (s.IsBitmap ? "[image]" : "")));
        return cues;
    }

    public IReadOnlyList<SubtitleTrack> GetSubtitleTracks()
    {
        List<SubtitleTrack> tracks = [];

        if (player.Subtitles.Streams is { } embedded)
        {
            foreach (SubtitlesStream s in bridge.Snapshot(embedded))
            {
                string label = $"#{s.StreamIndex} {LanguageName(s.Language)}{(string.IsNullOrWhiteSpace(s.Title) ? "" : " · " + s.Title)}{(s.IsBitmap ? " (image)" : "")}";
                tracks.Add(new SubtitleTrack(label, false, s.GetSubEnabled(0), s.GetSubEnabled(1), s));
            }
        }

        if (player.Playlist?.Selected?.ExternalSubtitlesStreamsAll is { } external)
        {
            foreach (ExternalSubtitlesStream s in bridge.Snapshot(external))
            {
                string name = string.IsNullOrWhiteSpace(s.Title) ? Path.GetFileName(s.Url ?? "") : s.Title;
                string lang = s.Language == null || s.Language == Language.Unknown ? "" : $" ({LanguageName(s.Language)})";
                tracks.Add(new SubtitleTrack(name + lang, true, s.GetSubEnabled(0), s.GetSubEnabled(1), s));
            }
        }

        return tracks;
    }

    public void SelectSubtitleTrack(int slot, SubtitleTrack? track)
    {
        string subIndex = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (track == null)
        {
            player.Commands.SubtitlesOff.Execute(subIndex);
            return;
        }

        ValueTuple<object, object, object> arg = (subIndex, track.Source, SelectSubMethod.Original);
        player.Commands.OpenSubtitles.Execute(arg);
    }

    public void Open(string url) => player.OpenAsync(url);
    public void OpenSubtitles(string path) => player.OpenAsync(path);
    public void Play() => player.Play();
    public void Pause() => player.Pause();
    public void TogglePlayPause() => player.TogglePlayPause();

    public void SeekTo(TimeSpan position)
    {
        long ms = Math.Max(0, (long)position.TotalMilliseconds);
        player.SeekAccurate((int)Math.Min(ms, int.MaxValue));
    }

    public bool KeyDown(WpfKey key) => Player.KeyDown(player, key);
    public bool KeyUp(WpfKey key) => Player.KeyUp(player, key);

    public string? GetSubtitleLanguage(int slot)
    {
        Language? lang = player.SubtitlesManager[slot].Language;
        return lang == null || lang == Language.Unknown ? null : lang.ISO6391;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        foreach (IDisposable? w in cueWatchers)
            w?.Dispose();

        player.PropertyChanged -= playerChanged;
        player.Audio.PropertyChanged -= audioChanged;
        player.Subtitles[0].Data.PropertyChanged -= primaryDataChanged;
        player.Subtitles[1].Data.PropertyChanged -= secondaryDataChanged;
        player.SubtitlesManager[0].PropertyChanged -= primaryManagerChanged;
        player.Config.Subtitles[0].PropertyChanged -= subConfigChanged;
        player.OpenCompleted -= OnOpenCompleted;
        player.KnownErrorOccurred -= OnKnownError;
        player.UnknownErrorOccurred -= OnUnknownError;
    }

    static string LanguageName(Language? lang)
        => lang == null || lang == Language.Unknown ? "Unknown" : lang.TopEnglishName ?? lang.ToString();

    SubtitleImage? ToImage(int slot)
    {
        SubsBitmap? bmp = player.Subtitles[slot].Data.Bitmap;
        if (bmp?.Source == null)
            return null;

        SubManager manager = player.SubtitlesManager[slot];
        return new SubtitleImage(bmp.Source, bmp.X, bmp.Y, bmp.Width, bmp.Height, manager.Width, manager.Height);
    }

    void OnPlayerChanged(string? name)
    {
        switch (name)
        {
            case nameof(Player.CanPlay):
                Raise(nameof(IsOpened));
                break;
            case nameof(Player.Status):
                Raise(nameof(IsPlaying));
                Raise(nameof(IsOpening));
                break;
            case nameof(Player.CurTime):
                Raise(nameof(CurTime));
                break;
            case nameof(Player.Duration):
                Raise(nameof(Duration));
                break;
            case nameof(Player.Speed):
                Raise(nameof(Speed));
                break;
        }
    }

    void OnSubsDataChanged(int slot, string? name)
    {
        switch (name)
        {
            case nameof(SubsData.Text):
                Raise(slot == 0 ? nameof(PrimaryText) : nameof(SecondaryText));
                break;
            case nameof(SubsData.Bitmap):
                Raise(slot == 0 ? nameof(PrimaryImage) : nameof(SecondaryImage));
                break;
        }
    }

    void OnOpenCompleted(object? sender, OpenCompletedArgs? e)
    {
        if (e == null)
            return;

        string? url = e.Url;
        bool isSubs = e.IsSubtitles;
        string? error = e.Error;
        Post(() =>
        {
            Raise(nameof(Title));
            Raise(nameof(Url));
            Raise(nameof(IsOpened));
            OpenCompleted?.Invoke(url, isSubs, error);
        });
    }

    void OnKnownError(object? sender, KnownErrorOccurredEventArgs e)
    {
        string message = e.Message;
        Post(() => ErrorOccurred?.Invoke(message));
    }

    void OnUnknownError(object? sender, UnknownErrorOccurredEventArgs e)
    {
        string message = e.Message;
        Post(() => ErrorOccurred?.Invoke(message));
    }

    void Post(Action action)
    {
        if (ui == null || ui.CheckAccess())
            action();
        else
            ui.Post(action);
    }

    void Raise([CallerMemberName] string? name = null)
        => Post(() =>
        {
            if (!disposed)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        });
}
