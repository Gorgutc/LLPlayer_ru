using System.IO;
using System.Linq;
using AwesomeAssertions;
using FlyleafLib;

namespace FlyleafLib;

// HC-41: the download copy loop shared by the three model/engine download VMs (which live in LLPlayer, an app
// project with no test target). Pinning it here gives the extracted pump a real regression gate.
public class StreamDownloadPumpTests
{
    private static byte[] MakeData(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 251);
        }
        return data;
    }

    [Fact]
    public async Task CopyWithProgressAsync_CopiesEveryByte_AndReturnsTotal()
    {
        // 300 KB spans multiple 128 KB reads.
        byte[] data = MakeData(300_000);
        using MemoryStream source = new(data);
        using MemoryStream dest = new();

        long copied = await StreamDownloadPump.CopyWithProgressAsync(
            source, dest, onProgress: null, TimeSpan.Zero, TestContext.Current.CancellationToken);

        copied.Should().Be(data.Length);
        dest.ToArray().Should().Equal(data);
    }

    [Fact]
    public async Task CopyWithProgressAsync_ReportsMonotonicProgressEndingAtTotal()
    {
        byte[] data = MakeData(300_000);
        using MemoryStream source = new(data);
        using MemoryStream dest = new();

        List<long> progress = new();

        long copied = await StreamDownloadPump.CopyWithProgressAsync(
            source, dest, progress.Add, TimeSpan.Zero, TestContext.Current.CancellationToken);

        copied.Should().Be(data.Length);
        progress.Should().NotBeEmpty();
        progress.Should().BeInAscendingOrder();
        progress.Should().OnlyHaveUniqueItems();      // strictly increasing
        progress.Last().Should().Be(data.Length);
    }

    [Fact]
    public async Task CopyWithProgressAsync_EmptySource_ReturnsZeroAndReportsNothing()
    {
        using MemoryStream source = new([]);
        using MemoryStream dest = new();

        List<long> progress = new();

        long copied = await StreamDownloadPump.CopyWithProgressAsync(
            source, dest, progress.Add, TimeSpan.Zero, TestContext.Current.CancellationToken);

        copied.Should().Be(0);
        progress.Should().BeEmpty();
        dest.Length.Should().Be(0);
    }

    [Fact]
    public async Task CopyWithProgressAsync_CancelledToken_Throws()
    {
        byte[] data = MakeData(300_000);
        using MemoryStream source = new(data);
        using MemoryStream dest = new();

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await StreamDownloadPump.CopyWithProgressAsync(source, dest, null, TimeSpan.Zero, cts.Token));
    }

    [Fact]
    public async Task CopyWithProgressAsync_NullStreams_Throw()
    {
        using MemoryStream real = new(MakeData(16));

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await StreamDownloadPump.CopyWithProgressAsync(
                null!, real, null, TimeSpan.Zero, TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await StreamDownloadPump.CopyWithProgressAsync(
                real, null!, null, TimeSpan.Zero, TestContext.Current.CancellationToken));
    }
}
