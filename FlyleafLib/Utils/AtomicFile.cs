using System;
using System.IO;
using System.Text;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Crash-safe text file writes: serialize into a sibling temp file, then atomically replace the target with a
/// single <see cref="File.Move(string, string, bool)"/> (a rename on the same volume). A power-loss or crash
/// mid-write therefore leaves only the throwaway <c>.tmp</c> — the real file is never left half-written, so a
/// later load can never hit a truncated JSON. Mirrors the temp+move pattern already used by
/// <see cref="MediaPlayer.Dubbing.DubbingVoiceAssignmentStore"/> and <c>SrtSubtitleWriter</c>; extracted here so
/// the three config <c>Save</c> paths (AppConfig / Config / EngineConfig) share one implementation (HC-38).
/// </summary>
public static class AtomicFile
{
    // Config files are written as UTF-8 without a BOM — that is the default of File.WriteAllText(string, string?),
    // which the three Save sites used before this helper. Keep it explicit so the staged write is byte-identical.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> via a temp-file+move so a crash can never
    /// leave a partially-written file at <paramref name="path"/>. The temp is created in the same directory (so the
    /// move stays a rename, not a cross-volume copy) and is always cleaned up. I/O exceptions are NOT swallowed —
    /// they propagate exactly as a direct write would, and the existing target is left untouched on failure (the
    /// truncation happens to the temp, never the real file), so callers keep their own try/catch.
    /// </summary>
    /// <param name="encoding">Encoding to write with; defaults to UTF-8 without BOM (matches a direct write).</param>
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, contents, encoding ?? Utf8NoBom);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            // Best-effort cleanup of the temp if the write or move failed (or a stray temp lingers); never let a
            // cleanup error mask the original exception.
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
