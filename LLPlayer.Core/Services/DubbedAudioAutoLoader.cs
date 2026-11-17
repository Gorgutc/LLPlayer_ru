using FlyleafLib;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;

namespace LLPlayer.Core.Services;

/// <summary>Attaches an existing local dub through the engine's external-audio API on the supplied UI dispatcher.</summary>
public static class DubbedAudioAutoLoader
{
    public static void TryAttach(Player? player, string? mediaPath, Action<Action> dispatch)
    {
        if (player == null || string.IsNullOrWhiteSpace(mediaPath)) return;
        TryAttach(() => player.Playlist?.Selected, mediaPath, dispatch);
    }

    internal static void TryAttach(Func<PlaylistItem?> selectedItem, string mediaPath, Action<Action> dispatch)
    {
        try
        {
            dispatch(() =>
            {
                try
                {
                    if (selectedItem() is not { } item || !File.Exists(mediaPath) ||
                        (!SameLocalPath(item.Url, mediaPath) && !SameLocalPath(item.DirectUrl, mediaPath))) return;
                    string? dubPath = DubbingOutputPathBuilder.ResolveExistingRussianDubPath(mediaPath);
                    if (dubPath != null) Attach(item, dubPath);
                }
                catch { /* Convenience only: never fail an otherwise valid media open. */ }
            });
        }
        catch { /* A closing dispatcher must not fail media opening. */ }
    }

    public static ExternalAudioStream Attach(PlaylistItem item, string dubPath)
    {
        if (item.ExternalAudioStreams.FirstOrDefault(stream => SameLocalPath(stream.Url, dubPath)) is { } attached)
            return attached;
        var stream = new ExternalAudioStream { Url = dubPath, Language = Language.Russian };
        PlaylistItem.AddExternalStream(stream, item, "Dubbing");
        return stream;
    }

    static bool SameLocalPath(string? candidate, string path)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(path),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch { return false; }
    }
}
