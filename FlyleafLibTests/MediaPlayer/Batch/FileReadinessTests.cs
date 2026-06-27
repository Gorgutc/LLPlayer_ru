using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Batch;

namespace FlyleafLib.MediaPlayer;

public class FileReadinessTests
{
    private static readonly DateTime T = new(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstSighting_IsNotReady()
    {
        FileStabilityState s = FileReadiness.Step(FileStabilityState.Initial, 1000, T, canOpen: true);
        s.IsReady.Should().BeFalse();
        s.StableTicks.Should().Be(0);
    }

    [Fact]
    public void StableAcrossTwoPolls_BecomesReady()
    {
        // first sighting, then two unchanged+openable polls -> ready (RequiredStableTicks == 2)
        FileStabilityState s = FileReadiness.Step(FileStabilityState.Initial, 1000, T, true);
        s = FileReadiness.Step(s, 1000, T, true);
        s.IsReady.Should().BeFalse();
        s = FileReadiness.Step(s, 1000, T, true);
        s.IsReady.Should().BeTrue();
    }

    [Fact]
    public void GrowingSize_NeverReady()
    {
        FileStabilityState s = FileStabilityState.Initial;
        long size = 1000;
        for (int i = 0; i < 5; i++)
        {
            s = FileReadiness.Step(s, size, T.AddSeconds(i), true);
            s.IsReady.Should().BeFalse();
            size += 500; // still copying
        }
    }

    [Fact]
    public void NotOpenable_NeverReady()
    {
        FileStabilityState s = FileStabilityState.Initial;
        for (int i = 0; i < 5; i++)
        {
            s = FileReadiness.Step(s, 1000, T, canOpen: false); // writer still holds it
            s.IsReady.Should().BeFalse();
            s.StableTicks.Should().Be(0);
        }
    }

    [Fact]
    public void ZeroSize_NeverReady()
    {
        FileStabilityState s = FileStabilityState.Initial;
        s = FileReadiness.Step(s, 0, T, true);
        s = FileReadiness.Step(s, 0, T, true);
        s = FileReadiness.Step(s, 0, T, true);
        s.IsReady.Should().BeFalse();
    }

    [Fact]
    public void WriteTimeChange_ResetsStreak()
    {
        FileStabilityState s = FileReadiness.Step(FileStabilityState.Initial, 1000, T, true);
        s = FileReadiness.Step(s, 1000, T, true);            // StableTicks = 1
        s.StableTicks.Should().Be(1);
        s = FileReadiness.Step(s, 1000, T.AddSeconds(3), true); // mtime changed -> reset
        s.StableTicks.Should().Be(0);
        s.IsReady.Should().BeFalse();
    }

    [Fact]
    public void RecoversAfterStallThenSettles()
    {
        // a file that was locked, then settles, eventually becomes ready
        FileStabilityState s = FileReadiness.Step(FileStabilityState.Initial, 1000, T, canOpen: false);
        s.IsReady.Should().BeFalse();
        s = FileReadiness.Step(s, 1000, T, true); // first openable snapshot
        s = FileReadiness.Step(s, 1000, T, true); // tick 1
        s = FileReadiness.Step(s, 1000, T, true); // tick 2 -> ready
        s.IsReady.Should().BeTrue();
    }
}
