using System.Globalization;
using FlyleafLib.MediaPlayer.Diarization;

namespace LLPlayer.Core.Services.Diarization;

static class DiarizationCueMatcher
{
    readonly record struct Boundary(TimeSpan Time, string Speaker, int Delta);
    readonly record struct Speech(TimeSpan Start, TimeSpan End, string? Speaker);

    public static string?[] Match(IReadOnlyList<DiarizationTurn> turns, IReadOnlyList<DiarizationCue> cues, CancellationToken token)
    {
        List<Speech> speech = BuildTimeline(turns, token);
        var durations = new Dictionary<string, long>(StringComparer.Ordinal);
        var labels = new string?[cues.Count];
        for (int i = 0; i < cues.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            durations.Clear();
            labels[i] = MatchCue(cues[i], speech, durations, token);
        }
        return labels;
    }

    // Sweep turn boundaries to count same-speaker overlap once and retain simultaneous speakers as ambiguity.
    static List<Speech> BuildTimeline(IReadOnlyList<DiarizationTurn> turns, CancellationToken token)
    {
        foreach (DiarizationTurn turn in turns)
        {
            token.ThrowIfCancellationRequested();
            if (turn.Start < TimeSpan.Zero || turn.End <= turn.Start || string.IsNullOrWhiteSpace(turn.SpeakerId))
                throw new InvalidDataException("The diarization engine returned an invalid speaker interval.");
        }

        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var boundaries = new List<Boundary>();
        foreach (DiarizationTurn turn in turns.OrderBy(turn => turn.Start).ThenBy(turn => turn.SpeakerId, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (!labels.TryGetValue(turn.SpeakerId, out string? label))
            {
                label = "Speaker " + (labels.Count + 1).ToString(CultureInfo.InvariantCulture);
                labels.Add(turn.SpeakerId, label);
            }
            boundaries.Add(new(turn.Start, label, 1));
            boundaries.Add(new(turn.End, label, -1));
        }

        boundaries.Sort((left, right) => left.Time.CompareTo(right.Time));
        var timeline = new List<Speech>();
        var active = new Dictionary<string, int>(StringComparer.Ordinal);
        var previous = TimeSpan.Zero;
        foreach (Boundary boundary in boundaries)
        {
            token.ThrowIfCancellationRequested();
            if (active.Count > 0 && boundary.Time > previous)
                timeline.Add(new(previous, boundary.Time, active.Count == 1 ? active.Keys.First() : null));
            active.TryGetValue(boundary.Speaker, out int count);
            count += boundary.Delta;
            if (count == 0) active.Remove(boundary.Speaker);
            else active[boundary.Speaker] = count;
            previous = boundary.Time;
        }

        return timeline;
    }

    static string? MatchCue(DiarizationCue cue, List<Speech> speech, Dictionary<string, long> durations, CancellationToken token)
    {
        if (cue.End <= cue.Start) return null;

        long total = 0;
        for (int i = FindFirstOverlap(speech, cue.Start); i < speech.Count && speech[i].Start < cue.End; i++)
        {
            token.ThrowIfCancellationRequested();
            Speech span = speech[i];
            if (span.Speaker == null) return null;
            TimeSpan start = cue.Start > span.Start ? cue.Start : span.Start;
            TimeSpan end = cue.End < span.End ? cue.End : span.End;
            long ticks = (end - start).Ticks;
            durations.TryGetValue(span.Speaker, out long duration);
            durations[span.Speaker] = duration + ticks;
            total += ticks;
        }

        // Give a label only to a strict majority of the cue's actual speech.
        foreach ((string speaker, long duration) in durations)
            if (duration > total / 2) return speaker;

        return null;
    }

    static int FindFirstOverlap(List<Speech> speech, TimeSpan start)
    {
        int low = 0, high = speech.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (speech[mid].End <= start) low = mid + 1;
            else high = mid;
        }
        return low;
    }
}
