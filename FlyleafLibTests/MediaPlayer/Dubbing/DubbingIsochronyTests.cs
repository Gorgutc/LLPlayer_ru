using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib.MediaPlayer;

public class DubbingIsochronyTests
{
    [Fact]
    public void ComputeAtempo_ClipWithinSlot_ReturnsOne()
    {
        DubbingIsochrony.ComputeAtempo(clipMs: 500, slotMs: 1000, min: 0.9, max: 1.15).Should().Be(1.0);
    }

    [Fact]
    public void ComputeAtempo_OverflowWithinCap_ReturnsExactFactor()
    {
        // 1100ms into a 1000ms slot => 1.1x (within [0.9, 1.15]).
        DubbingIsochrony.ComputeAtempo(1100, 1000, 0.9, 1.15).Should().BeApproximately(1.1, 1e-9);
    }

    [Fact]
    public void ComputeAtempo_OverflowBeyondCap_ClampsToMax()
    {
        // 2000ms into a 1000ms slot would need 2.0x, but the cap is 1.15x.
        DubbingIsochrony.ComputeAtempo(2000, 1000, 0.9, 1.15).Should().Be(1.15);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1000, 0)]
    [InlineData(-5, 1000)]
    public void ComputeAtempo_NonPositiveInputs_ReturnsOne(double clipMs, double slotMs)
    {
        DubbingIsochrony.ComputeAtempo(clipMs, slotMs, 0.9, 1.15).Should().Be(1.0);
    }

    [Fact]
    public void ComputeAtempo_SwappedMinMax_StillClampsCorrectly()
    {
        DubbingIsochrony.ComputeAtempo(2000, 1000, min: 1.15, max: 0.9).Should().Be(1.15);
    }

    [Fact]
    public void Place_NonOverlappingLines_KeepsSourceStarts()
    {
        DubbingLine[] lines =
        [
            new(0, "a", TimeSpan.Zero, TimeSpan.FromMilliseconds(1000)),
            new(1, "b", TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(2500))
        ];
        double[] rendered = [1000, 500];

        var placed = DubbingIsochrony.Place(lines, rendered);

        placed[0].StartMs.Should().Be(0);
        placed[0].EndMs.Should().Be(1000);
        placed[1].StartMs.Should().Be(2000); // pause >= 300ms => no drift
        placed[1].EndMs.Should().Be(2500);
    }

    [Fact]
    public void Place_OverflowWithoutPause_AccumulatesDrift()
    {
        DubbingLine[] lines =
        [
            new(0, "a", TimeSpan.Zero, TimeSpan.FromMilliseconds(1000)),
            new(1, "b", TimeSpan.FromMilliseconds(1050), TimeSpan.FromMilliseconds(1600))
        ];
        // First clip renders long (1500ms); the 50ms source gap is below the 300ms pause threshold.
        double[] rendered = [1500, 500];

        var placed = DubbingIsochrony.Place(lines, rendered);

        placed[0].EndMs.Should().Be(1500);
        placed[1].StartMs.Should().Be(1500); // pushed by the previous overrun (drift)
    }

    [Fact]
    public void Place_OverflowWithPause_ResetsDrift()
    {
        DubbingLine[] lines =
        [
            new(0, "a", TimeSpan.Zero, TimeSpan.FromMilliseconds(1000)),
            new(1, "b", TimeSpan.FromMilliseconds(1400), TimeSpan.FromMilliseconds(1900))
        ];
        // First clip renders long (1500ms), but a 400ms source pause precedes line 1 => drift is forgiven.
        double[] rendered = [1500, 500];

        var placed = DubbingIsochrony.Place(lines, rendered);

        placed[1].StartMs.Should().Be(1400); // snapped back to its source start, not 1500
    }

    [Fact]
    public void Place_LengthMismatch_Throws()
    {
        DubbingLine[] lines = [new(0, "a", TimeSpan.Zero, TimeSpan.FromMilliseconds(1000))];
        double[] rendered = [1000, 500];

        Action act = () => DubbingIsochrony.Place(lines, rendered);

        act.Should().Throw<ArgumentException>();
    }
}
