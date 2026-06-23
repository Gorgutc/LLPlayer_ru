namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Builds the dubbed-audio output path beside the source video, mirroring
/// <see cref="FlyleafLib.MediaPlayer.Batch.SubtitleOutputPathBuilder"/> for subtitles. Default
/// container is FLAC (no AAC encoder priming → preserves A/V sync; see dubbing-spec §4.1).
/// </summary>
public static class DubbingOutputPathBuilder
{
    public const string DefaultExtension = "flac";

    /// <summary>e.g. "C:\videos\movie.mkv" → "C:\videos\movie.ru.dub.flac".</summary>
    public static string BuildRussianDubPath(string mediaPath, string extension = DefaultExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        string ext = NormalizeExtension(extension);
        string? directory = Path.GetDirectoryName(mediaPath);
        string fileName = Path.GetFileNameWithoutExtension(mediaPath);

        if (string.IsNullOrWhiteSpace(directory))
            directory = Directory.GetCurrentDirectory();

        return Path.Combine(directory, $"{fileName}.ru.dub.{ext}");
    }

    /// <summary>True when a usable dub already exists for the media (its output is present and non-empty).</summary>
    public static bool DubExists(string mediaPath, string extension = DefaultExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        return OutputExists(BuildRussianDubPath(mediaPath, extension));
    }

    /// <summary>True when the given OUTPUT path exists and is non-empty. Fail-safe: I/O errors → false.</summary>
    public static bool OutputExists(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        try
        {
            FileInfo info = new(outputPath);
            return info.Exists && info.Length > 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return DefaultExtension;

        return extension.TrimStart('.').Trim().ToLowerInvariant();
    }
}
