namespace FlyleafLib.MediaPlayer;

#nullable enable

internal static class AsrPhantomAlignmentEvidence
{
    private const int Rate = AsrPhantomFilter.SampleRate;
    private const int Pad = AsrPhantomFilter.PaddingSamples;
    private const int LocalWindow = 8 * Rate;
    private const int LocalStep = LocalWindow / 2;
    private const int LocalContext = LocalStep / 2;
    private const int WordMargin = 2 * Rate;
    private const int ResidualFrame = Rate / 10;
    private const decimal PinnedWindowTolerance = .000000001m * Rate;
    private const double ResidualThresholdDbfs = -90.308998699;
    private const double FullScale = 32_768;

    internal static bool IsLongCandidate(AsrPhantomCandidate candidate)
        => candidate.Alignment is { WordCount: >= 3, LargestGapSeconds: >= 30,
            CoverageFraction: <= .25m, MaximumWordProbability: < .50m };

    internal static bool IsShortCandidate(ReadOnlySpan<short> vocals, AsrPhantomCandidate candidate,
        AsrPhantomCandidate? rejectedPrevious)
    {
        if (rejectedPrevious is not { } previous || !IsLongCandidate(previous) ||
            previous.Alignment is not { } anchor || 2 * anchor.LargestGapSeconds < anchor.DurationSeconds ||
            candidate.Alignment is not { WordCount: >= 3, TimedWordCount: 1, DurationSeconds: <= 2 } ||
            candidate.NoSpeechProbability is not (>= .65 and <= 1) ||
            !TryNativeClock(candidate, vocals.Length, out decimal first, out decimal last))
            return false;

        decimal gap = first - (previous.EndSample + anchor.EndOffset);
        if (gap is < 0 or > Pad)
            return false;

        int left = (int)decimal.Round(Math.Max(0, first - Pad));
        int right = (int)decimal.Round(Math.Min(vocals.Length, last + Pad));
        return HasNegligibleResidual(vocals[left..right]);
    }

    internal static AsrPhantomDecision EvaluateLong(ReadOnlySpan<short> vocals, AsrPhantomCandidate candidate,
        AsrPhantomVoiceViews views, Func<short[], AsrPhantomVoice?> measure)
    {
        if (!TryPaddedWindow(candidate, vocals.Length, out var window, out var wholeWindow))
            return Review(AsrPhantomReason.AlignmentUnverifiedPaddedWindow);
        if (CheckWholeViews(views, vocals.Length, wholeWindow, out var whole) is { } veto)
            return veto;

        var primary = AnalyzePhase(vocals, window, LocalContext, measure);
        var shifted = AnalyzePhase(vocals, window, 2 * LocalContext, measure);
        if (!Covers(primary.Coverage, window) || !Covers(shifted.Coverage, window))
            return Review(AsrPhantomReason.AlignmentLocalCoverageMissing);

        List<AsrPhantomVoiceInterval>[] votesByView = [whole, primary.Votes, shifted.Votes, []];
        var seeds = new List<AsrPhantomVoiceInterval>();
        seeds.AddRange(whole);
        seeds.AddRange(primary.Votes);
        seeds.AddRange(shifted.Votes);
        if (seeds.Count > 0)
        {
            var focused = AnalyzeFocused(vocals, window, seeds, measure);
            foreach (var seed in seeds)
                if (!Covers(focused.Coverage, seed))
                    return Review(AsrPhantomReason.AlignmentLocalCoverageMissing);
            votesByView[3] = focused.Votes;
        }

        if (candidate.Alignment is not { TimedSpans.Count: > 0 } alignment)
            return Review(AsrPhantomReason.AlignmentWordClockUnavailable);

        // The binder keeps offsets from the unprojected cue start, so the same
        // clock also works when an exact neighboring PCM window is prepended.
        foreach (var word in alignment.TimedSpans)
        {
            var projected = new AsrPhantomVoiceInterval(candidate.StartSample + word.StartSample,
                candidate.StartSample + word.EndSample);
            foreach (var votes in votesByView)
                if (Overlaps(votes, projected, WordMargin))
                    return Review(AsrPhantomReason.AlignmentVoiceNearWordClock);
        }

        for (int first = 0; first < votesByView.Length; first++)
            for (int second = first + 1; second < votesByView.Length; second++)
                foreach (var other in votesByView[second])
                    if (Overlaps(votesByView[first], other, Pad))
                        return Review(AsrPhantomReason.AlignmentNormalizationConsensus);

        return new(AsrPhantomAction.Drop, AsrPhantomReason.InvalidLongAlignment);
    }

    internal static AsrPhantomDecision EvaluateShort(ReadOnlySpan<short> vocals, AsrPhantomCandidate candidate,
        AsrPhantomVoiceViews views, Func<short[], AsrPhantomVoice?> measure)
    {
        if (!TryPaddedWindow(candidate, vocals.Length, out var window, out var wholeWindow))
            return Review(AsrPhantomReason.AlignmentUnverifiedPaddedWindow);
        if (CheckWholeViews(views, vocals.Length, wholeWindow, out var whole) is { } veto)
            return veto;
        if (whole.Count > 0)
            return Review(AsrPhantomReason.AlignmentVoiceSupport);

        var primary = AnalyzePhase(vocals, window, LocalContext, measure);
        var shifted = AnalyzePhase(vocals, window, 2 * LocalContext, measure);
        if (!Covers(primary.Coverage, window) || !Covers(shifted.Coverage, window))
            return Review(AsrPhantomReason.AlignmentLocalCoverageMissing);
        if (primary.Votes.Count > 0 || shifted.Votes.Count > 0)
            return new(AsrPhantomAction.Keep, AsrPhantomReason.AlignmentLocalVoiceSupport);

        return new(AsrPhantomAction.Drop, AsrPhantomReason.CollapsedAdjacentAlignment);
    }

    private static AsrPhantomDecision Review(AsrPhantomReason reason) => new(AsrPhantomAction.Review, reason);

    private static bool TryNativeClock(AsrPhantomCandidate candidate, int length, out decimal start, out decimal end)
    {
        start = candidate.StartSample + (candidate.Alignment?.StartOffset ?? 0);
        end = candidate.EndSample + (candidate.Alignment?.EndOffset ?? 0);
        return start >= 0 && end <= length && start < end;
    }

    private static bool TryPaddedWindow(AsrPhantomCandidate candidate, int length,
        out AsrPhantomVoiceInterval local, out AsrPhantomVoiceInterval whole)
    {
        local = whole = default;
        if (!TryNativeClock(candidate, length, out decimal first, out decimal last) ||
            first < Pad - PinnedWindowTolerance || last > length - Pad + PinnedWindowTolerance)
            return false;

        decimal left = Math.Max(0, first - Pad);
        decimal right = Math.Min(length, last + Pad);
        local = new((int)decimal.Round(left), (int)decimal.Round(right));
        whole = new((int)decimal.Floor(left), (int)decimal.Ceiling(right));
        return true;
    }

    private static AsrPhantomDecision? CheckWholeViews(AsrPhantomVoiceViews views, int length,
        AsrPhantomVoiceInterval window, out List<AsrPhantomVoiceInterval> normalized)
    {
        normalized = [];
        foreach (var view in new[] { views.Original, views.Vocal })
        {
            if (view is not { } raw || !HasValidMeasurement(raw, length) || raw.CoveredSamples < window.EndSample)
                return Review(AsrPhantomReason.AlignmentCoverageMissing);
            if (Overlaps(raw.Intervals, window, 0))
                return Review(AsrPhantomReason.AlignmentVoiceSupport);
        }
        if (views.Normalized is not { } whole || !HasValidMeasurement(whole, length) ||
            whole.CoveredSamples < window.EndSample)
            return Review(AsrPhantomReason.AlignmentCoverageMissing);
        AppendProjected(normalized, whole.Intervals, 0, window);
        Merge(normalized);
        return null;
    }

    private static bool HasValidMeasurement(AsrPhantomVoice view, int sampleCount)
    {
        if (view.CoveredSamples <= 0 || view.CoveredSamples > sampleCount || view.Intervals is null)
            return false;
        int last = 0;
        foreach (var interval in view.Intervals)
        {
            if (interval.StartSample < last || interval.EndSample <= interval.StartSample ||
                interval.EndSample > sampleCount)
                return false;
            last = interval.EndSample;
        }
        return true;
    }

    private sealed class LocalProbe
    {
        internal List<AsrPhantomVoiceInterval> Coverage { get; } = [];
        internal List<AsrPhantomVoiceInterval> Votes { get; } = [];
    }

    private static LocalProbe AnalyzePhase(ReadOnlySpan<short> vocals, AsrPhantomVoiceInterval window, int phase,
        Func<short[], AsrPhantomVoice?> measure)
    {
        var probe = new LocalProbe();
        for (long first = Math.Max(0, window.StartSample - phase); first < window.EndSample; first += LocalStep)
            AddLocalWindow(probe, vocals, window, (int)first, measure);
        Merge(probe.Coverage);
        Merge(probe.Votes);
        return probe;
    }

    private static LocalProbe AnalyzeFocused(ReadOnlySpan<short> vocals, AsrPhantomVoiceInterval window,
        IReadOnlyList<AsrPhantomVoiceInterval> seeds, Func<short[], AsrPhantomVoice?> measure)
    {
        var starts = new SortedSet<int>();
        foreach (var seed in seeds)
        {
            int center = (int)Math.Round((seed.StartSample + (long)seed.EndSample) / 2d);
            starts.Add(Math.Max(0, center - LocalWindow / 2));
        }
        var probe = new LocalProbe();
        foreach (int first in starts)
            AddLocalWindow(probe, vocals, window, first, measure);
        Merge(probe.Coverage);
        Merge(probe.Votes);
        return probe;
    }

    private static void AddLocalWindow(LocalProbe probe, ReadOnlySpan<short> vocals, AsrPhantomVoiceInterval window,
        int start, Func<short[], AsrPhantomVoice?> measure)
    {
        int count = Math.Min(LocalWindow, vocals.Length - start);
        short[] normalized = AsrPhantomFilter.NormalizeVocalForVad(vocals.Slice(start, count));
        if (measure(normalized) is not { } view || !HasValidMeasurement(view, count))
            return;

        probe.Coverage.Add(new(start, start + view.CoveredSamples));
        AppendProjected(probe.Votes, view.Intervals, start, window);
    }

    private static void AppendProjected(List<AsrPhantomVoiceInterval> destination,
        IReadOnlyList<AsrPhantomVoiceInterval> intervals, int offset, AsrPhantomVoiceInterval window)
    {
        foreach (var span in intervals)
        {
            int first = Math.Max(window.StartSample, offset + span.StartSample);
            int last = Math.Min(window.EndSample, offset + span.EndSample);
            if (first < last)
                destination.Add(new(first, last));
        }
    }

    private static void Merge(List<AsrPhantomVoiceInterval> intervals)
    {
        intervals.Sort(static (left, right) => left.StartSample.CompareTo(right.StartSample));
        int count = 0;
        for (int index = 0; index < intervals.Count; index++)
        {
            var span = intervals[index];
            if (count > 0 && span.StartSample <= intervals[count - 1].EndSample)
            {
                if (span.EndSample > intervals[count - 1].EndSample)
                    intervals[count - 1] = intervals[count - 1] with { EndSample = span.EndSample };
            }
            else
                intervals[count++] = span;
        }
        intervals.RemoveRange(count, intervals.Count - count);
    }

    private static bool Covers(IReadOnlyList<AsrPhantomVoiceInterval> coverage, AsrPhantomVoiceInterval span)
    {
        foreach (var covered in coverage)
            if (covered.StartSample <= span.StartSample && covered.EndSample >= span.EndSample)
                return true;
        return false;
    }

    private static bool Overlaps(IReadOnlyList<AsrPhantomVoiceInterval> intervals, AsrPhantomVoiceInterval span,
        int margin)
    {
        foreach (var interval in intervals)
            if (interval.StartSample < (long)span.EndSample + margin &&
                interval.EndSample > (long)span.StartSample - margin)
                return true;
        return false;
    }

    private static bool HasNegligibleResidual(ReadOnlySpan<short> samples)
    {
        int frameSize = Math.Min(ResidualFrame, samples.Length);
        int last = samples.Length - frameSize;
        for (int first = 0; ; first = Math.Min(first + ResidualFrame / 2, last))
        {
            var frame = samples.Slice(first, frameSize);
            double sum = 0, sumSquares = 0;
            foreach (short sample in frame)
            {
                sum += sample;
                sumSquares += sample * sample;
            }
            double mean = sum / frameSize;
            double rms = Math.Sqrt(Math.Max(0, sumSquares / frameSize - mean * mean));
            double dbfs = Math.Round(20 * Math.Log10(Math.Max(1e-12, rms / FullScale)), 3);
            if (dbfs >= ResidualThresholdDbfs)
                return false;
            if (first == last)
                return true;
        }
    }
}
