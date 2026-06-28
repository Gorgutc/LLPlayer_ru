using AwesomeAssertions;

namespace FlyleafLib;

// Pure string helpers in Utils.cs (TruncateString / GetUrlExtention / LowerCaseFirstChar / ToHexadecimal /
// DoubleToTimeMini / GetValidFileName) had no unit coverage. Expected values are derived from each method's
// documented behaviour (substring math, LastIndexOf, the "x2"/"#.000" format specifiers, the invalid-char set).
public class UtilsStringTests
{
    [Theory]
    [InlineData("hello", 10, "hello")]            // length 5 <= 10 -> unchanged
    [InlineData("hello world", 8, "hello...")]    // available = 8 - 3 = 5 -> "hello" + "..."
    [InlineData("hello world", 3, "...")]         // available = 0 -> suffix[0..min(3,3)]
    [InlineData("hi", 1, ".")]                    // available = 1 - 3 < 0 -> suffix[0..min(1,3)]
    [InlineData("", 5, "")]                       // empty short-circuit
    [InlineData(null, 5, null)]                   // null short-circuit
    public void TruncateString_DefaultSuffix(string? str, int maxLength, string? expected)
    {
        Utils.TruncateString(str, maxLength).Should().Be(expected);
    }

    [Theory]
    [InlineData("HelloWorld", 5, ">>", "Hel>>")] // available = 5 - 2 = 3 -> first 3 chars + ">>"
    [InlineData("abc", 2, ">>>>", ">>")]         // available = 2 - 4 < 0 -> suffix[0..Min(2,4)] = ">>"
    public void TruncateString_CustomSuffix(string str, int maxLength, string suffix, string expected)
    {
        Utils.TruncateString(str, maxLength, suffix).Should().Be(expected);
    }

    [Theory]
    [InlineData("file.mkv", "mkv")]
    [InlineData("path/to/file.MP4", "mp4")]                 // lower-cased
    [InlineData("archive.tar.gz", "gz")]                    // only after the last dot
    [InlineData("http://example.com/file.PDF", "pdf")]
    [InlineData("file", "")]                                // no dot
    [InlineData(".hidden", "")]                             // dot at index 0 is not > 0
    public void GetUrlExtention_ReturnsLowercasedTrailingExtension(string url, string expected)
    {
        Utils.GetUrlExtention(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("Hello", "hello")]
    [InlineData("WORLD", "wORLD")] // only the first char is touched
    [InlineData("X", "x")]
    [InlineData("already", "already")]
    public void LowerCaseFirstChar_LowersOnlyFirstChar(string input, string expected)
    {
        Utils.LowerCaseFirstChar(input).Should().Be(expected);
    }

    [Fact]
    public void LowerCaseFirstChar_Empty_Throws()
    {
        // Documents the contract: the method stackallocs input.Length chars then writes buffer[0],
        // so an empty input indexes an empty span and throws (callers null/empty-check beforehand).
        Action act = () => Utils.LowerCaseFirstChar("");
        act.Should().Throw<IndexOutOfRangeException>();
    }

    [Fact]
    public void ToHexadecimal_LowercaseZeroPadded()
    {
        Utils.ToHexadecimal([0xFF, 0x00, 0x0A]).Should().Be("ff000a");
        Utils.ToHexadecimal([0xDE, 0xAD, 0xBE, 0xEF]).Should().Be("deadbeef");
        Utils.ToHexadecimal([0x01]).Should().Be("01");
        Utils.ToHexadecimal([]).Should().Be("");
    }

    [Theory]
    [InlineData(2.5, "2.500")]
    [InlineData(0.0, ".000")]       // "#" omits the lone leading zero, ".000" keeps three decimals
    [InlineData(12.3456, "12.346")] // 4th decimal 6 -> rounds UP (truncation would give 12.345)
    [InlineData(12.3453, "12.345")] // 4th decimal 3 -> rounds DOWN (contrast proves it rounds, not truncates)
    [InlineData(-1.5, "-1.500")]
    [InlineData(10.0, "10.000")]
    public void DoubleToTimeMini_InvariantThreeDecimals(double d, string expected)
    {
        // DoubleToTimeMini pins CultureInfo.InvariantCulture, so the decimal point is always '.'
        Utils.DoubleToTimeMini(d).Should().Be(expected);
    }

    [Theory]
    [InlineData("clean_name.txt", "clean_name.txt")] // no invalid chars (underscore is valid)
    [InlineData("a:b", "a_b")]                       // ':' invalid on Windows
    [InlineData("a/b", "a_b")]                       // '/' invalid
    [InlineData("a\\b", "a_b")]                      // '\\' invalid
    [InlineData("a<>b", "a__b")]                     // two adjacent invalids -> two underscores
    [InlineData("a*b?c", "a_b_c")]                   // '*' and '?' invalid
    public void GetValidFileName_ReplacesInvalidCharsWithUnderscore(string name, string expected)
    {
        // Path.GetInvalidFileNameChars() is the Windows set here (':','/','\\','*','?','"','<','>','|', controls).
        Utils.GetValidFileName(name).Should().Be(expected);
    }
}
