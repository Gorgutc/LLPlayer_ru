using System;
using System.Collections.Generic;
using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer.Dubbing;

// F-16 phase 2a: the dub renderer threads an optional per-line voice (SubtitleData.AssignedVoiceId) through
// BuildLines -> DubbingLine.VoiceId -> ResolveVoiceId(..). With no assignments the synthesized voice id is the
// run's DefaultVoiceId for every line, so the dub stays byte-identical to the single-voice render.
public class DubbingRendererTests
{
    private static SubtitleData Cue(string text, double startSec, double endSec, string? voiceId = null) => new()
    {
        Text = text,
        StartTime = TimeSpan.FromSeconds(startSec),
        EndTime = TimeSpan.FromSeconds(endSec),
        AssignedVoiceId = voiceId,
    };

    [Fact]
    public void BuildLines_NoOverrides_AllVoiceIdsNull()
    {
        // Byte-identical guarantee: a track with no per-line assignment carries a null VoiceId on every line, so
        // ResolveVoiceId falls back to the default for all of them.
        List<DubbingLine> lines = DubbingRenderer.BuildLines(new[]
        {
            Cue("one", 0, 1),
            Cue("two", 1, 2),
        });

        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(l => l.VoiceId == null);
    }

    [Fact]
    public void BuildLines_CarriesPerLineOverride()
    {
        List<DubbingLine> lines = DubbingRenderer.BuildLines(new[]
        {
            Cue("one", 0, 1, "ru-preset-1"),
            Cue("two", 1, 2, "custom-voice"),
        });

        lines[0].VoiceId.Should().Be("ru-preset-1");
        lines[1].VoiceId.Should().Be("custom-voice");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildLines_BlankOverride_BecomesNull(string? blank)
    {
        // A blank assignment means "no override"; it must normalize to null so the renderer falls back cleanly.
        List<DubbingLine> lines = DubbingRenderer.BuildLines(new[] { Cue("x", 0, 1, blank) });

        lines.Should().ContainSingle().Which.VoiceId.Should().BeNull();
    }

    [Fact]
    public void BuildLines_TrimsOverride()
    {
        List<DubbingLine> lines = DubbingRenderer.BuildLines(new[] { Cue("x", 0, 1, "  ru-preset-2  ") });

        lines.Should().ContainSingle().Which.VoiceId.Should().Be("ru-preset-2");
    }

    [Fact]
    public void BuildLines_OrdersByStartTime_OverrideTravelsWithItsCue()
    {
        // BuildLines sorts by start time; each per-line voice must stay attached to its own cue after the sort.
        List<DubbingLine> lines = DubbingRenderer.BuildLines(new[]
        {
            Cue("late", 5, 6, "voice-late"),
            Cue("early", 0, 1, "voice-early"),
        });

        lines[0].Text.Should().Be("early");
        lines[0].VoiceId.Should().Be("voice-early");
        lines[1].Text.Should().Be("late");
        lines[1].VoiceId.Should().Be("voice-late");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveVoiceId_BlankOverride_FallsBackToDefault(string? lineVoiceId)
    {
        DubbingRenderer.ResolveVoiceId(lineVoiceId, "ru-preset-1").Should().Be("ru-preset-1");
    }

    [Fact]
    public void ResolveVoiceId_Override_WinsOverDefault()
    {
        DubbingRenderer.ResolveVoiceId("custom-voice", "ru-preset-1").Should().Be("custom-voice");
    }
}
