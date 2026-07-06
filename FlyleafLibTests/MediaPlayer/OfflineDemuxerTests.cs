using System.Threading;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib;

public class OfflineDemuxerTests
{
    [Fact]
    public void DisposeIsolated_UnsubscribesCancellationCallback()
    {
        using var cts = new CancellationTokenSource();
        int interrupts = 0;
        bool interruptCleared = false;
        bool demuxerDisposed = false;

        CancellationTokenRegistration registration = OfflineDemuxer.RegisterInterruptForTest(
            cts.Token,
            () => interrupts++);
        OfflineDemuxer.IsolatedDemuxer isolatedDemuxer = new(
            registration,
            () => interruptCleared = true,
            () => demuxerDisposed = true);

        OfflineDemuxer.DisposeIsolated(isolatedDemuxer);
        cts.Cancel();

        interrupts.Should().Be(0);
        interruptCleared.Should().BeTrue();
        demuxerDisposed.Should().BeTrue();
    }
}
