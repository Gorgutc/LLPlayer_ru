using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

public class AiTranscriptTests
{
    [Fact]
    public void Build_JoinsCuesInOrder_OnePerLine()
    {
        string t = AiTranscript.Build(["Hello there", "How are you?", "Fine"]);
        t.Should().Be("Hello there\nHow are you?\nFine");
    }

    [Fact]
    public void Build_SkipsNullAndBlankCues()
    {
        string t = AiTranscript.Build(["A", null, "   ", "", "B"]);
        t.Should().Be("A\nB");
    }

    [Fact]
    public void Build_CollapsesMultilineCue_ToSingleLine()
    {
        string t = AiTranscript.Build(["first line\r\nsecond line", "next"]);
        t.Should().Be("first line second line\nnext");
    }

    [Fact]
    public void Build_Empty_ReturnsEmpty()
    {
        AiTranscript.Build([]).Should().BeEmpty();
        AiTranscript.Build([null, "  "]).Should().BeEmpty();
    }

    [Fact]
    public void Chunk_UnderBudget_SingleChunk()
    {
        string transcript = AiTranscript.Build(["one", "two", "three"]);
        var chunks = AiTranscript.Chunk(transcript, 100, out bool partial);

        chunks.Should().ContainSingle().Which.Should().Be("one\ntwo\nthree");
        partial.Should().BeFalse();
    }

    [Fact]
    public void Chunk_NeverSplitsMidCue_AndRespectsBudget()
    {
        // Each cue is 10 chars; budget 25 should pack ~2 cues per chunk, always at line boundaries.
        List<string> cues = Enumerable.Range(0, 6).Select(i => new string((char)('a' + i), 10)).ToList();
        string transcript = AiTranscript.Build(cues);

        var chunks = AiTranscript.Chunk(transcript, 25, out bool partial);

        partial.Should().BeFalse();
        chunks.Count.Should().BeGreaterThan(1);
        foreach (string chunk in chunks)
        {
            chunk.Length.Should().BeLessThanOrEqualTo(25);
            // every line in every chunk is one of the original whole cues (never split mid-cue)
            foreach (string line in chunk.Split('\n'))
                cues.Should().Contain(line);
        }
        // No cue is lost: re-joining all chunks reproduces the transcript.
        string.Join("\n", chunks.SelectMany(c => c.Split('\n'))).Should().Be(transcript);
    }

    [Fact]
    public void Chunk_SingleCueLongerThanBudget_BecomesItsOwnChunk_NotSplit()
    {
        string longCue = new('x', 50);
        var chunks = AiTranscript.Chunk(longCue, 20, out bool partial);

        chunks.Should().ContainSingle().Which.Should().Be(longCue); // not split mid-word
        partial.Should().BeFalse();
    }

    [Fact]
    public void Chunk_Empty_ReturnsEmpty()
    {
        AiTranscript.Chunk("", 100, out bool partial).Should().BeEmpty();
        partial.Should().BeFalse();
    }

    [Fact]
    public void Chunk_OverMaxChunks_SamplesAndFlagsPartialCoverage()
    {
        // 40 cues of 30 chars at a tiny budget forces far more than MaxChunks natural chunks.
        List<string> cues = Enumerable.Range(0, 40).Select(i => $"cue-{i:D2}-" + new string('y', 24)).ToList();
        string transcript = AiTranscript.Build(cues);

        var chunks = AiTranscript.Chunk(transcript, 30, out bool partial);

        partial.Should().BeTrue();
        chunks.Count.Should().BeLessThanOrEqualTo(AiInsightBudget.MaxChunks);
        chunks.Count.Should().BeGreaterThan(0);
    }
}
