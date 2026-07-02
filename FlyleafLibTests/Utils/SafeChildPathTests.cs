using AwesomeAssertions;

namespace FlyleafLib;

// Utils.GetSafeFileNameChildPath resolves an untrusted (API-supplied) file name to a path that always
// stays directly inside the given base directory (HC-05). Directory components, absolute paths and
// traversal sequences must be stripped so the result can never escape baseDir.
public class SafeChildPathTests
{
    static readonly string BaseDir = Path.Combine(Path.GetTempPath(), "llp_hc05_tests");

    [Fact]
    public void PlainName_ResolvesInsideBaseDir()
    {
        string? result = Utils.GetSafeFileNameChildPath(BaseDir, "movie.srt");

        result.Should().NotBeNull();
        Path.GetDirectoryName(result).Should().Be(Path.GetFullPath(BaseDir));
        Path.GetFileName(result).Should().Be("movie.srt");
    }

    [Theory]
    [InlineData("..\\..\\..\\Windows\\evil.srt")]
    [InlineData("../../evil.srt")]
    [InlineData("C:\\Windows\\System32\\evil.srt")]
    public void TraversalOrAbsolutePaths_StayInsideBaseDir(string hostile)
    {
        string? result = Utils.GetSafeFileNameChildPath(BaseDir, hostile);

        result.Should().NotBeNull();
        string baseFull = Path.GetFullPath(BaseDir);
        result!.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            .Should().BeTrue();
        Path.GetDirectoryName(result).Should().Be(baseFull);
        Path.GetFileName(result).Should().Be("evil.srt");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    // Names that Path.GetFileName collapses to "" (trailing separator) must hit the post-strip guard.
    [InlineData("subs\\")]
    [InlineData("..\\")]
    [InlineData("subs/")]
    public void EmptyOrDotNames_ReturnNull(string? name)
    {
        Utils.GetSafeFileNameChildPath(BaseDir, name).Should().BeNull();
    }
}
