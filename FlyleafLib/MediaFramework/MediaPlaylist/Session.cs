namespace FlyleafLib.MediaFramework.MediaPlaylist;

public class Session
{
    public string   Url                     { get; set; }
    public int      PlaylistItem            { get; set; } = -1;

    public int      ExternalAudioStream     { get; set; } = -1;
    public int      ExternalVideoStream     { get; set; } = -1;
    public string   ExternalSubtitlesUrl    { get; set; }

    public int      AudioStream             { get; set; } = -1;
    public int      VideoStream             { get; set; } = -1;
    public int      SubtitlesStream         { get; set; } = -1;

    public long     CurTime                 { get; set; }

    public long     AudioDelay              { get; set; }
    public long     SubtitlesDelay          { get; set; }

    internal bool   isReopen; // temp fix for opening existing playlist item as a new session (should not re-initialize - is like switch)
}
