using System;
using System.Collections.Generic;
using System.IO;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Immutable identity of the media that owned a sidebar voice edit. Candidate strings are captured on the UI
/// thread without touching the filesystem; the first candidate that actually exists is resolved later by the
/// background save queue. This preserves the existing Selected.Url -> DirectUrl -> Playlist.Url fallback without
/// letting a delayed save accidentally read a different, newly-opened media item.
/// </summary>
public sealed class DubbingVoiceAssignmentMediaTarget
{
    private const char KeySeparator = '\u001f';
    private readonly string[] _candidates;
    private readonly bool _requireExistingFile;

    private DubbingVoiceAssignmentMediaTarget(IEnumerable<string?> candidates, bool requireExistingFile)
    {
        List<string> captured = [];
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                captured.Add(NormalizeCandidateAtCapture(candidate.Trim()));
        }

        _candidates = [.. captured];
        _requireExistingFile = requireExistingFile;
        QueueKey = string.Join(KeySeparator, _candidates);
    }

    public bool IsEmpty => _candidates.Length == 0;

    internal string QueueKey { get; }

    public static DubbingVoiceAssignmentMediaTarget Capture(
        string? selectedUrl,
        string? selectedDirectUrl,
        string? playlistUrl)
        => new([selectedUrl, selectedDirectUrl, playlistUrl], requireExistingFile: true);

    internal static DubbingVoiceAssignmentMediaTarget FromResolvedPath(string mediaPath)
        => new([mediaPath], requireExistingFile: false);

    internal string? ResolveLocalMediaPath()
    {
        foreach (string candidate in _candidates)
        {
            if (_requireExistingFile && !File.Exists(candidate))
                continue;

            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                return candidate;
            }
        }

        return null;
    }

    private static string NormalizeCandidateAtCapture(string candidate)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
        {
            if (!uri.IsFile)
                return candidate;

            candidate = uri.LocalPath;
        }

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch
        {
            return candidate;
        }
    }
}
