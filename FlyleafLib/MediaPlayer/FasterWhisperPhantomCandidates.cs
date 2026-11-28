using System.Text.Json;

namespace FlyleafLib.MediaPlayer;

#nullable enable

internal static class FasterWhisperPhantomCandidates
{
    private const decimal TimestampTolerance = 0.001m;
    private const decimal WordEdgeTolerance = 0.00001m;
    private const decimal MinimumTimedWordDuration = 0.000001m;

    // Native scores can authorize removal only when every JSON segment maps to the
    // same stdout cue. Absent word evidence disables v6b for that row.
    internal static IReadOnlyList<AsrPhantomCandidate>? Read(string jsonPath,
        IReadOnlyList<(string text, TimeSpan start, TimeSpan end, string language)> stdoutCues, int sampleCount)
    {
        if (sampleCount <= 0 || string.IsNullOrWhiteSpace(jsonPath))
            return null;

        try
        {
            using var stream = File.OpenRead(jsonPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowDuplicateProperties = false });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("segments", out JsonElement segments) ||
                segments.ValueKind != JsonValueKind.Array ||
                segments.GetArrayLength() != stdoutCues.Count)
                return null;

            var candidates = new AsrPhantomCandidate[stdoutCues.Count];
            var nativeIdentities = new HashSet<(string Text, int Start, int End)>();
            var stdoutIdentities = new HashSet<(string Text, TimeSpan Start, TimeSpan End)>();
            decimal duration = sampleCount / (decimal)AsrPhantomFilter.SampleRate;
            int index = 0;
            foreach (JsonElement segment in segments.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object ||
                    !segment.TryGetProperty("text", out JsonElement nativeText) ||
                    nativeText.ValueKind != JsonValueKind.String)
                    return null;

                string? text = nativeText.GetString()?.Trim();
                var cue = stdoutCues[index];
                decimal stdoutStart = cue.start.Ticks / (decimal)TimeSpan.TicksPerSecond;
                decimal stdoutEnd = cue.end.Ticks / (decimal)TimeSpan.TicksPerSecond;
                if (string.IsNullOrEmpty(text) || !string.Equals(text, cue.text?.Trim(), StringComparison.Ordinal) ||
                    stdoutStart < 0 || stdoutEnd <= stdoutStart || stdoutEnd > duration ||
                    !TryReadNumber(segment, "start", out decimal start) ||
                    !TryReadNumber(segment, "end", out decimal end) ||
                    start < 0 || end <= start || end > duration ||
                    Math.Abs(start - stdoutStart) > TimestampTolerance ||
                    Math.Abs(end - stdoutEnd) > TimestampTolerance ||
                    !TryReadNumber(segment, "no_speech_prob", out decimal score) || score is < 0 or > 1)
                    return null;

                int firstSample = (int)decimal.Floor(start * AsrPhantomFilter.SampleRate);
                int endSample = (int)decimal.Ceiling(end * AsrPhantomFilter.SampleRate);
                if (!nativeIdentities.Add((text, firstSample, endSample)) ||
                    !stdoutIdentities.Add((text, cue.start, cue.end)))
                    return null;

                candidates[index++] = new(firstSample, endSample, (double)score,
                    ReadAlignment(segment, start, end, firstSample, endSample));
            }
            return candidates;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static AsrPhantomAlignment? ReadAlignment(JsonElement segment, decimal cueStart, decimal cueEnd,
        int cueStartSample, int cueEndSample)
    {
        if (!segment.TryGetProperty("words", out JsonElement words) ||
            words.ValueKind != JsonValueKind.Array || words.GetArrayLength() == 0)
            return null;

        var timed = new List<(decimal Start, decimal End)>();
        decimal maximumProbability = 0;
        foreach (JsonElement word in words.EnumerateArray())
        {
            if (word.ValueKind != JsonValueKind.Object ||
                !TryReadNumber(word, "start", out decimal start) || !TryReadNumber(word, "end", out decimal end) ||
                !TryReadNumber(word, "probability", out decimal probability) ||
                start < cueStart - WordEdgeTolerance || end > cueEnd + WordEdgeTolerance ||
                end < start || probability is < 0 or > 1)
                return null;

            maximumProbability = Math.Max(maximumProbability, probability);
            start = Math.Max(start, cueStart);
            end = Math.Min(end, cueEnd);
            if (end - start > MinimumTimedWordDuration)
                timed.Add((start, end));
        }

        timed.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        var spans = new List<AsrPhantomVoiceInterval>();
        decimal previousEnd = cueStart;
        decimal covered = 0;
        decimal largestGap = 0;
        for (int index = 0; index < timed.Count;)
        {
            var (start, end) = timed[index++];
            while (index < timed.Count && timed[index].Start <= end)
                end = Math.Max(end, timed[index++].End);

            largestGap = Math.Max(largestGap, start - previousEnd);
            covered += end - start;
            previousEnd = end;
            // These outward integer edges preserve strict time overlap tests against
            // integer VAD edges even for word timestamps between samples.
            spans.Add(new((int)decimal.Floor(start * AsrPhantomFilter.SampleRate) - cueStartSample,
                (int)decimal.Ceiling(end * AsrPhantomFilter.SampleRate) - cueStartSample));
        }
        largestGap = Math.Max(largestGap, cueEnd - previousEnd);
        decimal duration = cueEnd - cueStart;

        // v5 uses outward segment bounds. Preserve native fractions so v6b can
        // validate acoustic coverage before choosing the nearest PCM samples.
        return new(words.GetArrayLength(), timed.Count, decimal.Round(duration, 6),
            decimal.Round(largestGap, 6), decimal.Round(covered / duration, 6), decimal.Round(maximumProbability, 7),
            spans, cueStart * AsrPhantomFilter.SampleRate - cueStartSample,
            cueEnd * AsrPhantomFilter.SampleRate - cueEndSample);
    }

    private static bool TryReadNumber(JsonElement segment, string key, out decimal number)
    {
        number = 0;
        return segment.TryGetProperty(key, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out number);
    }
}
