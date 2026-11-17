using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Services;
using LLPlayer.Services;

namespace FlyleafLibTests.Automation;

public sealed class ControlWordOperationTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("llplayer-word-control-").FullName;

    [Fact]
    public void Word_listing_refreshes_through_the_existing_owner_and_checks_cursor_revision()
    {
        string path = Path.Combine(directory, "words.json");
        var library = new WordLibrary(path);
        Assert.True(library.TryAdd(Word("One")));
        Assert.True(library.TryAdd(Word("Two")));
        var control = new ControlWordOperations(path);

        var first = control.List(new(Limit: 1));
        Assert.Equal("One", Assert.Single(first.Words).Term);
        Assert.Equal("Two", Assert.Single(control.List(new(Offset: first.NextOffset!.Value, Limit: 1, Revision: first.Revision)).Words).Term);

        Assert.True(library.TryAdd(Word("Three")));
        Assert.Equal("revision_conflict", Assert.Throws<ControlException>(() => control.List(new(Offset: 1, Revision: first.Revision))).Error.Code);
        File.WriteAllText(path, "damaged-json");
        Assert.Equal("word_library_unreadable", Assert.Throws<ControlException>(() => control.List(new())).Error.Code);
        Assert.Equal("damaged-json", File.ReadAllText(path));
    }

    [Fact]
    public void Word_mutations_check_fresh_revision_and_notify_the_shared_owner_after_commit()
    {
        string path = Path.Combine(directory, "words.json");
        var owner = new WordLibrary(path);
        var control = new ControlWordOperations(path);
        Assert.True(owner.TryAdd(Word("One")));
        string firstRevision = control.List(new()).Revision;
        Assert.True(new WordLibrary(path).TryAdd(Word("Two")));
        int changed = 0;
        var token = TestContext.Current.CancellationToken;
        owner.Changed += () => changed++;
        Assert.Equal("revision_conflict", Assert.Throws<ControlException>(() =>
            ControlWordOperations.ClearWords(owner, new(firstRevision), token)).Error.Code);
        Assert.Equal(0, changed);
        var located = control.Lookup(new(" one "));
        Assert.True(located.Found);
        var edited = ControlWordOperations.UpdateWord(owner, new("ONE", located.Word! with { Definition = "Updated" }, located.Revision), token);
        Assert.Equal(1, changed);
        Assert.Equal("Updated", owner.GetAll().Single(word => word.Term == "One").Definition);
        Assert.Collection(owner.GetAll(), word => Assert.Equal("One", word.Term), word => Assert.Equal("Two", word.Term));
        var removed = ControlWordOperations.RemoveWord(owner, new("two", edited.Revision), token);
        Assert.Equal(2, changed);
        var merged = ControlWordOperations.MergeVocabulary(owner, new(
            [new("one", "", "duplicate", "", ""), new("three", "", "new", "", "")], "en", "fr", removed.Revision), token);
        Assert.Equal(1, merged.Affected);
        Assert.Equal(3, changed);
        Assert.Equal(WordSource.AiInsights, owner.GetAll().Single(word => word.Term == "three").Source);
        Assert.Equal(2, ControlWordOperations.ClearWords(owner, new(merged.Revision), token).Affected);
        Assert.Empty(control.List(new()).Words);
    }

    [Fact]
    public async Task Anki_notes_are_not_retried_after_an_unconfirmed_response_without_acknowledgement()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        string path = Path.Combine(directory, "words.json");
        Assert.True(new WordLibrary(path).TryAdd(Word("One")));
        using var handler = new AnkiHandler();
        using var client = new HttpClient(handler);
        var control = new ControlWordOperations(path, new AnkiConnectSender(client));
        ControlScope scope = new("test");
        await using var jobs = new ControlJobService(Path.Combine(directory, "journal"),
            new Dictionary<string, ControlJobHandler> { ["anki.send"] = (context, token) => control.SendAsync(context, new(), token) });

        var accepted = await jobs.EnqueueAsync(new(Guid.NewGuid(), "anki.send", scope, null, ControlProtocol.Encode(new { })), deadline.Token);
        Guid jobId = Assert.IsType<Guid>(accepted.JobId);
        var interrupted = await WaitAsync(jobs, scope, jobId, ControlJobStatus.Interrupted, deadline.Token);
        Assert.Equal("external_effect_unknown", interrupted.Error?.Code);
        Assert.Equal("external_effect_unknown", (await jobs.ResumeAsync(new(jobId, scope), deadline.Token)).Error?.Code);
        Assert.Equal(1, handler.AddRequests);

        await jobs.ResumeAsync(new(jobId, scope, AcknowledgeUnknownEffect: true), deadline.Token);
        var completed = await WaitAsync(jobs, scope, jobId, ControlJobStatus.Completed, deadline.Token);
        Assert.Equal(1, completed.Value?.GetProperty("added").GetInt32());
        Assert.Equal(2, handler.AddRequests);
    }

    [Fact]
    public async Task Apkg_job_publishes_the_existing_writer_output_and_preserves_it_on_conflict()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        string path = Path.Combine(directory, "words.json");
        string destination = Path.Combine(directory, "words.apkg");
        Assert.True(new WordLibrary(path).TryAdd(Word("One")));
        var control = new ControlWordOperations(path);
        ControlScope scope = new("test");
        await using var jobs = new ControlJobService(Path.Combine(directory, "journal"),
            new Dictionary<string, ControlJobHandler> { ["words.export"] = (context, token) =>
                control.ExportAsync(context, new(destination, ControlWordsFormat.Apkg), token) });
        var request = new ControlRequest(Guid.NewGuid(), "words.export", scope, null, ControlProtocol.Encode(new { }));
        var accepted = await jobs.EnqueueAsync(request, deadline.Token);
        await WaitAsync(jobs, scope, Assert.IsType<Guid>(accepted.JobId), ControlJobStatus.Completed, deadline.Token);
        byte[] published = await File.ReadAllBytesAsync(destination, deadline.Token);
        using (var archive = new ZipArchive(new MemoryStream(published)))
        {
            Assert.NotNull(archive.GetEntry("collection.anki2"));
            Assert.NotNull(archive.GetEntry("media"));
        }
        var second = await jobs.EnqueueAsync(request with { RequestId = Guid.NewGuid() }, deadline.Token);
        var failed = await WaitAsync(jobs, scope, Assert.IsType<Guid>(second.JobId), ControlJobStatus.Failed, deadline.Token);
        Assert.Equal("output_exists", failed.Error?.Code);
        Assert.Equal(published, await File.ReadAllBytesAsync(destination, deadline.Token));
    }

    private static async Task<ControlJob> WaitAsync(ControlJobService jobs, ControlScope scope, Guid jobId,
        ControlJobStatus status, CancellationToken token)
    {
        while (true)
        {
            if (await jobs.GetAsync(scope, jobId, token) is { } job && job.Status == status) return job;
            await Task.Delay(10, token);
        }
    }

    private static SavedWord Word(string term) => new(term, "", "Translated", "", "Context", "en", "fr", WordSource.WordClick, "");

    private sealed class AnkiHandler : HttpMessageHandler
    {
        private int addRequests;
        public int AddRequests => Volatile.Read(ref addRequests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var args = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string? action = args.RootElement.GetProperty("action").GetString();
            string reply = action switch
            {
                "version" => "6",
                "createDeck" => "4",
                "createModel" => "{}",
                "addNotes" when Interlocked.Increment(ref addRequests) == 1 => throw new HttpRequestException("Connection closed."),
                "addNotes" => "[42]",
                _ => throw new InvalidOperationException("Unexpected Anki request."),
            };
            return new(HttpStatusCode.OK) { Content = new StringContent($$"""{"result":{{reply}},"error":null}""", Encoding.UTF8, "application/json") };
        }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
