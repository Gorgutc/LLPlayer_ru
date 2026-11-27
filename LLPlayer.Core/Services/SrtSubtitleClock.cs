using FlyleafLib.MediaPlayer;

namespace LLPlayer.Core.Services;

/// <summary>A shared correction inferred from exact speech anchors for the authored subtitle track.</summary>
public sealed record SrtClockCorrection(int AnchorCount, double Scale, TimeSpan FirstAdjustment, TimeSpan LastAdjustment)
{
    public int ClippedBoundaryCount { get; init; }
}

internal static class SrtSubtitleClock
{
    private const int MinimumAnchors = 6;
    private const int MaximumFitAnchors = 64;
    private const int QuartileCount = 4;
    private const double BoundaryToleranceMs = 750;
    private const double ContradictoryPhraseToleranceMs = 1_500;
    private const double FoldAgreementMs = 500;
    private const double QuartileBiasToleranceMs = 400;
    private const double MaximumUnsupportedGapMs = 32_000;
    private const int PlaybackEdgeToleranceMs = 300;

    public static (SrtClockCorrection Summary, IReadOnlyList<(TimeSpan Start, TimeSpan End)> Intervals)? TryCreate(
        IReadOnlyList<SrtAlignmentProposal> firstPass, TimeSpan duration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (firstPass.Count < MinimumAnchors || duration <= TimeSpan.Zero)
            return null;

        double first = firstPass[0].Cue.Start.TotalMilliseconds;
        double last = firstPass[^1].Cue.End.TotalMilliseconds;
        double span = last - first;
        if (span <= 0)
            return null;

        List<Anchor> anchors = [];
        int lastId = -1;
        TimeSpan lastCueEnd = TimeSpan.Zero;
        TimeSpan lastSpeechEnd = TimeSpan.Zero;
        for (int i = 0; i < firstPass.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            SrtAlignmentProposal proposal = firstPass[i];
            var cue = proposal.Cue;
            if (cue.Id <= lastId || cue.Start < lastCueEnd || cue.End <= cue.Start)
                return null;
            lastId = cue.Id;
            lastCueEnd = cue.End;

            if (proposal.Rejection is SrtAlignmentRejection.ConflictingOrder or SrtAlignmentRejection.InvalidTiming)
                return null;
            if (!proposal.IsExactAnchor)
                continue;
            if (proposal.Rejection != SrtAlignmentRejection.None ||
                proposal.Start is not { } start || proposal.End is not { } end ||
                start < lastSpeechEnd || end <= start || end > duration)
                return null;
            lastSpeechEnd = end;

            double cueStart = cue.Start.TotalMilliseconds;
            double cueEnd = cue.End.TotalMilliseconds;
            int quartile = Math.Min(QuartileCount - 1, (int)(((cueStart + cueEnd) / 2 - first) / span * QuartileCount));
            anchors.Add(new(cueStart, cueEnd, start.TotalMilliseconds, end.TotalMilliseconds, quartile, i));
        }

        if (!HasTrackSupport(anchors, firstPass.Count, first, last, token))
            return null;

        double minimumPairSeparation = Math.Min(15_000, span / 4);
        if (Fit(anchors, 0, 2, minimumPairSeparation) is not { } even ||
            Fit(anchors, 1, 2, minimumPairSeparation) is not { } odd ||
            Math.Abs(even.Project(first) - odd.Project(first)) > FoldAgreementMs ||
            Math.Abs(even.Project(last) - odd.Project(last)) > FoldAgreementMs ||
            ValidatedInliers(anchors, (anchor, index) => Project(anchor, index % 2 == 0 ? odd : even), token) == null ||
            Fit(anchors, 0, 1, minimumPairSeparation) is not { } clock)
            return null;

        var intervals = new (TimeSpan Start, TimeSpan End)[firstPass.Count];
        long mediaMilliseconds = duration.Ticks / TimeSpan.TicksPerMillisecond;
        long previousEnd = 0;
        int clipped = 0;
        for (int i = 0; i < intervals.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            SrtAlignmentProposal proposal = firstPass[i];
            var cue = proposal.Cue;
            long start = RoundedMilliseconds(clock.Project(cue.Start.TotalMilliseconds));
            long end = RoundedMilliseconds(clock.Project(cue.End.TotalMilliseconds));

            // Allow rounding a small overshoot to the playable edge only with speech confirmed at that edge.
            if (i == 0 && start is >= -PlaybackEdgeToleranceMs and < 0 && proposal.IsExactAnchor &&
                proposal.Start is { } speechStart && speechStart.TotalMilliseconds <= PlaybackEdgeToleranceMs)
            {
                start = 0;
                clipped++;
            }
            if (i == intervals.Length - 1 && end > mediaMilliseconds && end - mediaMilliseconds <= PlaybackEdgeToleranceMs &&
                proposal.IsExactAnchor && proposal.End is { } speechEnd &&
                (duration - speechEnd).TotalMilliseconds <= PlaybackEdgeToleranceMs)
            {
                end = mediaMilliseconds;
                clipped++;
            }
            if (start < previousEnd || end <= start || end > mediaMilliseconds)
                return null;

            intervals[i] = (AsTime(start), AsTime(end));
            previousEnd = end;
        }

        TimeSpan firstAdjustment = intervals[0].Start - firstPass[0].Cue.Start;
        TimeSpan lastAdjustment = intervals[^1].End - firstPass[^1].Cue.End;
        if (Math.Abs(firstAdjustment.TotalMilliseconds) < BoundaryToleranceMs &&
            Math.Abs(lastAdjustment.TotalMilliseconds) < BoundaryToleranceMs)
            return null;

        var inliers = ValidatedInliers(anchors, (anchor, _) =>
        {
            var (start, end) = intervals[anchor.CueIndex];
            return (start.TotalMilliseconds, end.TotalMilliseconds);
        }, token);
        if (inliers == null || !HasTrackSupport(inliers, firstPass.Count, first, last, token))
            return null;

        return (new SrtClockCorrection(inliers.Count, clock.Scale, firstAdjustment, lastAdjustment)
            { ClippedBoundaryCount = clipped }, intervals);
    }

    internal static bool IsSupported(SrtAlignmentProposal proposal, (TimeSpan Start, TimeSpan End) interval) =>
        proposal.IsExactAnchor && proposal.Start is { } start && proposal.End is { } end &&
        WithinTolerance((interval.Start - start).TotalMilliseconds, (interval.End - end).TotalMilliseconds);

    private static bool HasTrackSupport(IReadOnlyList<Anchor> anchors, int cueCount, double first, double last,
        CancellationToken token)
    {
        // Independently corroborated anchors must span the track, not merely a block of common dialogue.
        double span = last - first;
        double edgeExtensionMs = Math.Clamp(.05 * span, 1_000, 5_000);
        if (anchors.Count < MinimumAnchors || anchors.Count < cueCount - anchors.Count ||
            anchors[^1].AuthoredCenter - anchors[0].AuthoredCenter < .8 * span ||
            anchors[0].AuthoredStart - first > edgeExtensionMs || last - anchors[^1].AuthoredEnd > edgeExtensionMs)
            return false;

        int quartilesSeen = 1 << anchors[0].Quartile;
        for (int i = 1; i < anchors.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            Anchor previous = anchors[i - 1];
            Anchor current = anchors[i];
            if (current.CueIndex > previous.CueIndex + 1 &&
                current.AuthoredCenter - previous.AuthoredCenter > MaximumUnsupportedGapMs)
                return false;
            quartilesSeen |= 1 << current.Quartile;
        }

        return quartilesSeen == (1 << QuartileCount) - 1;
    }

    private static ClockMap? Fit(IReadOnlyList<Anchor> anchors, int offset, int stride, double minimumPairSeparation)
    {
        int count = (anchors.Count - offset + stride - 1) / stride;
        int sampleCount = Math.Min(count, MaximumFitAnchors);
        var sample = new Anchor[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            int index = (int)((long)i * (count - 1) / (sampleCount - 1));
            sample[i] = anchors[offset + index * stride];
        }

        List<double> slopes = new(sampleCount * (sampleCount - 1) / 2);
        for (int i = 0; i < sample.Length; i++)
        {
            for (int j = i + 1; j < sample.Length; j++)
            {
                double cueDistance = sample[j].AuthoredCenter - sample[i].AuthoredCenter;
                if (cueDistance >= minimumPairSeparation)
                    slopes.Add((sample[j].SpeechCenter - sample[i].SpeechCenter) / cueDistance);
            }
        }
        if (slopes.Count == 0)
            return null;

        double scale = Median(slopes);
        if (scale is < .8 or > 1.25)
            return null;
        double offsetMs = Median(sample.Select(anchor => anchor.SpeechCenter - scale * anchor.AuthoredCenter).ToList());
        return new(scale, offsetMs);
    }

    private static (double Start, double End) Project(Anchor anchor, ClockMap clock) =>
        (RoundedMilliseconds(clock.Project(anchor.AuthoredStart)), RoundedMilliseconds(clock.Project(anchor.AuthoredEnd)));

    private static List<Anchor>? ValidatedInliers(IReadOnlyList<Anchor> anchors,
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
            double startResidual = start - anchor.SpeechStart;
            double endResidual = end - anchor.SpeechEnd;
            // A whole phrase displaced in the same direction at both edges is evidence of a local edit.
            if ((startResidual > ContradictoryPhraseToleranceMs && endResidual > ContradictoryPhraseToleranceMs) ||
                (startResidual < -ContradictoryPhraseToleranceMs && endResidual < -ContradictoryPhraseToleranceMs))
                return null;
            if (WithinTolerance(startResidual, endResidual))
                inliers.Add(anchor);
            quartiles[anchor.Quartile].Starts.Add(startResidual);
            quartiles[anchor.Quartile].Ends.Add(endResidual);
        }

        // The 90% joint threshold also bounds the 90th percentile of each edge.
        return inliers.Count >= anchors.Count - anchors.Count / 10 &&
            quartiles.All(bucket => Math.Abs(Median(bucket.Starts)) <= QuartileBiasToleranceMs &&
                Math.Abs(Median(bucket.Ends)) <= QuartileBiasToleranceMs) ? inliers : null;
    }

    private static bool WithinTolerance(double startResidual, double endResidual) =>
        Math.Abs(startResidual) <= BoundaryToleranceMs && Math.Abs(endResidual) <= BoundaryToleranceMs;

    private static double Median(List<double> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    private static long RoundedMilliseconds(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
    private static TimeSpan AsTime(long milliseconds) => new(milliseconds * TimeSpan.TicksPerMillisecond);

    private readonly record struct ClockMap(double Scale, double Offset)
    {
        public double Project(double time) => Scale * time + Offset;
    }

    private readonly record struct Anchor(
        double AuthoredStart, double AuthoredEnd, double SpeechStart, double SpeechEnd, int Quartile, int CueIndex)
    {
        public double AuthoredCenter => (AuthoredStart + AuthoredEnd) / 2;
        public double SpeechCenter => (SpeechStart + SpeechEnd) / 2;
    }
}
