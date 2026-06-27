using System;
using System.Collections.Generic;
using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaPlayer;

public class SubtitleExporterTests
{
    private static List<SubtitleExportLine> TwoCues() =>
    [
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), "Hello"),
        new(new TimeSpan(0, 1, 2, 3, 4), new TimeSpan(0, 1, 2, 5, 6), "World"),
    ];

    [Fact]
    public void BuildSrt_IndexedCues_CommaMs_BlankLineBetween_NoTrailingBlank()
    {
        string srt = SubtitleExporter.Build(TwoCues(), SubtitleExportFormat.Srt);

        srt.Should().Be(
            "1\r\n00:00:01,000 --> 00:00:04,000\r\nHello\r\n" +
            "\r\n" +
            "2\r\n01:02:03,004 --> 01:02:05,006\r\nWorld\r\n");
    }

    [Fact]
    public void BuildVtt_HasHeader_DotMs_NoIndices()
    {
        string vtt = SubtitleExporter.Build(TwoCues(), SubtitleExportFormat.Vtt);

        vtt.Should().Be(
            "WEBVTT\r\n\r\n" +
            "00:00:01.000 --> 00:00:04.000\r\nHello\r\n" +
            "\r\n" +
            "01:02:03.004 --> 01:02:05.006\r\nWorld\r\n");
    }

    [Fact]
    public void BuildTxt_TextOnly_NoTimings()
    {
        string txt = SubtitleExporter.Build(TwoCues(), SubtitleExportFormat.Txt);

        txt.Should().Be("Hello\r\nWorld");
    }

    [Fact]
    public void Italic_IsRendered_InSrtAndVtt_ButNotTxt()
    {
        List<SubtitleExportLine> lines =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "ab cd",
                [new SubStyle(SubStyles.ITALIC, 0, 2)]),
        ];

        SubtitleExporter.Build(lines, SubtitleExportFormat.Srt).Should().Contain("<i>ab</i> cd");
        SubtitleExporter.Build(lines, SubtitleExportFormat.Vtt).Should().Contain("<i>ab</i> cd");
        SubtitleExporter.Build(lines, SubtitleExportFormat.Txt).Should().Be("ab cd");
    }

    [Fact]
    public void Italic_MultipleDisjointRanges_AreEachWrapped()
    {
        List<SubtitleExportLine> lines =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "aa bb cc",
                [new SubStyle(SubStyles.ITALIC, 0, 2), new SubStyle(SubStyles.ITALIC, 6, 2)]),
        ];

        SubtitleExporter.Build(lines, SubtitleExportFormat.Srt)
            .Should().Contain("<i>aa</i> bb <i>cc</i>");
    }

    [Fact]
    public void OnlyItalicIsEmitted_BoldAndUnderlineIgnored()
    {
        // ITALIC on "aa", BOLD on "bb", UNDERLINE on "cc": only the italic span is wrapped.
        List<SubtitleExportLine> lines =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "aa bb cc",
                [
                    new SubStyle(SubStyles.ITALIC, 0, 2),
                    new SubStyle(SubStyles.BOLD, 3, 2),
                    new SubStyle(SubStyles.UNDERLINE, 6, 2),
                ]),
        ];

        string srt = SubtitleExporter.Build(lines, SubtitleExportFormat.Srt);
        srt.Should().Contain("<i>aa</i> bb cc");
        srt.Should().NotContain("<b>");
        srt.Should().NotContain("<u>");
    }

    [Fact]
    public void BlankLineInsideCue_IsCollapsed_ForTimedFormats()
    {
        List<SubtitleExportLine> lines =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "line1\n\nline2"),
        ];

        // A blank line inside a cue would terminate it in SRT/VTT; it must be collapsed.
        string srt = SubtitleExporter.Build(lines, SubtitleExportFormat.Srt);
        srt.Should().Contain("line1\r\nline2");
        srt.Should().NotContain("line1\r\n\r\nline2");
    }

    [Fact]
    public void EmptyCueText_EmitsTimingWithEmptyTextLine()
    {
        // The export dialog filters empty cues out, but the serializer must still handle one gracefully.
        List<SubtitleExportLine> lines =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(1), ""),
        ];

        SubtitleExporter.Build(lines, SubtitleExportFormat.Srt)
            .Should().Be("1\r\n00:00:00,000 --> 00:00:01,000\r\n\r\n");
    }

    [Fact]
    public void NullStyles_LeaveTextUnchanged()
    {
        List<SubtitleExportLine> lines =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "plain text", null),
        ];

        SubtitleExporter.Build(lines, SubtitleExportFormat.Srt).Should().Contain("plain text");
        SubtitleExporter.Build(lines, SubtitleExportFormat.Srt).Should().NotContain("<i>");
    }

    [Fact]
    public void Italic_RangeBeyondTextBounds_IsClamped()
    {
        List<SubtitleExportLine> lines =
        [
            // len overshoots the string end; must clamp instead of throwing.
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "abc",
                [new SubStyle(SubStyles.ITALIC, 1, 99)]),
        ];

        SubtitleExporter.Build(lines, SubtitleExportFormat.Srt).Should().Contain("a<i>bc</i>");
    }

    [Fact]
    public void MultilineCue_NewlinesNormalizedToCrlf()
    {
        List<SubtitleExportLine> lines =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "line1\nline2"),
        ];

        SubtitleExporter.Build(lines, SubtitleExportFormat.Srt).Should().Contain("line1\r\nline2");
        SubtitleExporter.Build(lines, SubtitleExportFormat.Txt).Should().Be("line1\r\nline2");
    }

    [Fact]
    public void EmptyList_ProducesEmptySrtAndTxt_AndHeaderOnlyVtt()
    {
        List<SubtitleExportLine> none = [];

        SubtitleExporter.Build(none, SubtitleExportFormat.Srt).Should().BeEmpty();
        SubtitleExporter.Build(none, SubtitleExportFormat.Txt).Should().BeEmpty();
        SubtitleExporter.Build(none, SubtitleExportFormat.Vtt).Should().Be("WEBVTT\r\n\r\n");
    }

    [Theory]
    [InlineData(SubtitleExportFormat.Srt, ".srt")]
    [InlineData(SubtitleExportFormat.Vtt, ".vtt")]
    [InlineData(SubtitleExportFormat.Txt, ".txt")]
    public void Extension_MapsPerFormat(SubtitleExportFormat format, string expected)
    {
        SubtitleExporter.Extension(format).Should().Be(expected);
    }
}
