using FlyleafLib.MediaFramework.MediaPlaylist;

namespace FlyleafLib.MediaPlayer;

unsafe partial class Player
{
    /// <summary>WPF Reopen semantics, synchronously inside the host's owned media mutation.</summary>
    public string OpenPlaylistItemFromStart(PlaylistItem item)
    {
        if (item == null || !Playlist.Items.Contains(item)) return "The playlist item is no longer available.";
        if (item.OpenedCounter > 0 && GetSession(item) is { } session)
        {
            session.isReopen = true;
            session.CurTime = 0;
            return Open(session).Error;
        }
        return Open(item).Error;
    }
}
