using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer.Dubbing;

public class DubbingVoiceAssignmentMapTests
{
    [Fact]
    public void Linux_case_distinct_media_do_not_share_voice_assignments()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows paths intentionally preserve case-insensitive matching.");
        string media = Path.Combine(Path.GetTempPath(), "Film.mkv");
        string other = Path.Combine(Path.GetTempPath(), "film.mkv");
        var map = DubbingVoiceAssignmentMap.FromCurrentSubtitles(media, [Cue("line", 1, 2, "film-voice")]);
        SubtitleData target = Cue("line", 1, 2, null);
        map.Apply(other, [target]);
        target.AssignedVoiceId.Should().BeNull();
        map.Apply(media, [target]);
        target.AssignedVoiceId.Should().Be("film-voice");
    }

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
