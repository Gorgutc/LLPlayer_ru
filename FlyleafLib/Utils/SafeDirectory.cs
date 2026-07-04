using System;
using System.IO;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Best-effort recursive directory deletion that never throws for the expected filesystem failures. Extracted so
/// teardown/Dispose paths — where an escaping exception can surface on a background thread's <c>finally</c> and take
/// the whole process down — can clean up temp folders safely. A locked file, a vanished directory, or a permissions
/// issue is swallowed (and reported <c>false</c>) instead of propagating. Used by the YoutubeDL plugin's
/// <c>DisposeInternal</c>, whose unguarded <see cref="Directory.Delete(string, bool)"/> previously threw out of a
/// background <c>PlayThread</c>'s <c>finally</c> on the stop/switch-after-YouTube path.
/// </summary>
public static class SafeDirectory
{
    /// <summary>
    /// Deletes <paramref name="path"/> and its contents if it exists. Returns <c>true</c> when the directory no
    /// longer exists after the call (deleted, or was never there / <paramref name="path"/> was null-or-blank);
    /// returns <c>false</c> when a deletion was attempted but failed (the directory is left as-is). Never throws for
    /// the expected filesystem failures (<see cref="IOException"/> — which covers
    /// <see cref="DirectoryNotFoundException"/> from a delete race — and <see cref="UnauthorizedAccessException"/>),
    /// so a caller in a <c>finally</c>/Dispose can rely on it not to escape.
    /// </summary>
    public static bool TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        try
        {
            if (!Directory.Exists(path))
                return true;

            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (IOException)                 { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
