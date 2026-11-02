using System.Buffers.Binary;
using System.Text.Json;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public sealed class PdicClientTests
{
    [Fact]
    public async Task ConstructionAndUnusedShutdownNeverReadPathOrCreateAProcess()
    {
        int reads = 0; PdicFactory factory = new();
        var client = new PdicClient(() => { reads++; return "C:\\PDIC\\pdic-pipe.exe"; }, factory, true);
        await client.DisposeAsync(); await client.DisposeAsync();
        Assert.Equal(0, reads); Assert.Empty(factory.Paths);
    }

    [Fact]
    public async Task LinuxRejectsBeforeReadingPathsOrCreatingProcesses()
    {
        int reads = 0; PdicFactory factory = new();
        await using var client = new PdicClient(() => { reads++; return "C:\\PDIC\\pdic-pipe.exe"; }, factory, false);
        Assert.False(client.IsAvailable);
        var error = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => client.SendAsync("word", 0, TestContext.Current.CancellationToken));
        Assert.Contains("only on Windows", error.Message);
        Assert.Equal(0, reads); Assert.Empty(factory.Paths);
    }

    [Fact]
    public async Task ExactUtf16OffsetAndJsonEscapingAreSentThroughOneLazyProcess()
    {
        PdicFactory factory = new();
        var client = new PdicClient(() => "C:\\PDIC\\pdic-pipe.exe", factory, true);
        const string sentence = "😀 word word \"quote\"\n\\tail";
        await client.SendAsync(sentence, 8, TestContext.Current.CancellationToken);
        await client.SendAsync(sentence, 3, TestContext.Current.CancellationToken);
        Assert.Single(factory.Paths);
        PdicProcess pipe = Assert.Single(factory.Created);
        Assert.Equal(new[]
        {
            "p:Dictionary,SetUrl,LLPlayer", "p:Dictionary,PopupSearch3,9," + sentence,
            "p:Dictionary,SetUrl,LLPlayer", "p:Dictionary,PopupSearch3,4," + sentence,
        }, pipe.Messages);
        Assert.Equal(0, pipe.Disposals);
        await client.DisposeAsync(); await client.DisposeAsync();
        Assert.Equal("p:Dictionary,Close,", pipe.Messages.Last()); Assert.Equal(1, pipe.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SendAsync("word", 0, TestContext.Current.CancellationToken));
        Assert.Single(factory.Paths);
    }

    [Fact]
    public async Task ConcurrentLookupsKeepTheirTwoMessagePairsTogether()
    {
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PdicFactory factory = new();
        factory.Create = () => new PdicProcess { Write = async (count, token) => { if (count == 1) await release.Task.WaitAsync(token); } };
        await using var client = new PdicClient(() => "C:\\PDIC\\pdic-pipe.exe", factory, true);
        Task first = client.SendAsync("first", 0, TestContext.Current.CancellationToken);
        Task second = client.SendAsync("second", 0, TestContext.Current.CancellationToken);
        Assert.Single(Assert.Single(factory.Created).Messages);
        Assert.False(second.IsCompleted);
        release.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.Equal(new[] { "p:Dictionary,SetUrl,LLPlayer", "p:Dictionary,PopupSearch3,1,first",
            "p:Dictionary,SetUrl,LLPlayer", "p:Dictionary,PopupSearch3,1,second" }, factory.Created[0].Messages);
    }

    [Fact]
    public async Task ChangingTheExplicitPathDisposesTheOldOwnedBridgeBeforeTheNextLaunch()
    {
        string path = "C:\\One\\pdic-pipe.exe";
        PdicFactory factory = new();
        await using var client = new PdicClient(() => path, factory, true);
        await client.SendAsync("first", 0, TestContext.Current.CancellationToken);
        path = "C:\\Two\\pdic-pipe.exe";
        await client.SendAsync("second", 0, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "C:\\One\\pdic-pipe.exe", "C:\\Two\\pdic-pipe.exe" }, factory.Paths);
        Assert.Equal(1, factory.Created[0].Disposals); Assert.Equal(0, factory.Created[1].Disposals);
    }

    [Fact]
    public async Task FailedWritesReleaseOnlyTheirBridgeAndTheNextExplicitLookupCanReconnect()
    {
        PdicFactory factory = new();
        factory.Create = () => new PdicProcess { Write = (_, _) => Task.FromException(new IOException("Pipe closed")) };
        await using var client = new PdicClient(() => "C:\\PDIC\\pdic-pipe.exe", factory, true);
        await Assert.ThrowsAsync<IOException>(() => client.SendAsync("first", 0, TestContext.Current.CancellationToken));
        Assert.Equal(1, factory.Created[0].Disposals);
        factory.Create = () => new();
        await client.SendAsync("second", 0, TestContext.Current.CancellationToken);
        Assert.Equal(2, factory.Paths.Count);
    }

    [Fact]
    public async Task ShutdownCancelsAndDrainsTheWriterThenClosesAndReleasesOnlyTheExistingProcess()
    {
        PdicFactory factory = new();
        factory.Create = () => new PdicProcess { Write = async (count, token) => { if (count == 1) await Task.Delay(Timeout.InfiniteTimeSpan, token); } };
        var client = new PdicClient(() => "C:\\PDIC\\pdic-pipe.exe", factory, true);
        Task send = client.SendAsync("pending", 0, TestContext.Current.CancellationToken);
        await client.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        PdicProcess pipe = Assert.Single(factory.Created);
        Assert.Equal("p:Dictionary,Close,", pipe.Messages.Last()); Assert.Equal(1, pipe.Disposals);
    }

    [Theory]
    [InlineData(-1)] [InlineData(1)] [InlineData(20)]
    public async Task MissingOrInvalidOffsetNeverGuessesTheFirstRepeatedWord(int offset)
    {
        PdicFactory factory = new();
        await using var client = new PdicClient(() => "C:\\PDIC\\pdic-pipe.exe", factory, true);
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync("😀 word word", offset, TestContext.Current.CancellationToken));
        Assert.Empty(factory.Paths);
    }

    internal sealed class PdicFactory : IPdicProcessFactory
    {
        public List<string> Paths { get; } = [];
        public List<PdicProcess> Created { get; } = [];
        public Func<PdicProcess> Create { get; set; } = () => new();
        public Task<IPdicProcess> StartAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(path); PdicProcess process = Create(); Created.Add(process);
            return Task.FromResult<IPdicProcess>(process);
        }
    }
    internal sealed class PdicProcess : IPdicProcess
    {
        public List<string> Messages { get; } = [];
        public int Disposals { get; private set; }
        public Func<int, CancellationToken, Task>? Write { get; init; }
        public async Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            Assert.Equal(frame.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(frame.Span));
            Messages.Add(JsonSerializer.Deserialize<string>(frame.Span[4..])!);
            if (Write != null) await Write(Messages.Count, cancellationToken);
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
