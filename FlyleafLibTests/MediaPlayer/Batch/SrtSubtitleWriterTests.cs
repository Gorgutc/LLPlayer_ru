using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.MediaPlayer.Batch;

public class SrtSubtitleWriterTests
{
    // HC-15: the batch SRT writer had a bespoke per-cue serializer that skipped NormalizeCueText and
    // InvariantCulture. It now delegates to the shared, unit-tested SubtitleExporter. These pin the fix and the
    // byte-identical normal case.

    private static SubtitleData Cue(TimeSpan start, TimeSpan end, string? translated, string? text = null) =>
        new() { StartTime = start, EndTime = end, Text = text, TranslatedText = translated };

    [Fact]
    public void BuildSrtContent_TranslatedCues_MatchesSharedExporter_ByteIdentical()
    {
        List<SubtitleData> subs =
        [
            Cue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), translated: "Hello", text: "orig-1"),
            Cue(new TimeSpan(0, 1, 2, 3, 4), new TimeSpan(0, 1, 2, 5, 6), translated: "World", text: "orig-2"),
        ];

        string srt = SrtSubtitleWriter.BuildSrtContent(subs);

        // DisplayText prefers TranslatedText; comma ms separator; blank line between cues, none trailing.
        srt.Should().Be(
            "1\r\n00:00:01,000 --> 00:00:04,000\r\nHello\r\n" +
            "\r\n" +
            "2\r\n01:02:03,004 --> 01:02:05,006\r\nWorld\r\n");
    }

    [Fact]
    public void BuildSrtContent_BlankLineInsideCue_IsNormalizedAway()
    {
        // The real bug: an LLM translation with an internal blank line would (with ResegmentSubtitles=Off)
        // terminate the cue early and desync the parser. NormalizeCueText collapses it.
        List<SubtitleData> subs =
        [
            Cue(TimeSpan.Zero, TimeSpan.FromSeconds(1), translated: "line1\n\nline3"),
        ];

        string srt = SrtSubtitleWriter.BuildSrtContent(subs);

        srt.Should().Be("1\r\n00:00:00,000 --> 00:00:01,000\r\nline1\r\nline3\r\n");
        srt.Should().NotContain("\r\n\r\n"); // no blank line inside the single cue
    }

    [Fact]
    public void BuildSrtContent_NullTexts_EmitsEmptyCueText()
    {
        List<SubtitleData> subs =
        [
            Cue(TimeSpan.Zero, TimeSpan.FromSeconds(1), translated: null, text: null),
        ];

        SrtSubtitleWriter.BuildSrtContent(subs)
            .Should().Be("1\r\n00:00:00,000 --> 00:00:01,000\r\n\r\n");
    }

    [Fact]
    public async Task WriteAsync_BlankLineInsideCue_FileHasNoInCueBlankLine()
    {
        // End-to-end at the public boundary: the pre-HC-15 writer wrote DisplayText verbatim (blank line and
        // all). This asserts the read-back file is well-formed SRT — a genuine RED-without-fix on old code.
        string dir = Path.Combine(Path.GetTempPath(), "llp_srt_test_" + Guid.NewGuid().ToString("N"));
        string outPath = Path.Combine(dir, "video.ru.srt");
        try
        {
            SrtSubtitleWriter writer = new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            List<SubtitleData> subs =
            [
                Cue(TimeSpan.Zero, TimeSpan.FromSeconds(1), translated: "A\n\nB"),
            ];

            await writer.WriteAsync(subs, outPath, overwrite: false, TestContext.Current.CancellationToken);

            string content = await File.ReadAllTextAsync(outPath, new UTF8Encoding(false), TestContext.Current.CancellationToken);
            content.Should().Be("1\r\n00:00:00,000 --> 00:00:01,000\r\nA\r\nB\r\n");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_OverwriteTrue_ReplacesExisting_AndLeavesNoTempFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "llp_srt_test_" + Guid.NewGuid().ToString("N"));
        string outPath = Path.Combine(dir, "video.ru.srt");
        try
        {
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(outPath, "stale", TestContext.Current.CancellationToken);

            SrtSubtitleWriter writer = new(new UTF8Encoding(false));
            List<SubtitleData> subs =
            [
                Cue(TimeSpan.Zero, TimeSpan.FromSeconds(1), translated: "Fresh"),
            ];

            await writer.WriteAsync(subs, outPath, overwrite: true, TestContext.Current.CancellationToken);

            (await File.ReadAllTextAsync(outPath, TestContext.Current.CancellationToken))
                .Should().Be("1\r\n00:00:00,000 --> 00:00:01,000\r\nFresh\r\n");
            // The atomic temp file must not linger beside the output.
            Directory.GetFiles(dir).Should().ContainSingle().Which.Should().Be(outPath);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
