using AwesomeAssertions;

namespace FlyleafLib;

// Utils.IsSafeProcessUrl gates Playlist.Url before it is interpolated into a quoted yt-dlp argument
// (HC-01). It must accept ordinary http/https URLs and reject anything that could break out of the
// surrounding quotes (a literal '"' or '\') or that is not an absolute http/https URL.
public class ProcessUrlSafetyTests
{
    [Theory]
    [InlineData("http://youtube.com/watch?v=abc123")]
    [InlineData("https://example.com/path/video.mp4")]
    [InlineData("https://example.com/a?b=c&d=e")]
    [InlineData("HTTPS://Example.COM/x")]
    // A legitimate URL percent-encodes '"' and '\'; the encoded form must NOT be over-rejected (frozen contract).
    [InlineData("https://example.com/a%22b%5Cc")]
    public void ValidHttpUrls_AreAccepted(string url)
    {
        Utils.IsSafeProcessUrl(url).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/only")]
    [InlineData("ftp://example.com/x")]
    [InlineData("file:///C:/x")]
    public void MissingOrNonHttp_AreRejected(string? url)
    {
        Utils.IsSafeProcessUrl(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://x/\"--exec=calc.exe")] // '"' breaks out of the quoted argument -> flag injection
    [InlineData("https://x/a\\b")]              // backslash can escape the closing quote
    [InlineData("http://x/a\nb")]               // control character
    public void QuoteBackslashOrControlChars_AreRejected(string url)
    {
        Utils.IsSafeProcessUrl(url).Should().BeFalse();
    }
}
