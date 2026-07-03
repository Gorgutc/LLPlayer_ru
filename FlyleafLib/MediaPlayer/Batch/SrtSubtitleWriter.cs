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
        token.ThrowIfCancellationRequested();

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Serialize through the shared, unit-tested SubtitleExporter (NormalizeCueText drops in-cue blank lines
        // that would otherwise terminate a cue and desync the SRT parser on LLM output; InvariantCulture time
        // formatting) instead of a bespoke per-cue loop. For well-formed cues this is byte-identical on Windows.
        string content = BuildSrtContent(subtitles);

        string tempPath = Path.Combine(
            directory ?? Directory.GetCurrentDirectory(),
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (StreamWriter writer = new(stream, _encoding))
            {
                await writer.WriteAsync(content.AsMemory(), token).ConfigureAwait(false);
            }

            File.Move(tempPath, outputPath, overwrite);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    // Map the batch cues onto the shared exporter. The batch path writes translated text, whose offsets no
    // longer match the original SubStyles, so styles are dropped (null) — same as the export dialog's translated
    // path. internal for unit tests (the mapping + normalization is the whole behavioural contract here).
    internal static string BuildSrtContent(IReadOnlyList<SubtitleData> subtitles)
    {
        List<SubtitleExportLine> lines = new(subtitles.Count);
        foreach (SubtitleData sub in subtitles)
            lines.Add(new SubtitleExportLine(
                sub.StartTime,
                sub.EndTime,
                sub.DisplayText ?? sub.Text ?? string.Empty,
                Styles: null));

        return SubtitleExporter.Build(lines, SubtitleExportFormat.Srt);
    }
}
