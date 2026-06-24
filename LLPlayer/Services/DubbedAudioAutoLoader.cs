using System.IO;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;
using System.Windows;

namespace LLPlayer.Services;

#nullable enable

/// <summary>
/// When a local media file opens, auto-attaches a pre-rendered Russian dub track (video.ru.dub.*) that
/// sits beside it as a selectable external audio stream — it then appears under the existing
/// "Audio ▸ External" menu. Best-effort convenience: any failure is swallowed (the user can still open
/// the dub manually). The ExternalAudioStreams collection is UI-bound, so the loader marshals itself to
/// the WPF dispatcher before touching the selected playlist item.
/// </summary>
public static class DubbedAudioAutoLoader
{
    private const string PluginName = "Dubbing";

    public static void TryAttach(Player? player, string? mediaUrl)
    {
        try
        {
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                _ = dispatcher.BeginInvoke(new Action(() => TryAttach(player, mediaUrl)));
                return;
            }

            if (player?.Playlist?.Selected is not { } item)
                return;
            if (string.IsNullOrWhiteSpace(mediaUrl) || !File.Exists(mediaUrl))
                return;
            if (!IsSelectedMedia(item, mediaUrl))
                return;

            string? dubPath = ResolveDubPath(mediaUrl);
            if (dubPath is null)
                return;

            // Don't add it twice (e.g. on re-open).
            foreach (ExternalAudioStream existing in item.ExternalAudioStreams)
            {
                if (string.Equals(existing.Url, dubPath, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            ExternalAudioStream dub = new()
            {
                Url = dubPath,
                Language = Language.Russian
            };

            PlaylistItem.AddExternalStream(dub, item, PluginName);
        }
        catch
        {
            // Convenience only — never let auto-load break opening a video.
        }
    }

    /// <summary>First existing "video.ru.dub.&lt;ext&gt;" beside the media, or null.</summary>
    public static string? ResolveDubPath(string mediaPath)
    {
        return DubbingOutputPathBuilder.ResolveExistingRussianDubPath(mediaPath);
    }

    private static bool IsSelectedMedia(PlaylistItem item, string mediaPath)
    {
        return SameLocalPath(item.Url, mediaPath)
            || SameLocalPath(item.DirectUrl, mediaPath);
    }

    private static bool SameLocalPath(string? candidate, string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(candidate),
                Path.GetFullPath(mediaPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(candidate, mediaPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
