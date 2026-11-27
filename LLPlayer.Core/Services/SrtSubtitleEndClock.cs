using FlyleafLib.MediaPlayer;

namespace LLPlayer.Core.Services;

/// <summary>Fits the track to spoken endings when a recognizer stretches the beginning of a matched phrase.</summary>
internal static class SrtSubtitleEndClock
{
    private const int MinimumAnchors = 6;
    private const int MaximumFitAnchors = 64;
    private const int QuartileCount = 4;
    private const int ToleranceMs = 750;
    private const int ContradictionMs = 1_500;
    private const int FoldAgreementMs = 500;
    private const int QuartileBiasMs = 400;
    private const int MaximumUnsupportedGapMs = 32_000;
    private const int PlaybackEdgeMs = 300;

    internal static (SrtClockCorrection Summary, IReadOnlyList<(TimeSpan Start, TimeSpan End)> Intervals, IReadOnlySet<int> AnchorIndices)? TryCreate(
        IReadOnlyList<SrtAlignmentProposal> proposals, IReadOnlyList<SrtSpeechEndEvidence> evidence,
        Func<int, int, IReadOnlyList<(TimeSpan Start, TimeSpan End)>, bool> corroborateEdge, TimeSpan duration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (duration <= TimeSpan.Zero || evidence.Count < MinimumAnchors || evidence.Count > proposals.Count ||
            evidence.Count < proposals.Count - evidence.Count)
            return null;

        long previousId = -1;
        TimeSpan previousEnd = TimeSpan.Zero;
        List<int> conflicts = [];
        for (int i = 0; i < proposals.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            SrtAlignmentProposal proposal = proposals[i];
            SrtTimingCue cue = proposal.Cue;
            if (cue.Id <= previousId || cue.Start < previousEnd || cue.End <= cue.Start ||
                proposal.Rejection is SrtAlignmentRejection.Ambiguous or SrtAlignmentRejection.InvalidTiming)
                return null;
            previousId = cue.Id;
            previousEnd = cue.End;
            if (proposal.Rejection == SrtAlignmentRejection.ConflictingOrder)
                conflicts.Add(i);
        }

        double first = proposals[0].Cue.Start.TotalMilliseconds;
        double last = proposals[^1].Cue.End.TotalMilliseconds;
        double span = last - first;
        var anchors = new List<Anchor>(evidence.Count);
        int previousIndex = -1;
        TimeSpan previousSpeechEnd = TimeSpan.Zero;
        foreach (SrtSpeechEndEvidence item in evidence)
        {
            token.ThrowIfCancellationRequested();
            int index = item.CueIndex;
            if (index <= previousIndex || index >= proposals.Count ||
                item.Start < previousSpeechEnd || item.End <= item.Start || item.End > duration ||
                item.OnsetPrefixEnd < item.Start || item.OnsetPrefixEnd > item.End ||
                proposals[index].Rejection == SrtAlignmentRejection.ConflictingOrder)
                return null;
            previousIndex = index;
            previousSpeechEnd = item.End;
            SrtTimingCue cue = proposals[index].Cue;
            double authoredStart = cue.Start.TotalMilliseconds;
            double authoredEnd = cue.End.TotalMilliseconds;
            int quartile = Math.Min(QuartileCount - 1, (int)(((authoredStart + authoredEnd) / 2 - first) / span * QuartileCount));
            anchors.Add(new(index, quartile, authoredStart, authoredEnd,
                item.Start.TotalMilliseconds, item.OnsetPrefixEnd.TotalMilliseconds, item.End.TotalMilliseconds));
        }

        if (conflicts.Any(index => index > anchors[0].CueIndex && index < anchors[^1].CueIndex) ||
            !HasTrackSupport(anchors, proposals.Count, span))
            return null;

        double minimumPairSeparation = Math.Min(15_000, span / 4);
        if (Fit(anchors, 0, 2, minimumPairSeparation, token) is not { } even ||
            Fit(anchors, 1, 2, minimumPairSeparation, token) is not { } odd ||
            Math.Abs(even.Project(first) - odd.Project(first)) > FoldAgreementMs ||
            Math.Abs(even.Project(last) - odd.Project(last)) > FoldAgreementMs ||
            Validate(anchors, (anchor, index) =>
            {
                Clock fold = index % 2 == 0 ? odd : even;
                return (fold.Project(anchor.AuthoredStart), fold.Project(anchor.AuthoredEnd));
            }, token) == null ||
            Fit(anchors, 0, 1, minimumPairSeparation, token) is not { } clock)
            return null;

        var intervals = new (TimeSpan Start, TimeSpan End)[proposals.Count];
        var fitIndices = anchors.Select(anchor => anchor.CueIndex).ToHashSet();
        long mediaEnd = duration.Ticks / TimeSpan.TicksPerMillisecond;
        long previousProjectedEnd = 0;
        int clipped = 0;
        for (int i = 0; i < intervals.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            SrtAlignmentProposal proposal = proposals[i];
            SrtTimingCue cue = proposal.Cue;
            long start = clock.Project(cue.Start.TotalMilliseconds);
            long end = clock.Project(cue.End.TotalMilliseconds);
            // Only a literal, observed playback boundary may trim an interval.
            if (i == 0 && start is >= -PlaybackEdgeMs and < 0 &&
                anchors[0].CueIndex == 0 && anchors[0].SpeechStart <= PlaybackEdgeMs &&
                proposal.IsExactAnchor && !proposal.OneRepeatedWordElided)
            {
                start = 0;
                clipped++;
            }
            if (i == intervals.Length - 1 && end > mediaEnd && end - mediaEnd <= PlaybackEdgeMs &&
                anchors[^1].CueIndex == i && mediaEnd - anchors[^1].SpeechEnd <= PlaybackEdgeMs &&
                proposal.IsExactAnchor && !proposal.OneRepeatedWordElided)
            {
                end = mediaEnd;
                clipped++;
            }
            if (start < previousProjectedEnd || end <= start || end > mediaEnd)
                return null;
            if (!fitIndices.Contains(i) && proposal.Start is { } observedStart && proposal.End is { } observedEnd &&
                Contradicts(start - observedStart.TotalMilliseconds, end - observedEnd.TotalMilliseconds))
                return null;

            intervals[i] = (TimeSpan.FromMilliseconds(start), TimeSpan.FromMilliseconds(end));
            previousProjectedEnd = end;
        }

        TimeSpan firstAdjustment = intervals[0].Start - proposals[0].Cue.Start;
        TimeSpan lastAdjustment = intervals[^1].End - proposals[^1].Cue.End;
        if (firstAdjustment.Duration().TotalMilliseconds < ToleranceMs && lastAdjustment.Duration().TotalMilliseconds < ToleranceMs)
            return null;

        List<Anchor>? inliers = Validate(anchors, (anchor, _) =>
        {
            var (start, end) = intervals[anchor.CueIndex];
            return (start.TotalMilliseconds, end.TotalMilliseconds);
        }, token);
        if (inliers == null || !HasTrackSupport(inliers, proposals.Count, span) ||
            conflicts.Any(index => index > inliers[0].CueIndex && index < inliers[^1].CueIndex))
            return null;

        int firstAnchor = inliers[0].CueIndex;
        int lastAnchor = inliers[^1].CueIndex;
        double edgeMargin = Math.Clamp(.05 * span, 1_000, 5_000);
        token.ThrowIfCancellationRequested();
        if (firstAnchor > 0 && (inliers[0].AuthoredStart - first > edgeMargin || conflicts.Any(index => index < firstAnchor)) &&
            !corroborateEdge(0, firstAnchor - 1, intervals))
            return null;
        token.ThrowIfCancellationRequested();
        if (lastAnchor < proposals.Count - 1 && (last - inliers[^1].AuthoredEnd > edgeMargin || conflicts.Any(index => index > lastAnchor)) &&
            !corroborateEdge(lastAnchor + 1, proposals.Count - 1, intervals))
            return null;
        token.ThrowIfCancellationRequested();

        var summary = new SrtClockCorrection(inliers.Count, clock.Scale, firstAdjustment, lastAdjustment)
            { ClippedBoundaryCount = clipped };
        return (summary, intervals, inliers.Select(anchor => anchor.CueIndex).ToHashSet());
    }

    private static bool HasTrackSupport(List<Anchor> anchors, int cueCount, double span)
    {
        if (anchors.Count < MinimumAnchors || anchors.Count < cueCount - anchors.Count ||
            anchors[^1].AuthoredCenter - anchors[0].AuthoredCenter < .8 * span)
            return false;

        int quartilesSeen = 1 << anchors[0].Quartile;
        for (int i = 1; i < anchors.Count; i++)
        {
            Anchor previous = anchors[i - 1];
            Anchor current = anchors[i];
            if (current.CueIndex > previous.CueIndex + 1 && current.AuthoredCenter - previous.AuthoredCenter > MaximumUnsupportedGapMs)
                return false;
            quartilesSeen |= 1 << current.Quartile;
        }
        return quartilesSeen == (1 << QuartileCount) - 1;
    }

    private static Clock? Fit(List<Anchor> anchors, int offset, int stride, double minimumPairSeparation,
        CancellationToken token)
    {
        int count = (anchors.Count - offset + stride - 1) / stride;
        int sampleCount = Math.Min(count, MaximumFitAnchors);
        var sample = new Anchor[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            sample[i] = anchors[offset + (int)((long)i * (count - 1) / (sampleCount - 1)) * stride];

        List<double> slopes = new(sampleCount * (sampleCount - 1) / 2);
        for (int i = 0; i < sample.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            for (int j = i + 1; j < sample.Length; j++)
            {
                double authoredDistance = sample[j].AuthoredEnd - sample[i].AuthoredEnd;
                if (authoredDistance >= minimumPairSeparation)
                    slopes.Add((sample[j].SpeechEnd - sample[i].SpeechEnd) / authoredDistance);
            }
        }
        if (slopes.Count == 0)
            return null;
        double scale = Median(slopes);
        if (scale is < .8 or > 1.25)
            return null;
        return new(scale, Median(sample.Select(anchor => anchor.SpeechEnd - scale * anchor.AuthoredEnd).ToList()));
    }

    private static List<Anchor>? Validate(List<Anchor> anchors,
        Func<Anchor, int, (double Start, double End)> project, CancellationToken token)
    {
        var quartiles = Enumerable.Range(0, QuartileCount)
            .Select(_ => (Starts: new List<double>(), Ends: new List<double>())).ToArray();
        List<Anchor> inliers = [];
        for (int i = 0; i < anchors.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            Anchor anchor = anchors[i];
            var (start, end) = project(anchor, i);
            double rawStartResidual = start - anchor.SpeechStart;
            double endResidual = end - anchor.SpeechEnd;
            if (Contradicts(rawStartResidual, endResidual))
                return null;

            // The recognizer can stretch the first word over a pause. An independently identified
            // elongated prefix gives a range for the onset, with the speech end still measured directly.
            double startResidual = rawStartResidual > 0 ? Math.Max(0, start - anchor.OnsetPrefixEnd) : rawStartResidual;
            if (Math.Abs(startResidual) <= ToleranceMs && Math.Abs(endResidual) <= ToleranceMs)
                inliers.Add(anchor);
            quartiles[anchor.Quartile].Starts.Add(startResidual);
            quartiles[anchor.Quartile].Ends.Add(endResidual);
        }
        return inliers.Count >= anchors.Count - anchors.Count / 10 &&
            quartiles.All(bucket => Math.Abs(Median(bucket.Starts)) <= QuartileBiasMs &&
                Math.Abs(Median(bucket.Ends)) <= QuartileBiasMs) ? inliers : null;
    }

    private static bool Contradicts(double startResidual, double endResidual)
        => startResidual > ContradictionMs && endResidual > ContradictionMs ||
            startResidual < -ContradictionMs && endResidual < -ContradictionMs;

    private static double Median(List<double> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    private readonly record struct Anchor(int CueIndex, int Quartile, double AuthoredStart, double AuthoredEnd,
        double SpeechStart, double OnsetPrefixEnd, double SpeechEnd)
    {
        internal double AuthoredCenter => (AuthoredStart + AuthoredEnd) / 2;
    }

    private readonly record struct Clock(double Scale, double Offset)
    {
        internal long Project(double position) => (long)Math.Round(Scale * position + Offset, MidpointRounding.AwayFromZero);
    }
}
