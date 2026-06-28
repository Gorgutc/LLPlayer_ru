using AwesomeAssertions;

namespace FlyleafLib;

// Utils.FixFileUrl turns a "file:" URL into a local filesystem path (via Uri.LocalPath, which also percent-
// decodes), and otherwise returns the input unchanged. Inputs shorter than 5 chars or null are passed through
// untouched, and any Uri parse failure is swallowed (bare catch) so a malformed URL is returned as-is rather
// than throwing. These tests run on Windows (the only TFM), so LocalPath yields backslash paths.
public class UtilsFixFileUrlTests
{
    [Fact]
    public void Null_IsReturnedUnchanged()
    {
        Utils.FixFileUrl(null!).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("abcd")] // length 4 < 5 guard
    public void ShorterThanFiveChars_IsReturnedUnchanged(string url)
    {
        Utils.FixFileUrl(url).Should().Be(url);
    }

    [Theory]
    [InlineData("http://example.com/a.mp4")]
    [InlineData("https://example.com/file.mp4")]
    [InlineData("C:\\videos\\clip.mkv")]
    public void NonFileScheme_IsReturnedUnchanged(string url)
    {
        Utils.FixFileUrl(url).Should().Be(url);
    }

    [Fact]
    public void FileUrl_IsConvertedToLocalPath()
    {
        Utils.FixFileUrl("file:///C:/path/to/file.txt").Should().Be(@"C:\path\to\file.txt");
    }

    [Fact]
    public void FileScheme_IsMatchedCaseInsensitively()
    {
        Utils.FixFileUrl("FILE:///C:/x.txt").Should().Be(@"C:\x.txt");
    }

    [Fact]
    public void FileUrl_PercentEscapesAreDecoded()
    {
        Utils.FixFileUrl("file:///C:/a%20b.txt").Should().Be(@"C:\a b.txt");
    }
}
