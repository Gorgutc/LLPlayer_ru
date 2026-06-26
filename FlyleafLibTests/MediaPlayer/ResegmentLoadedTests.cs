using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaPlayer;

public class ResegmentLoadedTests
{
    private static readonly SubtitleSegmentOptions Opt = new()
    {
        MaxCharsPerLine = 42,
        MaxLinesPerCue = 2,
        MaxCjkCharsPerLine = 21,
        MaxCueDurationSec = 6.0,
        MinCueDurationSec = 1.0,
    };

    private static SubtitleData GiantCue() => new()
    {
        Text = string.Join(' ', Enumerable.Repeat("word", 120)),
        StartTime = TimeSpan.Zero,
        EndTime = TimeSpan.FromSeconds(30),
    };

    [Fact]
    public void GiantLoadedTextCue_SplitsIntoSortedCues_NoTextLost()
    {
        // F-01, the owner's pain: a loaded .srt cue with a giant ~6-line block must be split into short cues with
        // proportional timings, sorted within the original span (Subs binary-search invariant) and losing no text.
        SubtitleData data = GiantCue();

        List<SubtitleData> cues = SubManager.ResegmentLoaded(data, enabled: true, Opt);

        cues.Should().HaveCountGreaterThan(1);
        cues[0].StartTime.Should().Be(TimeSpan.Zero);
        cues[^1].EndTime.Should().Be(TimeSpan.FromSeconds(30));
        for (int i = 1; i < cues.Count; i++)
        {
            cues[i].StartTime.Should().BeGreaterThanOrEqualTo(cues[i - 1].StartTime);
        }
        Normalize(string.Join(' ', cues.Select(c => c.Text))).Should().Be(Normalize(GiantCue().Text!));
    }

    [Fact]
    public void ShortFittingCue_LeftUntouched_SameInstance()
    {
        SubtitleData data = new() { Text = "Short line.", StartTime = TimeSpan.FromSeconds(1), EndTime = TimeSpan.FromSeconds(3) };

        List<SubtitleData> cues = SubManager.ResegmentLoaded(data, enabled: true, Opt);

        cues.Should().ContainSingle().Which.Should().BeSameAs(data);
        data.Text.Should().Be("Short line.");
    }

    [Fact]
    public void DisabledToggle_PassesThroughUnchanged()
    {
        SubtitleData data = GiantCue();

        List<SubtitleData> cues = SubManager.ResegmentLoaded(data, enabled: false, Opt);

        cues.Should().ContainSingle().Which.Should().BeSameAs(data);
    }

    [Fact]
    public void BitmapCue_PassesThroughUnchanged()
    {
        SubtitleData data = new() { IsBitmap = true, StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(30) };

        List<SubtitleData> cues = SubManager.ResegmentLoaded(data, enabled: true, Opt);

        cues.Should().ContainSingle().Which.Should().BeSameAs(data);
    }

    [Fact]
    public void StyledAssCue_PassesThroughUnchanged_PreservingSubStyles()
    {
        // A styled (ASS) cue carries SubStyles whose per-character offsets re-segmentation would invalidate,
        // so it must pass through untouched.
        List<SubStyle> styles = new() { default };
        SubtitleData data = new()
        {
            Text = string.Join(' ', Enumerable.Repeat("word", 120)),
            SubStyles = styles,
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(30),
        };

        List<SubtitleData> cues = SubManager.ResegmentLoaded(data, enabled: true, Opt);

        cues.Should().ContainSingle().Which.Should().BeSameAs(data);
        data.SubStyles.Should().BeSameAs(styles);
    }

    [Fact]
    public void EmptyTextCue_PassesThroughUnchanged()
    {
        SubtitleData data = new() { Text = "", StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(5) };

        List<SubtitleData> cues = SubManager.ResegmentLoaded(data, enabled: true, Opt);

        cues.Should().ContainSingle().Which.Should().BeSameAs(data);
    }

    [Fact]
    public void LongDurationButFittingCue_IsNotSplit_AuthoredTimingPreserved()
    {
        // Owner decision: loaded subs split on line/character overflow ONLY, never on duration. A normal cue that
        // fits the line/char budget but is deliberately held longer than MaxCueDurationSec (6s here) must stay a
        // single cue with its authored timing — NOT be fragmented into timed pieces like the ASR path would.
        SubtitleData data = new() { Text = "A short readable line.", StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(20) };

        List<SubtitleData> cues = SubManager.ResegmentLoaded(data, enabled: true, Opt);

        cues.Should().ContainSingle().Which.Should().BeSameAs(data);
        data.EndTime.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void GiantCjkLoadedCue_SplitsByCjkCharCount_NoTextLost()
    {
        // Space-less CJK loaded subs must split by MaxCjkCharsPerLine, not the Latin width. 100 Han characters
        // over a long span -> multiple cues, no line over the CJK width, no character lost.
        string text = new string('字', 100);
        SubtitleData data = new() { Text = text, StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(20) };

        List<SubtitleData> cues = SubManager.ResegmentLoaded(data, enabled: true, Opt);

        cues.Should().HaveCountGreaterThan(1);
        cues[0].StartTime.Should().Be(TimeSpan.Zero);
        cues[^1].EndTime.Should().Be(TimeSpan.FromSeconds(20));
        foreach (SubtitleData cue in cues)
        {
            foreach (string line in cue.Text!.Split('\n'))
            {
                line.Length.Should().BeLessThanOrEqualTo(Opt.MaxCjkCharsPerLine);
            }
        }
        string.Concat(cues.Select(c => c.Text!.Replace("\n", ""))).Should().Be(text);
    }

    private static string Normalize(string s) =>
        string.Join(' ', s.Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
