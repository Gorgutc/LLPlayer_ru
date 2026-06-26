using System.Linq;
using AwesomeAssertions;
using FlyleafLib;

namespace FlyleafLib;

public class SubtitleSegmenterTests
{
    private static readonly SubtitleSegmentOptions Opt = new()
    {
        MaxCharsPerLine = 42,
        MaxLinesPerCue = 2,
        MaxCjkCharsPerLine = 21,
        MaxCueDurationSec = 6.0,
        MinCueDurationSec = 1.0,
    };

    // Three-line cap with the 0.3.7 relaxed defaults.
    private static readonly SubtitleSegmentOptions Opt3 = new()
    {
        MaxCharsPerLine = 48,
        MaxLinesPerCue = 3,
        MaxCjkCharsPerLine = 24,
        MaxCueDurationSec = 7.0,
        MinCueDurationSec = 1.0,
    };

    private static string Normalize(string s) =>
        string.Join(' ', s.Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void WrapLines_ShortText_ReturnedUnchangedWithoutBreak()
    {
        string result = SubtitleSegmenter.WrapLines("Hello there my friend.", Opt);
        result.Should().Be("Hello there my friend.");
        result.Should().NotContain("\n");
    }

    [Fact]
    public void WrapLines_LongText_SplitsIntoTwoBalancedLines()
    {
        string input = "The quick brown fox jumps over the lazy dog near the river.";
        string result = SubtitleSegmenter.WrapLines(input, Opt);

        string[] lines = result.Split('\n');
        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(l => l.Length <= Opt.MaxCharsPerLine);
        Normalize(result).Should().Be(Normalize(input)); // no text lost, no word split
    }

    [Fact]
    public void WrapLines_SingleOverlongWord_NotSplitMidWord()
    {
        string input = "Supercalifragilisticexpialidocioussupercalifragilisticword";
        string result = SubtitleSegmenter.WrapLines(input, Opt);
        result.Should().Be(input); // cannot break a single word; returned as-is, no crash
    }

    [Fact]
    public void Resegment_ShortCue_PassesThroughUnchanged()
    {
        var cues = SubtitleSegmenter.Resegment("Hello there.", TimeSpan.Zero, TimeSpan.FromSeconds(2), Opt);
        cues.Should().HaveCount(1);
        cues[0].Text.Should().Be("Hello there.");
        cues[0].Start.Should().Be(TimeSpan.Zero);
        cues[0].End.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Resegment_AlreadyTwoLineDialogue_Preserved()
    {
        const string dialogue = "- Yes, of course.\n- No, never again.";
        var cues = SubtitleSegmenter.Resegment(dialogue, TimeSpan.Zero, TimeSpan.FromSeconds(3), Opt);
        cues.Should().HaveCount(1);
        cues[0].Text.Should().Be(dialogue); // fast path leaves a well-formed multi-line cue untouched
    }

    [Fact]
    public void Resegment_LongCue_SplitsIntoMultipleCuesEachAtMostTwoLines()
    {
        string input =
            "Hello there my friend, how are you doing today? I really hope that you are " +
            "having a wonderful and pleasant afternoon out there in the bright sunshine.";

        var cues = SubtitleSegmenter.Resegment(input, TimeSpan.Zero, TimeSpan.FromSeconds(8), Opt);

        cues.Count.Should().BeGreaterThan(1);
        foreach (var cue in cues)
        {
            string[] lines = cue.Text.Split('\n');
            lines.Length.Should().BeLessThanOrEqualTo(Opt.MaxLinesPerCue);
            lines.Should().OnlyContain(l => l.Length <= Opt.MaxCharsPerLine);
        }
    }

    [Fact]
    public void Resegment_PreservesAllTextAcrossCues()
    {
        string input =
            "Hello there my friend, how are you doing today? I really hope that you are " +
            "having a wonderful and pleasant afternoon out there in the bright sunshine.";

        var cues = SubtitleSegmenter.Resegment(input, TimeSpan.Zero, TimeSpan.FromSeconds(8), Opt);

        string joined = Normalize(string.Join(' ', cues.Select(c => c.Text)));
        joined.Should().Be(Normalize(input));
    }

    [Fact]
    public void Resegment_TimesAreMonotonicContiguousAndBounded()
    {
        string input =
            "Hello there my friend, how are you doing today? I really hope that you are " +
            "having a wonderful and pleasant afternoon out there in the bright sunshine.";
        TimeSpan start = TimeSpan.FromSeconds(10);
        TimeSpan end = TimeSpan.FromSeconds(20);

        var cues = SubtitleSegmenter.Resegment(input, start, end, Opt);

        cues[0].Start.Should().Be(start);
        cues[^1].End.Should().Be(end);
        for (int i = 0; i < cues.Count; i++)
        {
            cues[i].End.Should().BeGreaterThanOrEqualTo(cues[i].Start);
            if (i > 0)
            {
                cues[i].Start.Should().Be(cues[i - 1].End); // no gaps / no overlaps
            }
        }
    }

    [Fact]
    public void Resegment_DurationOnlyOverlong_MaySplitByTime()
    {
        // Short text but a very long display time -> may split into multiple cues by MaxCueDurationSec.
        var cues = SubtitleSegmenter.Resegment(
            "Quiet on the set everyone please.", TimeSpan.Zero, TimeSpan.FromSeconds(18), Opt);

        cues.Count.Should().BeGreaterThan(1);
        cues.Should().OnlyContain(c => (c.End - c.Start) >= TimeSpan.FromSeconds(Opt.MinCueDurationSec));
        Normalize(string.Join(' ', cues.Select(c => c.Text)))
            .Should().Be(Normalize("Quiet on the set everyone please."));
    }

    [Fact]
    public void Resegment_TooShortFirstGeneratedCue_MergesForward()
    {
        string input = "x " + string.Join(' ', Enumerable.Repeat("readable", 80)) + " z";

        var cues = SubtitleSegmenter.Resegment(input, TimeSpan.Zero, TimeSpan.FromSeconds(30), Opt);

        cues.Should().HaveCountGreaterThan(1);
        cues.Should().OnlyContain(c => (c.End - c.Start) >= TimeSpan.FromSeconds(Opt.MinCueDurationSec));
        cues[0].Start.Should().Be(TimeSpan.Zero);
        cues[^1].End.Should().Be(TimeSpan.FromSeconds(30));
        Normalize(string.Join(' ', cues.Select(c => c.Text))).Should().Be(Normalize(input));
    }

    [Fact]
    public void Resegment_ShortStandaloneCue_PreservesRealShortPhrase()
    {
        var cues = SubtitleSegmenter.Resegment("Yes.", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.4), Opt);

        cues.Should().ContainSingle();
        cues[0].Text.Should().Be("Yes.");
        cues[0].Start.Should().Be(TimeSpan.FromSeconds(2));
        cues[0].End.Should().Be(TimeSpan.FromSeconds(2.4));
    }

    [Fact]
    public void Resegment_Cjk_SplitsByCharacterCount()
    {
        string input = new string('あ', 50); // space-less, 50 chars
        var cues = SubtitleSegmenter.Resegment(input, TimeSpan.Zero, TimeSpan.FromSeconds(8), Opt);

        cues.Count.Should().BeGreaterThan(1);
        foreach (var cue in cues)
        {
            string[] lines = cue.Text.Split('\n');
            lines.Length.Should().BeLessThanOrEqualTo(Opt.MaxLinesPerCue);
            lines.Should().OnlyContain(l => l.Length <= Opt.MaxCjkCharsPerLine);
        }

        string joined = string.Concat(cues.Select(c => c.Text.Replace("\n", "")));
        joined.Should().Be(input); // no character lost
    }

    [Fact]
    public void WrapLines_MixedLatinAndCjk_NeverSplitsInsideLatinWord()
    {
        // CJK-dominant cue with an embedded Latin word: the break must land at the word boundary, never inside
        // "internationalization".
        string input = "internationalization " + new string('字', 12);
        string result = SubtitleSegmenter.WrapLines(input, Opt);

        result.Should().NotContain("internationaliza\n");
        result.Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().Contain("internationalization");
    }

    [Fact]
    public void Resegment_MixedLatinAndCjk_PreservesLatinWordsAndAllText()
    {
        string input = "Welcome everyone to the show " + new string('字', 40);
        var cues = SubtitleSegmenter.Resegment(input, TimeSpan.Zero, TimeSpan.FromSeconds(8), Opt);

        foreach (var cue in cues)
        {
            // No Latin word ("Welcome"/"everyone"/...) may be cut by a line break.
            foreach (string word in new[] { "Welcome", "everyone", "show" })
            {
                cue.Text.Should().NotContain(word[..^1] + "\n");
            }
        }
    }

    [Fact]
    public void Resegment_EmptyText_ReturnsSingleCueUnchanged()
    {
        var cues = SubtitleSegmenter.Resegment("   ", TimeSpan.Zero, TimeSpan.FromSeconds(1), Opt);
        cues.Should().HaveCount(1);
    }

    [Fact]
    public void Defaults_AreThreeLineRelaxed()
    {
        SubtitleSegmentOptions o = new();
        o.MaxLinesPerCue.Should().Be(3);
        o.MaxCharsPerLine.Should().Be(48);
        o.MaxCjkCharsPerLine.Should().Be(24);
        o.MaxCueDurationSec.Should().Be(7.0);
        o.MinCueDurationSec.Should().Be(1.0);
    }

    [Fact]
    public void WrapLines_ThreeLineCap_LongText_UsesThreeBalancedLines_NoWordSplit()
    {
        // ~120 chars: cannot fit in two <=48 lines (>96), so it must use the third line.
        string input =
            "The quick brown fox jumps over the lazy dog while the curious cat watches " +
            "silently from the old wooden fence nearby today.";

        string result = SubtitleSegmenter.WrapLines(input, Opt3);

        string[] lines = result.Split('\n');
        lines.Should().HaveCount(3);
        lines.Should().OnlyContain(l => l.Length <= Opt3.MaxCharsPerLine);
        Normalize(result).Should().Be(Normalize(input)); // no text lost, no word split
    }

    [Fact]
    public void WrapLines_ThreeLineCap_TextThatFitsInTwo_DoesNotUseThirdLine()
    {
        // Fits in two <=48 lines, so even with a 3-line budget it should NOT be padded out to 3 lines.
        string input = "Hello there my dear old friend, how are you doing on this fine sunny morning?";

        string result = SubtitleSegmenter.WrapLines(input, Opt3);

        result.Split('\n').Length.Should().BeLessThanOrEqualTo(2);
        Normalize(result).Should().Be(Normalize(input));
    }

    [Fact]
    public void WrapLines_ThreeLineCap_SingleOverlongWord_NotSplit()
    {
        string input = "Supercalifragilisticexpialidocioussupercalifragilisticwordthatisextremelylong";
        SubtitleSegmenter.WrapLines(input, Opt3).Should().Be(input); // no break opportunity -> single line
    }

    [Fact]
    public void WrapLines_ZeroLineLength_ClampsToOneCharacter()
    {
        SubtitleSegmentOptions opt = new()
        {
            MaxCharsPerLine = 0,
            MaxLinesPerCue = 2,
            MaxCjkCharsPerLine = 0,
            MaxCueDurationSec = 6.0,
            MinCueDurationSec = 1.0,
        };

        string result = SubtitleSegmenter.WrapLines("ab cd", opt);

        result.Split('\n').Should().OnlyContain(line => line.Length >= 1);
        Normalize(result).Should().Be("ab cd");
    }

    [Fact]
    public void Resegment_ThreeLineCap_CuesAreAtMostThreeLines_NoTextLost()
    {
        string input =
            "Hello there my friend, how are you doing today? I really hope that you are " +
            "having a wonderful and pleasant afternoon out there in the bright sunshine.";

        var cues = SubtitleSegmenter.Resegment(input, TimeSpan.Zero, TimeSpan.FromSeconds(8), Opt3);

        foreach (var cue in cues)
        {
            string[] lines = cue.Text.Split('\n');
            lines.Length.Should().BeLessThanOrEqualTo(3);
            lines.Should().OnlyContain(l => l.Length <= Opt3.MaxCharsPerLine);
        }

        Normalize(string.Join(' ', cues.Select(c => c.Text))).Should().Be(Normalize(input));
    }
}
