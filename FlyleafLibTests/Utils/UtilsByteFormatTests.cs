using System.Globalization;
using AwesomeAssertions;

namespace FlyleafLib;

// Utils.GetBytesReadable(nuint) selects a unit suffix by power-of-two thresholds, bit-shifts, divides by 1024 and
// formats with "0.## " using InvariantCulture, so the decimal separator is always '.' regardless of the current
// culture (e.g. "1.5 KB", never "1,5 KB" on ru-RU). That lets the fractional cases below assert exact strings
// safely. nuint is not a valid attribute constant, so values come in as ulong and are cast inside the test (the
// project runs win-x64, so nuint is 64-bit).
public class UtilsByteFormatTests
{
    [Theory]
    [InlineData(0UL, "0 B")]
    [InlineData(512UL, "512 B")]
    [InlineData(1023UL, "1023 B")]                  // last value below the KB threshold (0x400)
    [InlineData(1024UL, "1 KB")]                    // 0x400 >> 0 = 1024, /1024 = 1
    [InlineData(2048UL, "2 KB")]
    [InlineData(1048576UL, "1 MB")]                 // 0x100000 >> 10 = 1024, /1024 = 1
    [InlineData(1073741824UL, "1 GB")]             // 0x40000000 >> 20 = 1024, /1024 = 1
    [InlineData(1099511627776UL, "1 TB")]          // 2^40 >> 30 = 1024, /1024 = 1
    [InlineData(1125899906842624UL, "1 PB")]       // 2^50 >> 40 = 1024, /1024 = 1
    [InlineData(1152921504606846976UL, "1 EB")]    // 2^60 >> 50 = 1024, /1024 = 1
    public void GetBytesReadable_WholeNumberValues(ulong bytes, string expected)
    {
        Utils.GetBytesReadable((nuint)bytes).Should().Be(expected);
    }

    [Theory]
    [InlineData(1023UL, "B")]            // below 0x400
    [InlineData(1048575UL, "KB")]        // just below the MB threshold (0x100000)
    [InlineData(1073741823UL, "MB")]     // just below the GB threshold (0x40000000)
    public void GetBytesReadable_SuffixThresholds(ulong bytes, string expectedSuffix)
    {
        Utils.GetBytesReadable((nuint)bytes).Should().EndWith(expectedSuffix);
    }

    [Theory]
    [InlineData(1536UL, "1.5 KB")]          // 1536 / 1024 = 1.5
    [InlineData(1280UL, "1.25 KB")]         // 1280 / 1024 = 1.25
    [InlineData(1572864UL, "1.5 MB")]       // (1572864 >> 10) / 1024 = 1536 / 1024 = 1.5
    public void GetBytesReadable_FractionalValues(ulong bytes, string expected)
    {
        // The format string is "0.## " so up to two decimals with trailing zeros trimmed; the separator is '.'.
        Utils.GetBytesReadable((nuint)bytes).Should().Be(expected);
    }

    [Fact]
    public void GetBytesReadable_FractionalValue_UsesDotSeparator_UnderCommaCulture()
    {
        // Regression guard: ru-RU uses ',' as its decimal separator. Because the method formats with
        // InvariantCulture, the output must still use '.' ("1.5 KB"), not the culture's ',' ("1,5 KB").
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Utils.GetBytesReadable((nuint)1536UL).Should().Be("1.5 KB");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
