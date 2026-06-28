using AwesomeAssertions;

namespace FlyleafLib;

// Pure arithmetic helpers in Utils.cs (Align / FFALIGN / Scale / SnapToInt / GCD) had no unit coverage.
// Every expected value is derived from the arithmetic each method performs, not from running the code.
public class UtilsMathTests
{
    [Theory]
    [InlineData(10, 4, 12)] // 10 % 4 = 2 -> 10 + (4 - 2) = 12
    [InlineData(8, 8, 8)]   // already a multiple -> unchanged
    [InlineData(16, 8, 16)] // already a multiple -> unchanged
    [InlineData(1, 5, 5)]   // 1 % 5 = 1 -> 1 + (5 - 1) = 5
    [InlineData(13, 8, 16)] // 13 % 8 = 5 -> 13 + (8 - 5) = 16
    [InlineData(0, 4, 0)]   // 0 is a multiple of any align
    public void Align_RoundsUpToMultiple(int num, int align, int expected)
    {
        Utils.Align(num, align).Should().Be(expected);
    }

    [Theory]
    [InlineData(10, 4, 12)]  // (10 + 3) & ~3 = 13 & 0xFFFFFFFC = 12
    [InlineData(8, 8, 8)]    // (8 + 7) & ~7 = 15 & 0xFFFFFFF8 = 8
    [InlineData(16, 8, 16)]  // (16 + 7) & ~7 = 23 & 0xFFFFFFF8 = 16
    [InlineData(1, 16, 16)]  // (1 + 15) & ~15 = 16 & 0xFFFFFFF0 = 16
    [InlineData(17, 16, 32)] // (17 + 15) & ~15 = 32 & 0xFFFFFFF0 = 32
    [InlineData(0, 4, 0)]    // (0 + 3) & ~3 = 0
    public void FFALIGN_PowerOfTwoAlign(int num, int align, int expected)
    {
        // FFALIGN is only defined for power-of-2 align values (per its XML doc), so all inputs use 4/8/16.
        Utils.FFALIGN(num, align).Should().Be(expected);
    }

    [Theory]
    [InlineData(5f, 0f, 10f, 0f, 100f, 50f)]   // (5 * 100 / 10) + 0
    [InlineData(0f, 0f, 10f, 0f, 100f, 0f)]    // lower bound maps to outMin
    [InlineData(10f, 0f, 10f, 0f, 100f, 100f)] // upper bound maps to outMax
    [InlineData(2f, 0f, 4f, 0f, 1f, 0.5f)]     // (2 * 1 / 4) + 0
    [InlineData(75f, 0f, 100f, 0f, 10f, 7.5f)] // (75 * 10 / 100) + 0
    public void Scale_LinearMapping(float value, float inMin, float inMax, float outMin, float outMax, float expected)
    {
        Utils.Scale(value, inMin, inMax, outMin, outMax).Should().BeApproximately(expected, 1e-4f);
    }

    [Fact]
    public void SnapToInt_WithinEpsilon_SnapsToNearest()
    {
        Utils.SnapToInt(5.0000001).Should().Be(5.0);   // |5.0000001 - 5| = 1e-7 < 1e-6
        Utils.SnapToInt(2.9999999).Should().Be(3.0);   // |2.9999999 - 3| = 1e-7 < 1e-6
        Utils.SnapToInt(10.0).Should().Be(10.0);       // already integral
    }

    [Fact]
    public void SnapToInt_BeyondEpsilon_ReturnsValueUnchanged()
    {
        Utils.SnapToInt(5.5).Should().Be(5.5);   // |5.5 - 6| = 0.5 (Math.Round 5.5 -> 6, even) >= 1e-6
        Utils.SnapToInt(2.4).Should().Be(2.4);   // |2.4 - 2| = 0.4 >= 1e-6
    }

    [Theory]
    [InlineData(48, 18, 6)]
    [InlineData(100, 50, 50)]
    [InlineData(17, 19, 1)]  // coprime
    [InlineData(12, 8, 4)]
    [InlineData(0, 5, 5)]    // GCD(0,5): b!=0 -> GCD(5, 0) -> 5
    [InlineData(5, 0, 5)]    // GCD(5,0): b==0 -> 5
    public void GCD_EuclideanResult(int a, int b, int expected)
    {
        Utils.GCD(a, b).Should().Be(expected);
    }
}
