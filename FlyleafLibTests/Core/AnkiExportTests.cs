using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Services;
using Microsoft.Data.Sqlite;

namespace FlyleafLibTests.Core;

public sealed class AnkiExportTests
{
    private static readonly SavedWord Word = new("猫", "ねこ", "cat", "a feline", "猫です", "ja", "en", WordSource.WordClick, "2026-10-03T00:00:00Z");

    [Fact]
    public void PackageContainsRealCollectionWithFiveFieldsAndStableGuid()
    {
        byte[] bytes = Assert.IsType<byte[]>(AnkiApkgWriter.TryBuild([Word], "Study"));
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(new[] { "collection.anki2", "media" }, zip.Entries.Select(e => e.FullName).Order().ToArray());
        using (var reader = new StreamReader(zip.GetEntry("media")!.Open())) Assert.Equal("{}", reader.ReadToEnd());
        string path = Path.Combine(Path.GetTempPath(), $"llplayer-apkg-test-{Guid.NewGuid():N}.db");
        try
        {
            zip.GetEntry("collection.anki2")!.ExtractToFile(path);
            using var db = new SqliteConnection($"Data Source={path};Pooling=False");
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            Assert.Equal("ok", cmd.ExecuteScalar());
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            using (var reader = cmd.ExecuteReader())
            {
                var names = new List<string>();
                while (reader.Read()) names.Add(reader.GetString(0));
                Assert.Equal(new[] { "cards", "col", "graves", "notes", "revlog" }, names);
            }
            cmd.CommandText = "SELECT guid,flds,sfld FROM notes";
            using (var reader = cmd.ExecuteReader())
            {
                Assert.True(reader.Read());
                Assert.Equal(AnkiApkgModel.BuildContent([Word], "Study", 1, 1000).Notes[0].Guid, reader.GetString(0));
                Assert.Equal(new[] { "猫", "ねこ", "cat", "a feline", "猫です" }, reader.GetString(1).Split('\x1f'));
                Assert.Equal("猫", reader.GetString(2));
                Assert.False(reader.Read());
            }
            cmd.CommandText = "SELECT count(*) FROM cards c JOIN notes n ON c.nid=n.id WHERE c.ord=0";
            Assert.Equal(1L, cmd.ExecuteScalar());
            cmd.CommandText = "SELECT ver,models,decks FROM col";
            using var col = cmd.ExecuteReader();
            Assert.True(col.Read());
            Assert.Equal(11, col.GetInt32(0));
            Assert.Contains("Study", col.GetString(2));
            using var model = JsonDocument.Parse(col.GetString(1));
            Assert.Equal(5, model.RootElement.EnumerateObject().Single().Value.GetProperty("flds").GetArrayLength());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0, "version probe")]
    [InlineData(1, "create the deck")]
    [InlineData(2, "create the note type")]
    public async Task PreparationErrorsStopBeforeAddNotes(int failure, string message)
    {
        using var handler = new FakeHandler((index, _) => Task.FromResult(Reply(index == failure ? "{\"result\":null,\"error\":\"denied\"}" : Success(index))));
        using var http = new HttpClient(handler);
        var result = await new AnkiConnectSender(http).PushAsync([Word], "Study", TestContext.Current.CancellationToken);
        Assert.Contains(message, result.Error);
        Assert.Equal(failure + 1, handler.Actions.Count);
    }

    [Theory]
    [InlineData(1, "{\"result\":null,\"error\":null}")]
    [InlineData(1, "{\"result\":-1,\"error\":null}")]
    [InlineData(2, "{\"result\":6,\"error\":null}")]
    public async Task InvalidPreparationResultStopsBeforeNotes(int failure, string reply)
    {
        using var handler = new FakeHandler((index, _) => Task.FromResult(Reply(index == failure ? reply : Success(index))));
        using var http = new HttpClient(handler);
        Assert.NotNull((await new AnkiConnectSender(http).PushAsync([Word], "Study", TestContext.Current.CancellationToken)).Error);
        Assert.Equal(failure + 1, handler.Actions.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"result\":5,\"error\":null}")]
    public async Task MalformedOrOldVersionStopsBeforeDeckCreation(string reply)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Reply(reply)));
        using var http = new HttpClient(handler);
        Assert.NotNull((await new AnkiConnectSender(http).PushAsync([Word], "Study", TestContext.Current.CancellationToken)).Error);
        Assert.Single(handler.Actions);
    }

    [Fact]
    public async Task ExistingModelAndDuplicateNotesAreReported()
    {
        using var handler = new FakeHandler((index, _) => Task.FromResult(Reply(index switch
        {
            2 => "{\"result\":null,\"error\":\"Model name already exists\"}",
            3 => "{\"result\":[123,null],\"error\":null}",
            _ => "{\"result\":6,\"error\":null}"
        })));
        using var http = new HttpClient(handler);
        var result = await new AnkiConnectSender(http).PushAsync([Word, Word with { Term = "犬" }], " Study ", TestContext.Current.CancellationToken);
        Assert.Null(result.Error);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(new[] { "version", "createDeck", "createModel", "addNotes" }, handler.Actions);
    }

    [Theory]
    [InlineData("{\"result\":[false],\"error\":null}")]
    [InlineData("{\"result\":[],\"error\":null}")]
    [InlineData("{\"result\":[123,456],\"error\":null}")]
    [InlineData("{\"result\":[-1],\"error\":null}")]
    [InlineData("{\"result\":[1.5],\"error\":null}")]
    [InlineData("{}")]
    [InlineData("{\"result\":null,\"error\":null}")]
    [InlineData("{\"result\":null,\"error\":\"notes denied\"}")]
    public async Task InvalidAddNotesReplyReportsError(string body)
    {
        using var handler = new FakeHandler((index, _) => Task.FromResult(Reply(index == 3 ? body : Success(index))));
        using var http = new HttpClient(handler);
        Assert.NotNull((await new AnkiConnectSender(http).PushAsync([Word], "Study", TestContext.Current.CancellationToken)).Error);
        Assert.Equal(4, handler.Actions.Count);
    }

    [Fact]
    public async Task EmptyListDoesNotSendHttp()
    {
        using var handler = new FakeHandler((_, _) => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        Assert.NotNull((await new AnkiConnectSender(http).PushAsync([], "Study", TestContext.Current.CancellationToken)).Error);
        Assert.Empty(handler.Actions);
    }

    [Fact]
    public async Task TransportFailureAndTimeoutFailSoft()
    {
        foreach (Exception error in new Exception[] { new HttpRequestException(), new TaskCanceledException() })
        {
            using var handler = new FakeHandler((_, _) => Task.FromException<HttpResponseMessage>(error));
            using var http = new HttpClient(handler);
            Assert.NotNull((await new AnkiConnectSender(http).PushAsync([Word], "Study", TestContext.Current.CancellationToken)).Error);
        }
    }

    [Fact]
    public async Task UserCancellationPropagates()
    {
        using var handler = new FakeHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Reply(""); });
        using var http = new HttpClient(handler);
        using var cts = new CancellationTokenSource();
        var push = new AnkiConnectSender(http).PushAsync([Word], "Study", cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => push);
    }

    private static string Success(int index) => index switch
    {
        2 => "{\"result\":{\"id\":1,\"name\":\"LLPlayer\"},\"error\":null}",
        3 => "{\"result\":[123],\"error\":null}",
        _ => "{\"result\":6,\"error\":null}"
    };

    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<string> Actions { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(new Uri(AnkiConnectRequests.DefaultUrl), request.RequestUri);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            int index = Actions.Count;
            Actions.Add(json.RootElement.GetProperty("action").GetString()!);
            return await response(index, token);
        }
    }
}
