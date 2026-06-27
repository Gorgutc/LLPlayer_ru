using System.Text;
using AwesomeAssertions;

namespace FlyleafLib;

// TextEncodings.DetectEncoding (BOM sniffing, then UTF-8 validation, then charset-library fallback) had no
// coverage. Reference code pages: UTF-8 = 65001, UTF-16 LE = 1200, UTF-16 BE = 1201, UTF-32 BE = 12001.
public class TextEncodingsTests
{
    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' }, 65001)]                 // UTF-8 BOM
    [InlineData(new byte[] { 0xFF, 0xFE, (byte)'h', 0x00 }, 1200)]                             // UTF-16 LE BOM
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, (byte)'h' }, 1201)]                             // UTF-16 BE BOM
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF }, 12001)]                                 // UTF-32 BE BOM
    [InlineData(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' }, 65001)]  // ASCII is valid UTF-8
    [InlineData(new byte[] { 0x63, 0x61, 0x66, 0xC3, 0xA9 }, 65001)]                           // "caf" + e-acute, UTF-8 no BOM
    public void DetectEncoding_Bytes_ReturnsExpectedCodePage(byte[] data, int expectedCodePage)
    {
        Encoding? enc = TextEncodings.DetectEncoding(data);

        enc.Should().NotBeNull();
        enc!.CodePage.Should().Be(expectedCodePage);
    }

    [Fact]
    public void DetectEncoding_Utf32LeBom_DetectedAsUtf16Le_OrderingQuirk()
    {
        // FF FE 00 00 is a valid UTF-32 LE BOM, but the UTF-16 LE check (FF FE) runs first and wins, so the
        // data is reported as UTF-16 LE (1200). Characterization of the current BOM-check ordering.
        TextEncodings.DetectEncoding(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })!.CodePage.Should().Be(1200);
    }

    [Fact]
    public void DetectEncoding_Empty_IsUtf8()
    {
        TextEncodings.DetectEncoding(Array.Empty<byte>())!.CodePage.Should().Be(65001);
    }

    [Fact]
    public void DetectEncoding_MaxBytesLargerThanData_IsClampedNoThrow()
    {
        Encoding? enc = TextEncodings.DetectEncoding(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'x' }, maxBytes: 1000);

        enc!.CodePage.Should().Be(65001);
    }

    [Fact]
    public void DetectEncoding_Path_ReadsBomFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' });
            TextEncodings.DetectEncoding(path)!.CodePage.Should().Be(65001);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DetectEncoding_Path_MissingFile_ReturnsNull()
    {
        string missing = Path.Combine(Path.GetTempPath(), "llplayer-no-such-file-" + Guid.NewGuid().ToString("N") + ".txt");

        TextEncodings.DetectEncoding(missing).Should().BeNull();
    }
}
