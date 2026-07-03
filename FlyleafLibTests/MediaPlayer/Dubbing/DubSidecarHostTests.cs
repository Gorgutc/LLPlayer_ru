using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib.MediaPlayer;

public class DubSidecarHostTests
{
    // HC-30: WaitForExitAsync completing does NOT prove the sidecar died — a caller-cancel or the 120s timeout
    // cancels the linked token and completes the exit-wait while the process is alive. The pure classifier maps
    // (caller-cancel?, process-exited?) to the fault so a cancel becomes Canceled and a timeout gets its own
    // message, instead of both masquerading as "sidecar exited before reporting a port" (a Failed job).

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassifyPortWaitFailure_CallerCanceled_WinsRegardlessOfExit(bool processHasExited)
    {
        // The user asked to stop: the job must be Canceled, not Failed — even if the process also happened to
        // exit in the same instant.
        DubSidecarHost.ClassifyPortWaitFailure(callerCanceled: true, processHasExited)
            .Should().Be(DubSidecarHost.SidecarStartFault.Canceled);
    }

    [Fact]
    public void ClassifyPortWaitFailure_NotCanceled_ProcessAlive_IsTimeout()
    {
        // Not cancelled + still alive => the 120s port-report budget elapsed on a healthy-but-slow process.
        // Before HC-30 this reported "exited before reporting a port" (Failed) on a live process.
        DubSidecarHost.ClassifyPortWaitFailure(callerCanceled: false, processHasExited: false)
            .Should().Be(DubSidecarHost.SidecarStartFault.Timeout);
    }

    [Fact]
    public void ClassifyPortWaitFailure_NotCanceled_ProcessExited_IsExitedEarly()
    {
        DubSidecarHost.ClassifyPortWaitFailure(callerCanceled: false, processHasExited: true)
            .Should().Be(DubSidecarHost.SidecarStartFault.ExitedEarly);
    }

    // Pin the fault -> exception mapping (the behavioural payload of HC-30). Without these, a refactor could
    // silently map Canceled -> TimeoutException / InvalidOperationException and the classifier tests above would
    // still pass, re-introducing the exact "cancel looks like a Failed job" bug the fix removed.
    [Fact]
    public void BuildPortWaitException_Canceled_IsOperationCanceled()
    {
        DubSidecarHost.BuildPortWaitException(DubSidecarHost.SidecarStartFault.Canceled, "unused-stderr")
            .Should().BeOfType<OperationCanceledException>();
    }

    [Fact]
    public void BuildPortWaitException_Timeout_IsTimeoutException_WithBudgetInMessage()
    {
        DubSidecarHost.BuildPortWaitException(DubSidecarHost.SidecarStartFault.Timeout, "unused-stderr")
            .Should().BeOfType<TimeoutException>()
            .Which.Message.Should().Contain("120 seconds");
    }

    [Fact]
    public void BuildPortWaitException_ExitedEarly_IsInvalidOperation_WithStderrDetail()
    {
        DubSidecarHost.BuildPortWaitException(DubSidecarHost.SidecarStartFault.ExitedEarly, "boom-on-stderr")
            .Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("exited before reporting a port").And.Contain("boom-on-stderr");
    }
}
