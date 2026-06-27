using AwesomeAssertions;

namespace FlyleafLib;

public class IsVideoExtensionTests
{
    [Theory]
    [InlineData(@"C:\v\movie.mkv", true)]
    [InlineData(@"C:\v\movie.mp4", true)]
    [InlineData(@"C:\v\movie.AVI", true)]   // case-insensitive
    [InlineData(@"C:\v\movie.MOV", true)]
    [InlineData(@"C:\v\subs.srt", false)]
    [InlineData(@"C:\v\movie.mkv.part", false)] // partial download
    [InlineData(@"C:\v\movie.tmp", false)]
    [InlineData(@"C:\v\noext", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsVideoExtension_MatchesKnownContainers(string path, bool expected)
    {
        Utils.IsVideoExtension(path).Should().Be(expected);
    }

    [Fact]
    public void GetMoviesSorted_UsesSamePredicate()
    {
        // The refactor must keep GetMoviesSorted byte-identical in behaviour: only video files pass through.
        List<string> input = [@"C:\v\b.mkv", @"C:\v\a.mp4", @"C:\v\notes.txt", @"C:\v\clip.srt"];
        List<string> result = Utils.GetMoviesSorted(input);

        result.Should().OnlyContain(p => Utils.IsVideoExtension(p));
        result.Should().Contain(@"C:\v\a.mp4");
        result.Should().Contain(@"C:\v\b.mkv");
        result.Should().NotContain(@"C:\v\notes.txt");
        result.Should().NotContain(@"C:\v\clip.srt");
    }
}
