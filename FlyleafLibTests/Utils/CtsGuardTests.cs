using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace FlyleafLib;

public class CtsGuardTests
{
    [Fact]
    public void CancelCaptured_NullSlot_ReturnsNull()
    {
        CancellationTokenSource? slot = null;

        CtsGuard.CancelCaptured(ref slot).Should().BeNull();
    }

    [Fact]
    public void CancelCaptured_CancelsAndReturnsCurrent_WithoutClearing()
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? slot = cts;

        CancellationTokenSource? captured = CtsGuard.CancelCaptured(ref slot);

        captured.Should().BeSameAs(cts);
        cts.IsCancellationRequested.Should().BeTrue();
        slot.Should().BeSameAs(cts); // capture cancels but does not clear the slot (dispose happens under the lock)

        cts.Dispose();
    }

    [Fact]
    public void CancelCaptured_AlreadyDisposed_DoesNotThrow()
    {
        var cts = new CancellationTokenSource();
        cts.Dispose();
        CancellationTokenSource? slot = cts;

        // Cancel() on a disposed CTS throws ObjectDisposedException; CancelCaptured swallows it (a concurrent
        // teardown may have disposed the CTS between the read and the cancel).
        CancellationTokenSource? captured = CtsGuard.CancelCaptured(ref slot);

        captured.Should().BeSameAs(cts);
    }

    [Fact]
    public void TryDisposeAndClear_SlotMatchesCaptured_DisposesAndClears()
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? slot = cts;

        CtsGuard.TryDisposeAndClear(ref slot, cts).Should().BeTrue();

        slot.Should().BeNull();
        Action useAfterDispose = () => cts.Token.Register(() => { });
        useAfterDispose.Should().Throw<ObjectDisposedException>(); // proves it was disposed
    }

    [Fact]
    public void TryDisposeAndClear_SlotReplacedByFreshCts_DoesNotClobber()
    {
        // RED-without-fix: the original teardown unconditionally nulled the slot, clobbering a CTS a concurrent
        // Do() had freshly installed. Compare-and-clear must leave the fresh one alone and not dispose it.
        var captured = new CancellationTokenSource();
        var fresh = new CancellationTokenSource();
        CancellationTokenSource? slot = fresh;

        CtsGuard.TryDisposeAndClear(ref slot, captured).Should().BeFalse();

        slot.Should().BeSameAs(fresh);
        fresh.Token.Register(() => { }); // fresh must NOT be disposed -> no throw

        captured.Dispose();
        fresh.Dispose();
    }

    [Fact]
    public void TryDisposeAndClear_NullCaptured_ReturnsFalse()
    {
        CancellationTokenSource? slot = null;

        CtsGuard.TryDisposeAndClear(ref slot, null).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentTeardown_ExactlyOneClears_NeverThrows()
    {
        // Stress the exact call-site pattern: each round installs a CTS, then several threads concurrently tear it
        // down (capture+cancel outside the lock, compare-and-clear inside). Exactly one teardown per round must
        // clear the slot and none may throw (NRE / ObjectDisposedException / double-dispose).
        var gate = new object();
        CancellationTokenSource? slot = null;
        CancellationToken ct = TestContext.Current.CancellationToken;

        for (int round = 0; round < 400; round++)
        {
            lock (gate)
            {
                slot = new CancellationTokenSource();
            }

            int cleared = 0;
            using var barrier = new Barrier(4);
            var tasks = new Task[4];
            for (int t = 0; t < tasks.Length; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    barrier.SignalAndWait(ct);
                    CancellationTokenSource? cts = CtsGuard.CancelCaptured(ref slot);
                    if (cts == null)
                        return;

                    lock (gate)
                    {
                        if (CtsGuard.TryDisposeAndClear(ref slot, cts))
                            Interlocked.Increment(ref cleared);
                    }
                }, ct);
            }

            await Task.WhenAll(tasks);

            cleared.Should().Be(1);
            slot.Should().BeNull();
        }
    }
}
