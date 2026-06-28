using AwesomeAssertions;

namespace FlyleafLib;

// Utils.GetBytesReadable(nuint) had no coverage. It selects a unit suffix by power-of-two thresholds, bit-shifts,
// divides by 1024 and formats with "0.## ". The fractional path uses the CURRENT culture's decimal separator
// (no InvariantCulture), so to stay culture-independent these tests only assert exact values that land on whole
// numbers (no separator) plus suffix-boundary checks. nuint is not a valid attribute constant, so values come in
// as ulong and are cast inside the test (the project runs win-x64, so nuint is 64-bit).
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
        // Asserting only the suffix keeps the boundary checks free of the culture-dependent decimal separator.
        Utils.GetBytesReadable((nuint)bytes).Should().EndWith(expectedSuffix);
    }
}
