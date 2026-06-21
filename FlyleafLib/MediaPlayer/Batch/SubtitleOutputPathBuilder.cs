namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public static class SubtitleOutputPathBuilder
{
    public static string BuildRussianSrtPath(string mediaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        string? directory = Path.GetDirectoryName(mediaPath);
        string fileName = Path.GetFileNameWithoutExtension(mediaPath);

        if (string.IsNullOrWhiteSpace(directory))
            directory = Directory.GetCurrentDirectory();

        return Path.Combine(directory, fileName + ".ru.srt");
    }

    // True when a usable translation already exists for the media (its .ru.srt output is present
    // and non-empty). Takes the MEDIA path and derives the output path.
    public static bool TranslationExists(string mediaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        return OutputExists(BuildRussianSrtPath(mediaPath));
    }

    // True when the given OUTPUT (.ru.srt) path exists and is non-empty. Use this when you already
    // hold the built output path (e.g. BatchSubtitleJob.OutputPath) so the extension is not appended
    // twice. Mirrors the run-time collision check so scan-time and run-time never diverge. Fail-safe:
    // any I/O or access error returns false (treat as "needs translation") rather than throwing.
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
}
