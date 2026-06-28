using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib;

// Pure truth-table tests for the F-12 A-B repeat decision helpers. Expectations are derived from the spec
// (both points set AND A < B to be enabled; loop back once curTime reaches/passes B), not from a run, so they
// are non-vacuous: a regression that flips a comparison or drops the sentinel guard turns one of these red.
public class AbLoopTests
{
    // 10s / 20s / 15s expressed in 100ns ticks (the Player.CurTime domain).
    private const long A10s = 10L * 10_000_000;
    private const long B20s = 20L * 10_000_000;

    [Fact]
    public void Unset_Sentinel_IsLongMinValue()
        => AbLoop.Unset.Should().Be(long.MinValue);

    // ---- IsEnabled --------------------------------------------------------

    [Fact]
    public void IsEnabled_True_WhenBothSetAndOrdered()
        => AbLoop.IsEnabled(A10s, B20s).Should().BeTrue();

    [Theory]
    [InlineData(long.MinValue, long.MinValue)] // both unset (OFF default)
    [InlineData(long.MinValue, B20s)]          // only B set
    [InlineData(A10s, long.MinValue)]          // only A set
    [InlineData(B20s, A10s)]                   // inverted (A > B)
    [InlineData(A10s, A10s)]                   // equal (A == B)
    public void IsEnabled_False_ForIncompleteOrUnorderedWindow(long a, long b)
        => AbLoop.IsEnabled(a, b).Should().BeFalse();

    // ---- ShouldLoopBack: disabled window never loops (OFF byte-identical) --

    [Theory]
    [InlineData(long.MinValue, long.MinValue, 0)]
    [InlineData(long.MinValue, long.MinValue, long.MaxValue)] // both unset, any playhead
    [InlineData(A10s, long.MinValue, long.MaxValue)]          // only A set, playhead far past
    [InlineData(long.MinValue, B20s, long.MaxValue)]          // only B set
    [InlineData(B20s, A10s, long.MaxValue)]                   // inverted window
    [InlineData(A10s, A10s, long.MaxValue)]                   // zero-length window
    public void ShouldLoopBack_False_WhenWindowDisabled(long a, long b, long cur)
        => AbLoop.ShouldLoopBack(a, b, cur).Should().BeFalse();

    // ---- ShouldLoopBack: enabled window honours the B boundary ------------

    [Fact]
    public void ShouldLoopBack_False_BeforeB()
        => AbLoop.ShouldLoopBack(A10s, B20s, B20s - 1).Should().BeFalse();

    [Fact]
    public void ShouldLoopBack_False_AtA()
        => AbLoop.ShouldLoopBack(A10s, B20s, A10s).Should().BeFalse();

    [Fact]
    public void ShouldLoopBack_True_ExactlyAtB()
        => AbLoop.ShouldLoopBack(A10s, B20s, B20s).Should().BeTrue();

    [Fact]
    public void ShouldLoopBack_True_PastB()
        => AbLoop.ShouldLoopBack(A10s, B20s, B20s + 1).Should().BeTrue();

    [Theory]
    [InlineData(150_000_000, false)] // 15s, before B(20s)
    [InlineData(200_000_000, true)]  // exactly B(20s)
    [InlineData(250_000_000, true)]  // 25s, past B
    public void ShouldLoopBack_BoundaryAroundB(long curTicks, bool expected)
        => AbLoop.ShouldLoopBack(A10s, B20s, curTicks).Should().Be(expected);
}
