using AwesomeAssertions;

namespace FlyleafLib;

// Pure time-string formatters in Utils.cs (TsToTime / TicksToTime / McsToTime / TicksToTimeMini) had no
// unit coverage. Expected strings are derived from the .NET TimeSpan custom-format specifiers the code
// selects per range, not from running the code. The NoTs sentinel is hardcoded to long.MinValue
// (= Flyleaf.FFmpeg.Raw.NoTs = AV_NOPTS_VALUE) so the test needs no FFmpeg-binding reference.
public class UtilsTimeFormatTests
{
    private const long NoTs = long.MinValue;

    [Theory]
    [InlineData(0, 1, 2, 3, 4, "01:02:03.004")]   // positive < 1 day -> hh:mm:ss.fff
    [InlineData(0, 0, 0, 0, 7, "00:00:00.007")]   // milliseconds only
    [InlineData(2, 3, 4, 5, 6, "2-03:04:05.006")] // >= 1 day -> d-hh:mm:ss.fff
    public void TsToTime_Positive(int d, int h, int m, int s, int ms, string expected)
    {
        Utils.TsToTime(new TimeSpan(d, h, m, s, ms)).Should().Be(expected);
    }

    [Fact]
    public void TsToTime_Zero_DirectCall_HasLeadingMinus()
    {
        // A direct TsToTime(Zero) takes the negative branch (Ticks is not > 0). Callers normally go through
        // the TicksToTime / McsToTime wrappers, which special-case 0 to "00:00:00.000".
        Utils.TsToTime(TimeSpan.Zero).Should().Be("-00:00:00.000");
    }

    [Fact]
    public void TsToTime_NegativeWithinDay()
    {
        Utils.TsToTime(TimeSpan.FromSeconds(-5)).Should().Be("-00:00:05.000");
    }

    [Fact]
    public void TsToTime_NegativeBeyondDay()
    {
        // -(1 day 2 hours) = -26h, TotalDays < -1 -> -d-hh:mm:ss.fff
        Utils.TsToTime(new TimeSpan(1, 2, 0, 0).Negate()).Should().Be("-1-02:00:00.000");
    }

    [Fact]
    public void TicksToTime_Sentinels()
    {
        Utils.TicksToTime(NoTs).Should().Be("-");
        Utils.TicksToTime(0).Should().Be("00:00:00.000");
    }

    [Fact]
    public void TicksToTime_FromSeconds()
    {
        Utils.TicksToTime(TimeSpan.FromSeconds(3).Ticks).Should().Be("00:00:03.000");
    }

    [Fact]
    public void TicksToTime_OneDay()
    {
        Utils.TicksToTime(new TimeSpan(1, 0, 0, 0).Ticks).Should().Be("1-00:00:00.000");
    }

    [Fact]
    public void McsToTime_Sentinels()
    {
        Utils.McsToTime(NoTs).Should().Be("-");
        Utils.McsToTime(0).Should().Be("00:00:00.000");
    }

    [Theory]
    [InlineData(2_500_000, "00:00:02.500")] // 2.5s expressed in microseconds
    [InlineData(1_000, "00:00:00.001")]     // 1ms
    public void McsToTime_FromMicroseconds(long micro, string expected)
    {
        Utils.McsToTime(micro).Should().Be(expected);
    }

    [Fact]
    public void TicksToTimeMini_Sentinels()
    {
        Utils.TicksToTimeMini(NoTs).Should().Be("-");
        Utils.TicksToTimeMini(0).Should().Be("00.000");
    }

    [Theory]
    [InlineData(5, "05.000")]     // < 1 min -> ss.fff
    [InlineData(65, "01:05.000")] // < 1 hour -> mm:ss.fff
    public void TicksToTimeMini_Seconds(int seconds, string expected)
    {
        Utils.TicksToTimeMini(TimeSpan.FromSeconds(seconds).Ticks).Should().Be(expected);
    }

    [Fact]
    public void TicksToTimeMini_Hours()
    {
        Utils.TicksToTimeMini(TimeSpan.FromMinutes(61).Ticks).Should().Be("01:01:00.000");
    }

    [Fact]
    public void TicksToTimeMini_Days()
    {
        Utils.TicksToTimeMini(new TimeSpan(1, 2, 3, 4).Ticks).Should().Be("1-02:03:04.000");
    }

    [Fact]
    public void TicksToTimeMini_NegativeWithinMinute()
    {
        Utils.TicksToTimeMini(TimeSpan.FromSeconds(-5).Ticks).Should().Be("-05.000");
    }

    [Fact]
    public void TicksToTimeMini_NegativeBeyondMinute_AllBranches()
    {
        // The three remaining negative format branches of TsToTimeMini (Utils.cs ~:837-844).
        Utils.TicksToTimeMini(TimeSpan.FromMinutes(-5).Ticks).Should().Be("-05:00.000");               // -mm:ss.fff
        Utils.TicksToTimeMini(TimeSpan.FromHours(-2).Ticks).Should().Be("-02:00:00.000");              // -hh:mm:ss.fff
        Utils.TicksToTimeMini(new TimeSpan(1, 2, 3, 4).Negate().Ticks).Should().Be("-1-02:03:04.000"); // -d-hh:mm:ss.fff
    }
}
