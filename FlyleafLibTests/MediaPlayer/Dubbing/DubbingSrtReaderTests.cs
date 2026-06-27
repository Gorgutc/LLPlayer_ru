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

    [Theory]
    [InlineData("5", 500)]   // PadRight('0') -> "500"
    [InlineData("05", 50)]   // -> "050"
    [InlineData("250", 250)]
    [InlineData("1", 100)]   // -> "100"
    public void Parse_PadsFractionalMillis(string msDigits, int expectedMs)
    {
        string srt = $"1\n00:00:00,{msDigits} --> 00:00:09,000\nX\n";

        var subs = DubbingSrtReader.Parse(srt);

        subs.Should().ContainSingle();
        subs[0].StartTime.Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public void Parse_SkipsBlockWithoutTimeline_AndIndexesOnlyAcceptedBlocks()
    {
        string srt =
            "1\n00:00:01,000 --> 00:00:02,000\nFirst\n" +
            "\n" +
            "just a note with no timing\nsecond line\n" +
            "\n" +
            "3\n00:00:05,000 --> 00:00:06,000\nThird\n";

        var subs = DubbingSrtReader.Parse(srt);

        subs.Should().HaveCount(2);
        subs[0].Index.Should().Be(0);
        subs[0].Text.Should().Be("First");
        subs[1].Index.Should().Be(1); // index advances only on accepted blocks, not the skipped middle one
        subs[1].StartTime.Should().Be(TimeSpan.FromSeconds(5));
        subs[1].Text.Should().Be("Third");
    }

    [Fact]
    public void Parse_SkipsBlockWithTimelineButNoText()
    {
        string srt = "1\n00:00:01,000 --> 00:00:02,000\n   \n";

        DubbingSrtReader.Parse(srt).Should().BeEmpty();
    }

    [Fact]
    public void Parse_AcceptsSingleDigitHour()
    {
        string srt = "1\n1:02:03,000 --> 1:02:04,000\nHi\n";

        var subs = DubbingSrtReader.Parse(srt);

        subs.Should().ContainSingle();
        subs[0].StartTime.Should().Be(new TimeSpan(1, 2, 3));
    }
}
