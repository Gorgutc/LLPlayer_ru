using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaPlayer;

#nullable enable

public enum SubtitleExportFormat
{
    /// <summary>SubRip (.srt) — indexed cues, comma millisecond separator, inline italic tags.</summary>
    Srt,
    /// <summary>WebVTT (.vtt) — "WEBVTT" header, dot millisecond separator, inline italic tags.</summary>
    Vtt,
    /// <summary>Plain transcript (.txt) — cue text only, no timings, no markup.</summary>
    Txt
}

/// <summary>
/// One cue to export. <see cref="Styles"/> (if any) index into <see cref="Text"/> and are only meaningful for the
/// original (untranslated) text — pass <c>null</c> when exporting translated text, whose offsets no longer match.
/// </summary>
public sealed record SubtitleExportLine(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    IReadOnlyList<SubStyle>? Styles = null);

/// <summary>
/// Pure subtitle serializer (no file/UI I/O), shared by the export dialog. Lives next to <see cref="SubtitleData"/>
/// and <see cref="SubStyle"/> so it can be unit-tested in FlyleafLibTests.
/// </summary>
public static class SubtitleExporter
{
    private const string NL = "\r\n";

    public static string Extension(SubtitleExportFormat format) => format switch
    {
        SubtitleExportFormat.Srt => ".srt",
        SubtitleExportFormat.Vtt => ".vtt",
        SubtitleExportFormat.Txt => ".txt",
        _ => ".srt"
    };

    public static string Build(IReadOnlyList<SubtitleExportLine> lines, SubtitleExportFormat format) => format switch
    {
        SubtitleExportFormat.Srt => BuildSrt(lines),
        SubtitleExportFormat.Vtt => BuildVtt(lines),
        SubtitleExportFormat.Txt => BuildTxt(lines),
        _ => BuildSrt(lines)
    };

    private static string BuildSrt(IReadOnlyList<SubtitleExportLine> lines)
    {
        StringBuilder sb = new();

        for (int i = 0; i < lines.Count; i++)
        {
            SubtitleExportLine line = lines[i];

            sb.Append(i + 1).Append(NL);
            sb.Append(FormatTime(line.Start, ',')).Append(" --> ").Append(FormatTime(line.End, ',')).Append(NL);
            sb.Append(NormalizeCueText(RenderItalic(line.Text, line.Styles))).Append(NL);

            // Blank separator line between cues, but not a trailing one after the last cue.
            if (i != lines.Count - 1)
                sb.Append(NL);
        }

        return sb.ToString();
    }

    private static string BuildVtt(IReadOnlyList<SubtitleExportLine> lines)
    {
        StringBuilder sb = new();
        sb.Append("WEBVTT").Append(NL).Append(NL);

        for (int i = 0; i < lines.Count; i++)
        {
            SubtitleExportLine line = lines[i];

            sb.Append(FormatTime(line.Start, '.')).Append(" --> ").Append(FormatTime(line.End, '.')).Append(NL);
            sb.Append(NormalizeCueText(RenderItalic(line.Text, line.Styles))).Append(NL);

            if (i != lines.Count - 1)
                sb.Append(NL);
        }

        return sb.ToString();
    }

    private static string BuildTxt(IReadOnlyList<SubtitleExportLine> lines)
    {
        StringBuilder sb = new();

        for (int i = 0; i < lines.Count; i++)
        {
            // Plain transcript: cue text only, no timings, no markup.
            sb.Append(NormalizeNewlines(lines[i].Text));

            if (i != lines.Count - 1)
                sb.Append(NL);
        }

        return sb.ToString();
    }

    private static string FormatTime(TimeSpan time, char msSeparator)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:00}:{1:00}:{2:00}{3}{4:000}",
            (int)time.TotalHours,
            time.Minutes,
            time.Seconds,
            msSeparator,
            time.Milliseconds);
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", NL);

    // Cue text for the timed formats (SRT/VTT): normalize newlines to CRLF and drop blank lines. A blank line
    // inside a cue would terminate the cue in SRT/VTT and desync the parser, so collapse runs of newlines and
    // trim leading/trailing ones. (Plain-text export keeps NormalizeNewlines and is unaffected by cue framing.)
    private static string NormalizeCueText(string text) =>
        string.Join(NL, NormalizeNewlines(text).Split(NL).Where(line => line.Length > 0));

    /// <summary>
    /// Wraps each ITALIC <see cref="SubStyle"/> range with <c>&lt;i&gt;…&lt;/i&gt;</c> (the tag SRT and WebVTT both
    /// understand). The text is otherwise returned unchanged — bold/underline/colour/font styles are not emitted.
    /// Ranges index into <paramref name="text"/>; they are clamped to its bounds. Inserts back-to-front so earlier
    /// offsets are not shifted. Only ITALIC is handled, so disjoint italic spans (the realistic case) stay well
    /// formed; the rare overlapping-italic case still renders italic.
    /// </summary>
    internal static string RenderItalic(string text, IReadOnlyList<SubStyle>? styles)
    {
        if (styles == null || styles.Count == 0 || string.IsNullOrEmpty(text))
            return text;

        List<(int from, int to)> ranges = styles
            .Where(s => s.style == SubStyles.ITALIC && s.len > 0 && s.from >= 0 && s.from < text.Length)
            .Select(s => (from: s.from, to: Math.Min(text.Length, s.from + s.len)))
            .Where(r => r.to > r.from)
            .OrderByDescending(r => r.from)
            .ToList();

        if (ranges.Count == 0)
            return text;

        StringBuilder sb = new(text);
        foreach ((int from, int to) in ranges)
        {
            sb.Insert(to, "</i>");
            sb.Insert(from, "<i>");
        }

        return sb.ToString();
    }
}
