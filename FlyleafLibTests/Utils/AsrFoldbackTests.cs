using AwesomeAssertions;

namespace FlyleafLib;

public class AsrFoldbackTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    [Fact]
    public void Plan_Disabled_NeverFoldsBack()
    {
        var (foldBack, _) = AsrFoldback.Plan(TimeSpan.FromMinutes(40), enabled: false, Threshold);
        foldBack.Should().BeFalse();
    }

    [Fact]
    public void Plan_AtOrBelowThreshold_NeverFoldsBack()
    {
        AsrFoldback.Plan(TimeSpan.FromSeconds(30), enabled: true, Threshold).foldBack.Should().BeFalse();
        AsrFoldback.Plan(TimeSpan.FromSeconds(10), enabled: true, Threshold).foldBack.Should().BeFalse();
    }

    [Fact]
    public void Plan_EnabledPastThreshold_FoldsBackFromZero()
    {
        var (foldBack, floor) = AsrFoldback.Plan(TimeSpan.FromMinutes(40), enabled: true, Threshold);
        foldBack.Should().BeTrue();
        floor.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ReachedStop_NullStop_NeverReached()
    {
        AsrFoldback.ReachedStop(TimeSpan.FromHours(5), null).Should().BeFalse();
    }

    [Theory]
    [InlineData(10, 20, false)]
    [InlineData(20, 20, true)]  // exactly at stop
    [InlineData(25, 20, true)]
    public void ReachedStop_AgainstStopAt(int relSec, int stopSec, bool expected)
    {
        AsrFoldback.ReachedStop(TimeSpan.FromSeconds(relSec), TimeSpan.FromSeconds(stopSec))
            .Should().Be(expected);
    }
}
