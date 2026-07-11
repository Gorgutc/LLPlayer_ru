using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer;

public class MediaResetStateTests
{
    [Fact]
    public void Begin_StoppedResetInvalidatesGenerationAndMarksResetInProgress()
    {
        MediaResetState state = new();
        long before = state.Generation;

        state.Begin(Status.Stopped);

        state.Generation.Should().Be(before + 1);
        state.IsResetting.Should().BeTrue();

        state.Complete();
        state.IsResetting.Should().BeFalse();
    }

    [Fact]
    public void Begin_EveryFullResetAdvancesGeneration()
    {
        MediaResetState state = new();

        state.Begin(Status.Opening);
        state.Complete();
        long afterOpen = state.Generation;

        state.Begin(Status.Stopped);
        state.Complete();

        state.Generation.Should().Be(afterOpen + 1);
    }
}
