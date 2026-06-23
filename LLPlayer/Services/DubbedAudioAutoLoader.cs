using System.IO;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;

namespace LLPlayer.Services;

#nullable enable

/// <summary>
/// When a local media file opens, auto-attaches a pre-rendered Russian dub track (video.ru.dub.*) that
/// sits beside it as a selectable external audio stream — it then appears under the existing
/// "Audio ▸ External" menu. Best-effort convenience: any failure is swallowed (the user can still open
/// the dub manually). Must be called on the UI thread (the ExternalAudioStreams collection is UI-bound);
/// the OpenCompleted handler that calls it already runs there.
/// </summary>
public static class DubbedAudioAutoLoader
{
    private const string PluginName = "Dubbing";

    // The configured default is FLAC, but accept any common container a user might have chosen.
    private static readonly string[] CandidateExtensions = ["flac", "m4a", "opus", "mka", "aac", "wav"];

    public static void TryAttach(Player? player, string? mediaUrl)
    {
        try
        {
            if (player?.Playlist?.Selected is not { } item)
                return;
            if (string.IsNullOrWhiteSpace(mediaUrl) || !File.Exists(mediaUrl))
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
        foreach (string ext in CandidateExtensions)
        {
            string candidate = DubbingOutputPathBuilder.BuildRussianDubPath(mediaPath, ext);
            if (DubbingOutputPathBuilder.OutputExists(candidate))
                return candidate;
        }

        return null;
    }
}
