using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlyleafLib.MediaPlayer;

#nullable enable

/// <summary>A cue's stable ordinal within one loaded SubRip revision.</summary>
public sealed record SrtTimingCue(int Id, TimeSpan Start, TimeSpan End, string Text);

/// <summary>A strict UTF-8 SubRip snapshot. Rewriting preserves all text outside the timestamp tokens.</summary>
public sealed class SrtTimingDocument
{
    private const string Timestamp = "[0-9]{2,9}:[0-5][0-9]:[0-5][0-9],[0-9]{3}";
    private static readonly Regex TimeLine = new(
        $@"^[ \t]*(?<start>{Timestamp})[ \t]*-->[ \t]*(?<end>{Timestamp})(?:[ \t]+(?:[XY][12]:-?[0-9]+))*[ \t]*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private readonly string _source;
    private readonly ImmutableArray<TimestampSpans> _timingSpans;

    private SrtTimingDocument(string source, string sha256, ImmutableArray<SrtTimingCue> cues,
        ImmutableArray<TimestampSpans> timingSpans)
    {
        _source = source;
        Sha256 = sha256;
        Cues = cues;
        _timingSpans = timingSpans;
    }

    public ImmutableArray<SrtTimingCue> Cues { get; }
    public string Sha256 { get; }

    public static SrtTimingDocument Parse(ReadOnlySpan<byte> utf8)
    {
        string source;
        try { source = Utf8.GetString(utf8); }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("Alignment requires an SRT encoded as UTF-8.", error);
        }

        if (source.IndexOf('\0') >= 0)
            throw new InvalidDataException("Alignment requires an SRT encoded as UTF-8.");

        var cues = ImmutableArray.CreateBuilder<SrtTimingCue>();
        var spans = ImmutableArray.CreateBuilder<TimestampSpans>();
        int offset = source.Length > 0 && source[0] == '\ufeff' ? 1 : 0;
        while (offset < source.Length)
        {
            ReadOnlySpan<char> sequence = ReadLine(source, ref offset).Trim();
            if (sequence.IsEmpty)
                continue;

            if (sequence.IndexOfAnyExceptInRange('0', '9') >= 0)
                throw InvalidCue(cues.Count);

            int lineStart = offset;
            Match timing = TimeLine.Match(ReadLine(source, ref offset).ToString());
            Group start = timing.Groups["start"], end = timing.Groups["end"];
            if (!timing.Success || !TryParseTime(start.ValueSpan, out TimeSpan startTime)
                || !TryParseTime(end.ValueSpan, out TimeSpan endTime) || endTime <= startTime)
                throw InvalidCue(cues.Count);

            var cueText = new StringBuilder();
            while (offset < source.Length)
            {
                ReadOnlySpan<char> line = ReadLine(source, ref offset);
                if (line.IsWhiteSpace())
                    break;
                if (cueText.Length > 0)
                    cueText.Append('\n');
                cueText.Append(line);
            }
            if (cueText.Length == 0)
                throw InvalidCue(cues.Count);

            cues.Add(new(cues.Count, startTime, endTime, cueText.ToString()));
            spans.Add(new(lineStart + start.Index, start.Length, lineStart + end.Index, end.Length));
        }

        if (cues.Count == 0)
            throw new InvalidDataException("The SRT contains no cues to align.");

        return new(source, Convert.ToHexString(SHA256.HashData(utf8)), cues.ToImmutable(),
            spans.ToImmutable());
    }

    public byte[] Rewrite(IReadOnlyDictionary<int, (TimeSpan Start, TimeSpan End)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (int id in changes.Keys)
            if ((uint)id >= (uint)Cues.Length)
                throw new ArgumentException("A timing change refers to an unknown cue.", nameof(changes));

        var output = new StringBuilder(_source.Length);
        int previous = 0;
        for (int id = 0; id < _timingSpans.Length; id++)
        {
            if (!changes.TryGetValue(id, out var change))
                continue;

            long start = change.Start.Ticks / TimeSpan.TicksPerMillisecond;
            long end = change.End.Ticks / TimeSpan.TicksPerMillisecond;
            if (change.Start < TimeSpan.Zero || end <= start)
                throw new ArgumentException($"Cue {id + 1} needs a positive millisecond duration.", nameof(changes));

            TimestampSpans spans = _timingSpans[id];
            output.Append(_source.AsSpan(previous, spans.Start - previous));
            AppendTime(output, start, spans.StartLength - 10);
            int startTokenEnd = spans.Start + spans.StartLength;
            output.Append(_source.AsSpan(startTokenEnd, spans.End - startTokenEnd));
            AppendTime(output, end, spans.EndLength - 10);
            previous = spans.End + spans.EndLength;
        }
        output.Append(_source.AsSpan(previous));
        return Utf8.GetBytes(output.ToString());
    }

    internal static bool IsSupportedEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Contains((byte)0))
            return false;
        try { return Utf8.GetCharCount(bytes) > 0; }
        catch (DecoderFallbackException) { return false; }
    }

    private static ReadOnlySpan<char> ReadLine(string source, ref int offset)
    {
        ReadOnlySpan<char> rest = source.AsSpan(offset);
        int newline = rest.IndexOf('\n');
        if (newline < 0)
        {
            offset = source.Length;
            return rest;
        }
        offset += newline + 1;
        return newline > 0 && rest[newline - 1] == '\r' ? rest[..(newline - 1)] : rest[..newline];
    }

    private static bool TryParseTime(ReadOnlySpan<char> timestamp, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        int separator = timestamp.IndexOf(':');
        if (!int.TryParse(timestamp[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            || hours > TimeSpan.MaxValue.TotalHours)
            return false;

        int minutes = int.Parse(timestamp.Slice(separator + 1, 2), CultureInfo.InvariantCulture);
        int seconds = int.Parse(timestamp.Slice(separator + 4, 2), CultureInfo.InvariantCulture);
        int millis = int.Parse(timestamp.Slice(separator + 7, 3), CultureInfo.InvariantCulture);
        long totalMilliseconds = (hours * 3600L + minutes * 60L + seconds) * 1000 + millis;
        if (totalMilliseconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond)
            return false;

        result = TimeSpan.FromTicks(totalMilliseconds * TimeSpan.TicksPerMillisecond);
        return true;
    }

    private static void AppendTime(StringBuilder builder, long milliseconds, int hourDigits)
    {
        string format = "D" + hourDigits.ToString(CultureInfo.InvariantCulture);
        builder.Append((milliseconds / 3_600_000).ToString(format, CultureInfo.InvariantCulture));
        builder.Append(CultureInfo.InvariantCulture,
            $":{milliseconds / 60_000 % 60:D2}:{milliseconds / 1_000 % 60:D2},{milliseconds % 1_000:D3}");
    }

    private static InvalidDataException InvalidCue(int ordinal) =>
        new($"Cue {ordinal + 1} is not in the supported SubRip format.");

    private readonly record struct TimestampSpans(int Start, int StartLength, int End, int EndLength);
}
