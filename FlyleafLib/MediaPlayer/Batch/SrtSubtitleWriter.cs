using System.Text;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public sealed class SrtSubtitleWriter : IBatchSubtitleWriter
{
    private readonly Encoding _encoding;

    public SrtSubtitleWriter(Encoding encoding)
    {
        _encoding = encoding;
    }

    public async Task WriteAsync(
        IReadOnlyList<SubtitleData> subtitles,
        string outputPath,
        bool overwrite,
        CancellationToken token)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory ?? Directory.GetCurrentDirectory(),
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (StreamWriter writer = new(stream, _encoding))
            {
                for (int i = 0; i < subtitles.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    SubtitleData sub = subtitles[i];
                    await writer.WriteLineAsync((i + 1).ToString());
                    await writer.WriteLineAsync($"{FormatTime(sub.StartTime)} --> {FormatTime(sub.EndTime)}");
                    await writer.WriteLineAsync(sub.DisplayText ?? sub.Text ?? string.Empty);

                    if (i != subtitles.Count - 1)
                        await writer.WriteLineAsync();
                }
            }

            File.Move(tempPath, outputPath, overwrite);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static string FormatTime(TimeSpan time)
        => string.Format("{0:00}:{1:00}:{2:00},{3:000}",
            (int)time.TotalHours,
            time.Minutes,
            time.Seconds,
            time.Milliseconds);
}
