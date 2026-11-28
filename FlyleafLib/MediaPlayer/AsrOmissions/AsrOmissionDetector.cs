using System.Linq;

#nullable enable

namespace FlyleafLib.MediaPlayer.AsrOmissions;

/// <summary>Finds additive ASR candidates without changing the primary subtitle track.</summary>
public static partial class AsrOmissionDetector
{
    private sealed record Frame(AsrOmissionChunk Chunk, Word[] A, Word[] B, Edit[] Ops);

    private sealed record Candidate(AsrOmissionKind Kind, int DonorLo, int DonorHi, long Start, long End,
        int? FollowingCue = null, long? FollowingStart = null, AsrOmissionReviewReason? Review = null,
        long? ConfirmationOffset = null);

    /// <summary>
    /// Scans complete chunks in source order. Source clocks are in milliseconds. The input is read-only and the
    /// result contains suggestions only, including separately labeled candidates requiring review.
    /// </summary>
    public static IReadOnlyList<AsrOmissionCandidate> Scan(IReadOnlyList<AsrOmissionChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(chunks);
        Frame? previous = null;
        List<AsrOmissionCandidate> result = [];
        foreach (AsrOmissionChunk chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidBaseline(chunk))
            {
                previous = null;
                continue;
            }
            Word[] a = Flatten(chunk.Baseline), b = Flatten(chunk.Donor);
            Frame frame = new(chunk, a, b, Align(a, b, legacyKeys: false, cancellationToken));
            if (b.Length > 0)
            {
                List<Candidate> suggestions = Anchored(frame, previous, cancellationToken);
                Corroborate(frame, suggestions, cancellationToken);
                foreach (Candidate candidate in suggestions.OrderBy(p => p.Start))
                {
                    string text = string.Concat(b[candidate.DonorLo..candidate.DonorHi].Select(w => w.Text)).Trim();
                    if (text.Length == 0)
                        continue;
                    long offset = chunk.OffsetMs;
                    result.Add(new(text, offset + candidate.Start, offset + candidate.End, candidate.Kind, candidate.Review,
                        candidate.FollowingCue, offset + candidate.FollowingStart, offset, candidate.DonorLo,
                        candidate.DonorHi, candidate.ConfirmationOffset));
                }
            }
            previous = frame;
        }
        return result.AsReadOnly();
    }

    internal static bool ValidBaseline(AsrOmissionChunk chunk)
    {
        if (chunk.OffsetMs < 0 || chunk.DurationMs <= 0 || chunk.OffsetMs > long.MaxValue - chunk.DurationMs)
            return false;
        long previousEnd = 0;
        foreach (AsrOmissionCue cue in chunk.Baseline)
        {
            if (cue.StartMs < previousEnd || cue.EndMs <= cue.StartMs || cue.EndMs > chunk.DurationMs)
                return false;
            previousEnd = cue.EndMs;
        }
        return true;
    }

    private static bool DonorRangeValid(Frame f, int first, int end)
    {
        if (first < 0 || end > f.B.Length || first >= end)
            return false;
        long start = f.B[first].Start, stop = f.B[end - 1].End;
        if (start < 0 || stop <= start || stop > f.Chunk.DurationMs)
            return false;
        for (int i = first; i < end; i++)
        {
            Word word = f.B[i];
            if (word.Start < start || word.End < word.Start || word.End > stop)
                return false;
        }
        return true;
    }

    private static bool DonorSupported(Frame frame, Candidate candidate)
    {
        var (coverage, pause) = WordTiming(frame.B, candidate.DonorLo, candidate.DonorHi);
        return coverage >= .60 || pause < 2000;
    }

    private static (double Coverage, long LongestPause) WordTiming(Word[] words, int first, int end)
    {
        long start = words[first].Start, stop = words[end - 1].End, right = start;
        long covered = 0, longestPause = 0;
        if (stop <= start)
            return default;
        foreach (Word word in words[first..end].OrderBy(w => w.Start))
        {
            long left = Math.Max(start, word.Start), durationEnd = Math.Min(stop, word.End);
            if (durationEnd <= Math.Max(left, right))
                continue;
            longestPause = Math.Max(longestPause, left - right);
            covered += durationEnd - Math.Max(left, right);
            right = durationEnd;
        }
        return ((double)covered / (stop - start), Math.Max(longestPause, stop - right));
    }
}
