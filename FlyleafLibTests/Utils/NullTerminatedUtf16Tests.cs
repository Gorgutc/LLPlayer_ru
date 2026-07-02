using AwesomeAssertions;

namespace FlyleafLib;

// Utils.ToNullTerminatedUtf16 backs WindowsClipboard.SetText (HC-35): the CF_UNICODETEXT buffer must end
// in '\0'. The buffer is always text.Length + 1 chars with the final char being the null terminator.
public class NullTerminatedUtf16Tests
{
    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Hello, subtitles")]
    public void Buffer_IsOneLonger_AndNullTerminated(string text)
    {
        char[] buffer = Utils.ToNullTerminatedUtf16(text);

        buffer.Length.Should().Be(text.Length + 1);
        buffer[^1].Should().Be('\0');
        new string(buffer, 0, text.Length).Should().Be(text);
    }

    [Fact]
    public void Null_Throws()
    {
        Action act = () => Utils.ToNullTerminatedUtf16(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
