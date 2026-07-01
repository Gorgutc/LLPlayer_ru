using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer.Dubbing;

public class DubbingVoiceAssignmentMapTests
{
    [Fact]
    public void Apply_MatchingMediaAndTiming_CopiesVoiceOverride()
    {
        string mediaPath = Path.Combine(Path.GetTempPath(), "movie.mkv");
        IDubbingVoiceAssignmentProvider provider = DubbingVoiceAssignmentMap.FromCurrentSubtitles(
            mediaPath,
            [Cue("source", 1, 2, "voice-a")]);
        SubtitleData target = Cue("translated", 1, 2, null);

        provider.Apply(mediaPath, [target]);

        target.AssignedVoiceId.Should().Be("voice-a");
    }

    [Fact]
    public void Apply_DifferentMedia_DoesNothing()
    {
        IDubbingVoiceAssignmentProvider provider = DubbingVoiceAssignmentMap.FromCurrentSubtitles(
            @"C:\video\movie.mkv",
            [Cue("source", 1, 2, "voice-a")]);
        SubtitleData target = Cue("translated", 1, 2, null);

        provider.Apply(@"C:\video\other.mkv", [target]);

        target.AssignedVoiceId.Should().BeNull();
    }

    [Fact]
    public void Apply_BlankSourceVoice_IsIgnored()
    {
        string mediaPath = Path.Combine(Path.GetTempPath(), "movie.mkv");
        IDubbingVoiceAssignmentProvider provider = DubbingVoiceAssignmentMap.FromCurrentSubtitles(
            mediaPath,
            [Cue("source", 1, 2, "   ")]);
        SubtitleData target = Cue("translated", 1, 2, null);

        provider.Apply(mediaPath, [target]);

        target.AssignedVoiceId.Should().BeNull();
    }

    [Fact]
    public void Apply_DoesNotOverwriteExistingTargetVoice()
    {
        string mediaPath = Path.Combine(Path.GetTempPath(), "movie.mkv");
        IDubbingVoiceAssignmentProvider provider = DubbingVoiceAssignmentMap.FromCurrentSubtitles(
            mediaPath,
            [Cue("source", 1, 2, "voice-a")]);
        SubtitleData target = Cue("translated", 1, 2, "voice-existing");

        provider.Apply(mediaPath, [target]);

        target.AssignedVoiceId.Should().Be("voice-existing");
    }

    [Fact]
    public void Apply_MatchesSrtMillisecondPrecision()
    {
        string mediaPath = Path.Combine(Path.GetTempPath(), "movie.mkv");
        IDubbingVoiceAssignmentProvider provider = DubbingVoiceAssignmentMap.FromCurrentSubtitles(
            mediaPath,
            [Cue("source", 1.1239, 2.9879, "voice-a")]);
        SubtitleData target = Cue("translated", 1.123, 2.987, null);

        provider.Apply(mediaPath, [target]);

        target.AssignedVoiceId.Should().Be("voice-a");
    }

    private static SubtitleData Cue(string text, double startSec, double endSec, string? voiceId) => new()
    {
        Text = text,
        StartTime = TimeSpan.FromSeconds(startSec),
        EndTime = TimeSpan.FromSeconds(endSec),
        AssignedVoiceId = voiceId,
    };
}
