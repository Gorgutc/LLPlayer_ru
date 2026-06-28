using AwesomeAssertions;

namespace FlyleafLib;

// Utils.GetMediaParts parses Season/Episode/Year/Title/Extension out of a media file name via the RxSeasonEpisode*/
// RxEpisodePart/RxYear/RxResolution regexes. Expectations below are derived from those regex definitions and the
// method's control flow, not from running the code.
//
// Two control-flow facts drive the expected values:
//   * When a season/episode regex matches at index 0 (or checkSeasonEpisodeOnly is set), the method returns early
//     with ONLY Season/Episode populated - Extension/Year/Title stay at their defaults ("" / 0 / "").
//   * When no S/E regex matches, .NET returns Match.Empty (Groups.Count == 1), so the "res.Groups.Count > 1"
//     block is skipped and the method falls through to Year/Title/Extension extraction.
public class UtilsMediaPartsTests
{
    [Theory]
    // title before the marker -> Title extracted, dots become spaces
    [InlineData("Show.Name.S01E02.mkv", false, 1, 2, 0, "Show Name", "mkv")]
    [InlineData("Show.S01E02.mkv", false, 1, 2, 0, "Show", "mkv")]
    // year sits between title and the S/E marker -> Year captured, title trimmed to before the year
    [InlineData("Show.2015.S01E02.mkv", false, 1, 2, 2015, "Show", "mkv")]
    // no S/E marker -> falls through: Year + Title extracted, no early return
    [InlineData("Movie Name 2015.mp4", false, 0, 0, 2015, "Movie Name", "mp4")]
    // no S/E marker, RxResolution noise boundary trims the title at ".1080p" (1080 is not a year)
    [InlineData("Show.Name.1080p.mkv", false, 0, 0, 0, "Show Name", "mkv")]
    // no S/E marker, RxExtended noise boundary trims the title at ".Extended"
    [InlineData("Movie.Extended.mkv", false, 0, 0, 0, "Movie", "mkv")]
    // episode regex matches at index > 0 -> NOT an early return, so Title/Extension ARE populated
    [InlineData("Show episode 05.mkv", false, 0, 5, 0, "Show", "mkv")]
    // S/E regex matches at index 0 -> early return, Extension/Title NOT populated
    [InlineData("01x05", false, 1, 5, 0, "", "")]
    [InlineData("episode 05.txt", false, 0, 5, 0, "", "")]
    // checkSeasonEpisodeOnly forces the early return even though a real title exists
    [InlineData("Show.S01E02.mkv", true, 1, 2, 0, "", "")]
    public void GetMediaParts_DerivesFieldsFromName(
        string title, bool seasonEpisodeOnly, int season, int episode, int year, string expectedTitle, string expectedExt)
    {
        var mp = Utils.GetMediaParts(title, seasonEpisodeOnly);

        mp.Season.Should().Be(season);
        mp.Episode.Should().Be(episode);
        mp.Year.Should().Be(year);
        mp.Title.Should().Be(expectedTitle);
        mp.Extension.Should().Be(expectedExt);
    }
}
