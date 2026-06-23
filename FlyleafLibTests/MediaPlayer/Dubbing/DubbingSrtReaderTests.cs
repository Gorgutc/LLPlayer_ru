using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib.MediaPlayer;

public class DubbingSrtReaderTests
{
    [Fact]
    public void Parse_TwoBlocks_ReadsTimingsAndText()
    {
        string srt =
            "1\n" +
            "00:00:01,000 --> 00:00:03,500\n" +
            "Line one\n" +
            "\n" +
            "2\n" +
            "00:00:04,000 --> 00:00:06,000\n" +
            "Line two\n" +
            "second row\n";

        var subs = DubbingSrtReader.Parse(srt);

        subs.Should().HaveCount(2);
        subs[0].StartTime.Should().Be(TimeSpan.FromMilliseconds(1000));
        subs[0].EndTime.Should().Be(TimeSpan.FromMilliseconds(3500));
        subs[0].Text.Should().Be("Line one");
        subs[1].StartTime.Should().Be(TimeSpan.FromSeconds(4));
        subs[1].Text.Should().Be("Line two\nsecond row");
        subs[1].Index.Should().Be(1);
    }

    [Fact]
    public void Parse_CrlfAndDotMillis_Handled()
    {
        string srt = "1\r\n00:00:02.250 --> 00:00:05.000\r\nHi\r\n";
        var subs = DubbingSrtReader.Parse(srt);

        subs.Should().ContainSingle();
        subs[0].StartTime.Should().Be(TimeSpan.FromMilliseconds(2250));
        subs[0].EndTime.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Parse_EmptyOrGarbage_ReturnsEmpty()
    {
        DubbingSrtReader.Parse("").Should().BeEmpty();
        DubbingSrtReader.Parse("not a subtitle\n\njust text").Should().BeEmpty();
    }
}
