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
}
