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
        // A short leading token before a long UNBREAKABLE run: pre-fix the splitter isolated "x" as a ~40ms
        // first cue (a sub-MinCueDurationSec sliver, violating the "never emit a sliver" contract). The
        // forward-merge must fold it into the next cue so EVERY cue is >= MinCueDurationSec, keeping the first
        // Start and losing no text. (Guard verified RED against the pre-fix segmenter: first cue was 0.040s;
        // codex's original "x " + 80×"readable" input never produced a short first cue, so it guarded nothing.)
        string input = "x " + new string('y', 200) + " z";

        var cues = SubtitleSegmenter.Resegment(input, TimeSpan.Zero, TimeSpan.FromSeconds(8), Opt);

        cues.Should().OnlyContain(c => (c.End - c.Start) >= TimeSpan.FromSeconds(Opt.MinCueDurationSec));
        (cues[0].End - cues[0].Start).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(Opt.MinCueDurationSec));
        cues[0].Start.Should().Be(TimeSpan.Zero);
        cues[^1].End.Should().Be(TimeSpan.FromSeconds(8));
        Normalize(string.Join(' ', cues.Select(c => c.Text))).Should().Be(Normalize(input));
    }

    [Fact]
    public void Resegment_ShortStandaloneCue_PreservesRealShortPhrase()
    {
        // A genuine short reply must NOT be stretched or glued to anything. (Pins the fits-as-is fast path for
        // a single short cue; the forward-merge only ever acts when there is more than one generated cue.)
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

        // Effective per-line width clamps 0 -> 1, so wrapping degrades to one token per line (not a div-by-zero
        // budget). The observable shape is each token on its own line, with no text lost.
        result.Split('\n').Should().Equal("ab", "cd");
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

    // ---- F-19: word-timestamp-driven cue boundaries -------------------------------------------------

    // "one two three four five six seven eight": the first four words are spoken slowly (ending at 8.5s), the
    // last four in a quick burst (8.5-10s). Character proportion assumes a constant rate; the word timings do not.
    private static readonly (string Word, double Start, double End)[] EightWordsFrontSlow =
    {
        ("one",   0.0, 1.0),
        ("two",   1.0, 3.0),
        ("three", 3.0, 6.0),
        ("four",  6.0, 8.5),
        ("five",  8.5, 8.9),
        ("six",   8.9, 9.3),
        ("seven", 9.3, 9.7),
        ("eight", 9.7, 10.0),
    };

    private static List<WordTiming> Words((string Word, double Start, double End)[] src) =>
        src.Select(w => new WordTiming(w.Word, TimeSpan.FromSeconds(w.Start), TimeSpan.FromSeconds(w.End))).ToList();

    [Fact]
    public void Resegment_WithWordTimings_SnapsBoundaryToRealSpeechPace()
    {
        // The cue fits on one line but its 10s display time forces a split by MaxCueDurationSec. Character
        // proportion would cut near the middle of the span (~4.7s); the word timings must instead snap the cut
        // to the real speech boundary (the slow front half ends ~8.5s). RED without the WordClock: null words
        // fall back to the char-proportion path and the first cue ends at ~4.7s.
        const string input = "one two three four five six seven eight";
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = TimeSpan.FromSeconds(10);

        var withWords = SubtitleSegmenter.Resegment(input, start, end, Opt, Words(EightWordsFrontSlow));
        var charOnly = SubtitleSegmenter.Resegment(input, start, end, Opt);

        withWords.Count.Should().BeGreaterThan(1);
        charOnly[0].End.Should().BeLessThan(TimeSpan.FromSeconds(6));       // char proportion lands mid-span
        withWords[0].End.Should().BeGreaterThan(TimeSpan.FromSeconds(7));   // word timing snaps to real speech
        Normalize(string.Join(' ', withWords.Select(c => c.Text))).Should().Be(Normalize(input));
    }

    [Fact]
    public void Resegment_WithWordTimings_TimesStayMonotonicContiguousAndBounded()
    {
        const string input = "one two three four five six seven eight";
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = TimeSpan.FromSeconds(10);

        var cues = SubtitleSegmenter.Resegment(input, start, end, Opt, Words(EightWordsFrontSlow));

        cues[0].Start.Should().Be(start);
        cues[^1].End.Should().Be(end);
        for (int i = 0; i < cues.Count; i++)
        {
            cues[i].End.Should().BeGreaterThanOrEqualTo(cues[i].Start);
            cues[i].End.Should().BeLessThanOrEqualTo(end);
            if (i > 0)
                cues[i].Start.Should().Be(cues[i - 1].End); // no gaps / no overlaps
        }
    }

    [Fact]
    public void Resegment_NullWords_ByteIdenticalToCharProportion()
    {
        // The optional words parameter must not change any existing caller: passing null is exactly the old path.
        string input =
            "Hello there my friend, how are you doing today? I really hope that you are " +
            "having a wonderful and pleasant afternoon out there in the bright sunshine.";
        TimeSpan start = TimeSpan.FromSeconds(10);
        TimeSpan end = TimeSpan.FromSeconds(20);

        var noArg = SubtitleSegmenter.Resegment(input, start, end, Opt);
        var nullWords = SubtitleSegmenter.Resegment(input, start, end, Opt, null);

        nullWords.Should().Equal(noArg);
    }

    [Fact]
    public void Resegment_WithWordTimings_FitsAsIs_PassesThroughUnchanged()
    {
        // A short cue that already fits takes the fast path; supplied words must not split or re-time it.
        var words = Words(new[] { ("Yes", 2.0, 2.3) });
        var cues = SubtitleSegmenter.Resegment("Yes.", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.4), Opt, words);

        cues.Should().HaveCount(1);
        cues[0].Text.Should().Be("Yes.");
        cues[0].Start.Should().Be(TimeSpan.FromSeconds(2));
        cues[0].End.Should().Be(TimeSpan.FromSeconds(2.4));
    }

    // ---- F-19 slice 2: VAD speech-aware cue-boundary snapping ---------------------------------------

    private static List<SpeechSegment> Speech((double Start, double End)[] src) =>
        src.Select(s => new SpeechSegment(TimeSpan.FromSeconds(s.Start), TimeSpan.FromSeconds(s.End))).ToList();

    [Fact]
    public void Resegment_WithSpeech_SnapsBoundaryToNearestSilence()
    {
        // A 10s cue split in two by the duration cap. Constant-rate character proportion puts the internal
        // boundary at 5.0s; a VAD silence gap sits at [4.9, 5.5] (midpoint 5.2s, within the 0.5s tolerance), so
        // the boundary snaps into that pause. RED without SilenceClock: speech is ignored and it stays at 5.0s.
        const string input = "one two three four five six";
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = TimeSpan.FromSeconds(10);

        var charOnly = SubtitleSegmenter.Resegment(input, start, end, Opt);
        var withSpeech = SubtitleSegmenter.Resegment(input, start, end, Opt, null,
            Speech(new[] { (0.0, 4.9), (5.5, 10.0) }));

        charOnly.Should().HaveCount(2);
        charOnly[0].End.Should().Be(TimeSpan.FromSeconds(5.0));       // constant-rate char proportion
        withSpeech[0].End.Should().Be(TimeSpan.FromSeconds(5.2));     // snapped to the silence-gap midpoint
        Normalize(string.Join(' ', withSpeech.Select(c => c.Text))).Should().Be(Normalize(input));
    }

    [Fact]
    public void Resegment_WithSpeech_BeyondTolerance_DoesNotSnap()
    {
        // The only silence gap (midpoint 6.0s) is 1.0s from the 5.0s character boundary — beyond the 0.5s
        // tolerance — so the boundary is left exactly where the character proportion put it.
        const string input = "one two three four five six";
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = TimeSpan.FromSeconds(10);

        var withSpeech = SubtitleSegmenter.Resegment(input, start, end, Opt, null,
            Speech(new[] { (0.0, 5.7), (6.3, 10.0) }));

        withSpeech[0].End.Should().Be(TimeSpan.FromSeconds(5.0));
    }

    [Fact]
    public void Resegment_NullSpeech_ByteIdenticalToCharProportion()
    {
        // The optional speech parameter must not change any existing caller: passing null (or omitting it) is
        // exactly the old character-proportion path.
        const string input = "one two three four five six";
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = TimeSpan.FromSeconds(10);

        var noArg = SubtitleSegmenter.Resegment(input, start, end, Opt);
        var nullSpeech = SubtitleSegmenter.Resegment(input, start, end, Opt, null, null);

        nullSpeech.Should().Equal(noArg);
    }

    [Fact]
    public void Resegment_WithSpeech_TimesStayMonotonicContiguousAndBounded()
    {
        const string input = "one two three four five six seven eight nine ten eleven twelve";
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = TimeSpan.FromSeconds(18);
        // Several silence gaps across the span; snapping must never break the sorted/contiguous/bounded invariant.
        var speech = Speech(new[] { (0.0, 3.4), (3.7, 8.4), (8.7, 13.4), (13.7, 18.0) });

        var cues = SubtitleSegmenter.Resegment(input, start, end, Opt, null, speech);

        cues[0].Start.Should().Be(start);
        cues[^1].End.Should().Be(end);
        for (int i = 0; i < cues.Count; i++)
        {
            cues[i].End.Should().BeGreaterThanOrEqualTo(cues[i].Start);
            cues[i].End.Should().BeLessThanOrEqualTo(end);
            if (i > 0)
                cues[i].Start.Should().Be(cues[i - 1].End); // no gaps / no overlaps
        }
        Normalize(string.Join(' ', cues.Select(c => c.Text))).Should().Be(Normalize(input));
    }

    [Fact]
    public void Resegment_WordsThenSpeech_SnapsWordBoundaryIntoAdjacentPause()
    {
        // Compose slice 1 + slice 2: the word timings place the boundary at a real word end (8.9s); a VAD silence
        // gap at [8.5, 8.9] (midpoint 8.7s) is within tolerance, so slice 2 nudges that boundary into the pause.
        const string input = "one two three four five six seven eight";
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = TimeSpan.FromSeconds(10);

        var wordsOnly = SubtitleSegmenter.Resegment(input, start, end, Opt, Words(EightWordsFrontSlow));
        var wordsAndSpeech = SubtitleSegmenter.Resegment(input, start, end, Opt,
            Words(EightWordsFrontSlow), Speech(new[] { (0.0, 8.5), (8.9, 10.0) }));

        wordsOnly[0].End.Should().Be(TimeSpan.FromSeconds(8.9));          // word end (slice 1)
        wordsAndSpeech[0].End.Should().Be(TimeSpan.FromSeconds(8.7));     // nudged into the pause (slice 2)
    }
}
