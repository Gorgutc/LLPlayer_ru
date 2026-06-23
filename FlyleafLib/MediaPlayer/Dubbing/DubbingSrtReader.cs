using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FlyleafLib.MediaPlayer.Dubbing;

#nullable enable

/// <summary>
/// Minimal SRT reader for the "dub from an existing .ru.srt" path (re-run with dubbing enabled when the
/// translation already exists — no re-ASR/translate). Pure + unit-tested. The SRT text is already the
/// Russian translation, so it is loaded into <see cref="SubtitleData.Text"/> (DubbingRenderer reads
/// TranslatedText ?? Text).
/// </summary>
public static partial class DubbingSrtReader
{
    [GeneratedRegex(@"(\d{1,2}):(\d{2}):(\d{2})[,\.](\d{1,3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,\.](\d{1,3})")]
    private static partial Regex TimeLineRegex { get; }

    public static List<SubtitleData> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // File.ReadAllText auto-detects a UTF-8 BOM (the batch writer can emit one).
        return Parse(File.ReadAllText(path));
    }

    public static List<SubtitleData> Parse(string content)
    {
        List<SubtitleData> result = [];
        if (string.IsNullOrWhiteSpace(content))
            return result;

        // Normalise newlines and split into blocks on blank lines.
        string normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] blocks = normalized.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries);

        int index = 0;
        foreach (string block in blocks)
        {
            string[] lines = block.Split('\n');
            int timeLine = -1;
            Match? match = null;

            for (int i = 0; i < lines.Length; i++)
            {
                Match m = TimeLineRegex.Match(lines[i]);
                if (m.Success)
                {
                    timeLine = i;
                    match = m;
                    break;
                }
            }

            if (timeLine < 0 || match is null)
                continue;

            TimeSpan start = ToTimeSpan(match, 1);
            TimeSpan end = ToTimeSpan(match, 5);

            string text = string.Join('\n', lines[(timeLine + 1)..]).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            result.Add(new SubtitleData
            {
                Index = index++,
                Text = text,
                StartTime = start,
                EndTime = end
            });
        }

        return result;
    }

    private static TimeSpan ToTimeSpan(Match m, int groupOffset)
    {
        int h = int.Parse(m.Groups[groupOffset].Value);
        int min = int.Parse(m.Groups[groupOffset + 1].Value);
        int sec = int.Parse(m.Groups[groupOffset + 2].Value);
        int ms = int.Parse(m.Groups[groupOffset + 3].Value.PadRight(3, '0')[..3]);
        return new TimeSpan(0, h, min, sec, ms);
    }
}
