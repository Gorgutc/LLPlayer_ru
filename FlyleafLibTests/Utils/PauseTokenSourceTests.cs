using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AwesomeAssertions;
using FlyleafLib;

namespace FlyleafLib;

public class PauseTokenSourceTests
{
    private static CancellationToken TestCancellation => TestContext.Current.CancellationToken;

    // Asserts a task completes promptly (not via a multi-second framework timeout) so a regression that hangs
    // the gate fails fast and deterministically rather than stalling the suite.
    private static async Task CompletesWithin(Task task, int milliseconds = 2000)
    {
        Task finished = await Task.WhenAny(task, Task.Delay(milliseconds, TestCancellation));
        finished.Should().BeSameAs(task, "the awaited gate task should complete promptly, not hang");
        await task; // observe completion / surface any exception
    }

    [Fact]
    public void IsPaused_TracksPauseAndResume_Idempotently()
    {
        var pts = new PauseTokenSource();

        pts.IsPaused.Should().BeFalse();

        pts.Pause();
        pts.IsPaused.Should().BeTrue();
        pts.Token.IsPaused.Should().BeTrue();

        pts.Pause(); // idempotent
        pts.IsPaused.Should().BeTrue();

        pts.Resume();
        pts.IsPaused.Should().BeFalse();

        pts.Resume(); // idempotent, safe when not paused
        pts.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void IsPausedSetter_PausesAndResumes()
    {
        var pts = new PauseTokenSource();

        pts.IsPaused = true;
        pts.IsPaused.Should().BeTrue();

        pts.IsPaused = false;
        pts.IsPaused.Should().BeFalse();
    }

    [Fact]
    public async Task DefaultToken_IsNeverPaused_AndWaitIsTheCompletedSingleton()
    {
        PauseToken token = default;

        token.IsPaused.Should().BeFalse();

        Task wait = token.WaitWhilePausedAsync(TestCancellation);
        // A default token is an unconditional no-op regardless of any other source state: it hands back the
        // shared completed Task, which is exactly what the batch ASR path relies on.
        wait.Should().BeSameAs(Task.CompletedTask);
        await wait; // must not throw
    }

    [Fact]
    public void NotPaused_WaitReturnsAlreadyCompletedTask()
    {
        var pts = new PauseTokenSource();

        pts.Token.WaitWhilePausedAsync(TestCancellation).IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Paused_WaitSuspends_ThenCompletesOnResume()
    {
        var pts = new PauseTokenSource();
        pts.Pause();

        Task wait = pts.Token.WaitWhilePausedAsync(TestCancellation);
        wait.IsCompleted.Should().BeFalse(); // suspended while paused

        pts.Resume();

        await CompletesWithin(wait); // completes promptly once resumed (not via framework timeout)
        wait.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Paused_WaitThrows_WhenCancelledWhilePaused_AndGateStaysReusable()
    {
        var pts = new PauseTokenSource();
        pts.Pause();

        using var cts = new CancellationTokenSource();
        Task wait = pts.Token.WaitWhilePausedAsync(cts.Token);
        wait.IsCompleted.Should().BeFalse();

        cts.Cancel();

        Func<Task> act = async () => await wait;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Cancellation does not mutate the shared gate: it is still paused and reusable. A fresh, uncancelled
        // wait still completes on the next Resume.
        pts.IsPaused.Should().BeTrue();
        Task fresh = pts.Token.WaitWhilePausedAsync(TestCancellation);
        fresh.IsCompleted.Should().BeFalse();
        pts.Resume();
        await CompletesWithin(fresh);
    }

    [Fact]
    public async Task AlreadyCancelledToken_WhilePaused_Throws()
    {
        var pts = new PauseTokenSource();
        pts.Pause();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => pts.Token.WaitWhilePausedAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Gate_IsReusable_PauseResumePauseResume()
    {
        var pts = new PauseTokenSource();

        pts.Pause();
        Task w1 = pts.Token.WaitWhilePausedAsync(TestCancellation);
        w1.IsCompleted.Should().BeFalse();
        pts.Resume();
        await CompletesWithin(w1);

        // A second pause after resume must suspend a fresh wait independently.
        pts.Pause();
        Task w2 = pts.Token.WaitWhilePausedAsync(TestCancellation);
        w2.IsCompleted.Should().BeFalse();
        pts.Resume();
        await CompletesWithin(w2);

        pts.IsPaused.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentPauseResume_NeverDeadlocksOrLosesWakeups()
    {
        // Stress the CAS state transitions and the await/re-check loop under real thread contention — the gate
        // is toggled by a UI button that can be clicked rapidly while the ASR consumer awaits it.
        var pts = new PauseTokenSource();
        using var stop = new CancellationTokenSource();

        Task[] hammers = Enumerable.Range(0, 6).Select(i => Task.Run(() =>
        {
            var rnd = new Random(i + 1);
            while (!stop.IsCancellationRequested)
            {
                if (rnd.Next(2) == 0)
                    pts.Pause();
                else
                    pts.Resume();
            }
        }, TestCancellation)).ToArray();

        Task[] waiters = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            // The test token only cancels the runner; in normal execution a parked waiter unparks on Resume,
            // so a lost wakeup would hang this loop.
            while (!stop.IsCancellationRequested)
                await pts.Token.WaitWhilePausedAsync(TestCancellation);
        }, TestCancellation)).ToArray();

        await Task.Delay(400, TestCancellation);
        stop.Cancel();
        await CompletesWithin(Task.WhenAll(hammers), 5000);

        // Land in the running state so any waiter parked on the final pause is released, then everyone must exit.
        pts.Resume();
        await CompletesWithin(Task.WhenAll(waiters), 5000);

        pts.IsPaused.Should().BeFalse();
    }

    [Fact]
    public async Task PauseGate_WithBoundedChannel_BackpressuresProducer_ThenDrainsInOrderOnResume()
    {
        // Integration shape mirroring AudioReader.ReadAll: a bounded (capacity-1) channel with a consumer that
        // awaits the pause gate at the chunk boundary before reading. While paused the producer must back-pressure
        // (block on WriteAsync) and nothing is consumed; on resume everything drains in order with no loss.
        var pts = new PauseTokenSource();
        var channel = Channel.CreateBounded<int>(
            new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true });

        pts.Pause();

        var consumed = new List<int>();
        Task consumer = Task.Run(async () =>
        {
            while (await channel.Reader.WaitToReadAsync(TestCancellation))
            {
                await pts.Token.WaitWhilePausedAsync(TestCancellation);
                if (channel.Reader.TryRead(out int v))
                    consumed.Add(v);
            }
        }, TestCancellation);

        Task producer = Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
                await channel.Writer.WriteAsync(i, TestCancellation);
            channel.Writer.Complete();
        }, TestCancellation);

        await Task.Delay(50, TestCancellation);
        producer.IsCompleted.Should().BeFalse("the bounded channel must back-pressure the producer while paused");
        consumed.Should().BeEmpty();

        pts.Resume();

        await CompletesWithin(Task.WhenAll(producer, consumer), 5000);
        consumed.Should().Equal(0, 1, 2); // drained in order, nothing lost or duplicated
    }
}
