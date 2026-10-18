using System;
using System.Collections.Generic;
using System.IO;

namespace FlyleafLib;

#nullable enable

/// <summary>
/// Zip-slip containment guard for archive extraction. An archive entry whose name climbs out with <c>..</c> (or is an
/// absolute / drive-rooted path) can, when naively joined to the destination directory, resolve OUTSIDE it and
/// overwrite arbitrary files. Squid-Box.SevenZipSharp does not sanitize entry names (its
/// <c>RemoveIllegalCharacters</c> keeps <c>..</c>), so callers must validate entries before extracting. Pure and
/// string-only, so it is unit-testable without a real archive. Used before the downloaded Whisper-engine 7z is
/// unpacked.
/// </summary>
public static class ArchivePathGuard
{
    /// <summary>
    /// True when the archive entry <paramref name="entryName"/>, resolved relative to <paramref name="baseDirectory"/>,
    /// stays inside that base directory (so extracting it cannot write outside). An entry that is absolute,
    /// drive-rooted, or climbs out with <c>..</c> returns <c>false</c>. Comparison is case-insensitive (Windows FS).
    /// </summary>
    public static bool IsWithinDirectory(string baseDirectory, string entryName)
    {
        if (string.IsNullOrEmpty(baseDirectory))
            throw new ArgumentException("Base directory must be provided.", nameof(baseDirectory));

        // An empty entry name resolves to the base directory itself — nothing is written outside, so treat as contained.
        if (string.IsNullOrEmpty(entryName))
            return true;

#if !WINDOWS
        // Archive entries may use Windows separators / drive-qualified names: interpret them as Windows would ('\\' is
        // a separator, "X:..." is rooted) so an entry that would escape on Windows is refused on every platform.
        entryName = entryName.Replace('\\', '/');
        if (entryName.Length >= 2 && entryName[1] == ':' && char.IsAsciiLetter(entryName[0]))
            return false;
#endif

        string baseFull = Path.GetFullPath(baseDirectory);
        string baseWithSep = baseFull.EndsWith(Path.DirectorySeparatorChar)
            ? baseFull
            : baseFull + Path.DirectorySeparatorChar;

        // Path.Combine returns the second argument verbatim when it is rooted (absolute or drive-relative), so an
        // absolute/rooted entry escapes here and is rejected by the containment check below.
        string candidate = Path.GetFullPath(Path.Combine(baseFull, entryName));

#if WINDOWS
        return candidate.Equals(baseFull, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase);
#else
        // Case-sensitive file systems: "../foo" next to base "Foo" is a different directory, so compare ordinally.
        return candidate.Equals(baseFull, StringComparison.Ordinal)
            || candidate.StartsWith(baseWithSep, StringComparison.Ordinal);
#endif
    }

    /// <summary>
    /// Throws <see cref="IOException"/> if ANY entry in <paramref name="entryNames"/> would escape
    /// <paramref name="baseDirectory"/> (zip-slip). Call before extraction so a malicious archive is refused as a
    /// whole, leaving nothing written to disk.
    /// </summary>
    public static void ValidateEntries(IEnumerable<string> entryNames, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(entryNames);

        foreach (string entry in entryNames)
        {
            if (!IsWithinDirectory(baseDirectory, entry))
                throw new IOException(
                    $"Archive entry '{entry}' resolves outside the extraction directory (possible zip-slip); extraction refused.");
        }
    }
}
