using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlyleafLib.MediaPlayer;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace FlyleafLibTests.Automation;

public sealed class ControlJobLifecycleTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("llplayer-journal-test-").FullName;
    private readonly ControlScope scope = new("test");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_unjournaled_stage_removes_only_its_temporary_file(bool cancelBeforePublication)
    {
        using var deadline = Deadline();
        string output = Path.Combine(directory, "video.ru.srt");
        string unrelatedStage = Path.Combine(directory, ".video.ru.srt.unrelated.tmp");
        await File.WriteAllTextAsync(output, "original", deadline.Token);
        await File.WriteAllTextAsync(unrelatedStage, "unrelated", deadline.Token);
        string? writtenStage = null;
        await using var service = NewService(async (context, token) =>
        {
            using var publication = CancellationTokenSource.CreateLinkedTokenSource(token);
            async Task WriteAsync(string stage, CancellationToken ct)
            {
                writtenStage = stage;
                await File.WriteAllTextAsync(stage, "generated", ct);
                if (cancelBeforePublication)
                {
                    publication.Cancel();
                    return;
                }
                throw new ControlJobException("stale_media", "The source changed during generation.");
            }
            Task publish = context.WriteAndPublishFileAsync("subtitle", output, overwrite: true, WriteAsync, publication.Token);
            if (cancelBeforePublication) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publish);
            else await publish;
            return new(ControlJobStatus.Completed);
        });

        var accepted = await service.EnqueueAsync(MakeRequest("{}"), deadline.Token);
        Guid id = Assert.IsType<Guid>(accepted.JobId);
        var settled = await WaitForAsync(service, id,
            cancelBeforePublication ? ControlJobStatus.Completed : ControlJobStatus.Failed, deadline.Token);
        if (!cancelBeforePublication) Assert.Equal("stale_media", settled.Error?.Code);
        Assert.NotNull(writtenStage);
        Assert.False(File.Exists(writtenStage));
        Assert.Equal("original", await File.ReadAllTextAsync(output, deadline.Token));
        Assert.Equal("unrelated", await File.ReadAllTextAsync(unrelatedStage, deadline.Token));
    }

    [Fact]
    public async Task Admission_persists_resolved_input_and_a_duplicate_does_not_recapture_it()
    {
        using var deadline = Deadline();
        var request = MakeRequest("{}");
        Guid id;
        await using (var service = NewService((context, _) =>
        {
            int track = context.Checkpoint!.Value.GetProperty("track").GetInt32();
            return Task.FromResult(new ControlJobOutcome(ControlJobStatus.Completed, JsonSerializer.SerializeToElement(new { track })));
        }))
        {
            var accepted = await service.EnqueueWithCheckpointAsync(request, () => Json("""{"track":4}"""), deadline.Token);
            id = Assert.IsType<Guid>(accepted.JobId);
            var result = await WaitForAsync(service, id, ControlJobStatus.Completed, deadline.Token);
            Assert.Equal(4, result.Value!.Value.GetProperty("track").GetInt32());
        }

        await using var restarted = NewService(Unexpected);
        var duplicate = await restarted.EnqueueWithCheckpointAsync(request,
            () => throw new InvalidOperationException("A duplicate must use the accepted selection."), deadline.Token);
        Assert.Equal(id, duplicate.JobId);
        var checkpoint = await restarted.ReadCheckpointAsync(scope, id, deadline.Token);
        Assert.Equal(4, checkpoint.Value!.Value.GetProperty("track").GetInt32());
    }

    [Fact]
    public async Task A_disconnected_client_retrieves_the_same_job_and_result_without_restarting()
    {
        using var deadline = Deadline();
        using var client = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var started = Checkpoint();
        var finish = Checkpoint();
        int calls = 0;
        await using (var service = NewService(async (_, token) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await finish.Task.WaitAsync(token);
            return new(ControlJobStatus.Completed, JsonSerializer.SerializeToElement(new { cueCount = 3 }));
        }))
        {
            var request = MakeRequest("""{"slot":1,"language":"en"}""");
            var accepted = await service.EnqueueAsync(request, client.Token);
            Guid id = Assert.IsType<Guid>(accepted.JobId);
            await started.Task.WaitAsync(deadline.Token);
            client.Cancel();
            var duplicate = await service.EnqueueAsync(request with { Arguments = Json("""{"language":"en","slot":1.0}""") }, deadline.Token);
            Assert.Equal(id, duplicate.JobId);
            var conflict = await service.EnqueueAsync(request with { Arguments = Json("""{"language":"en","slot":2}""") }, deadline.Token);
            Assert.Equal("request_conflict", conflict.Error?.Code);
            Assert.Equal(ControlJobStatus.Running, (await service.GetAsync(scope, id, deadline.Token))?.Status);
            finish.SetResult();
            var completed = await WaitForAsync(service, id, ControlJobStatus.Completed, deadline.Token);
            Assert.Equal(3, completed.Value?.GetProperty("cueCount").GetInt32());
            Assert.Equal(1, calls);
            await using var restarted = NewService(Unexpected);
            var savedResult = await restarted.EnqueueAsync(request, deadline.Token);
            Assert.Equal(id, savedResult.JobId);
            Assert.Equal(3, savedResult.Value?.GetProperty("cueCount").GetInt32());
            Assert.Equal(1, calls);
        }
    }

    [Fact]
    public async Task A_nonzero_number_does_not_collide_with_zero_in_request_fingerprints()
    {
        using var deadline = Deadline();
        await using var service = NewService((_, _) => Task.FromResult(new ControlJobOutcome(ControlJobStatus.Completed)));
        var request = MakeRequest("""{"threshold":1e-100}""");
        await service.EnqueueAsync(request, deadline.Token);
        var duplicate = await service.EnqueueAsync(request with { Arguments = Json("""{"threshold":0}""") }, deadline.Token);
        Assert.Equal("request_conflict", duplicate.Error?.Code);
    }

    [Fact]
    public async Task Settings_fingerprint_survives_restart_but_changes_with_settings()
    {
        byte[] initial;
        await using (var service = NewService(Unexpected))
            initial = service.FingerprintSettings("private-settings"u8.ToArray());

        await using var restarted = NewService(Unexpected);
        Assert.Equal(initial, restarted.FingerprintSettings("private-settings"u8.ToArray()));
        Assert.NotEqual(initial, restarted.FingerprintSettings("changed-settings"u8.ToArray()));
    }

    [Fact]
    public async Task An_accepted_job_is_cancelled_only_after_the_handler_exits()
    {
        using var deadline = Deadline();
        var started = Checkpoint();
        var cancellationObserved = Checkpoint();
        var processExited = Checkpoint();
        await using var service = NewService(async (_, token) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                await processExited.Task.WaitAsync(deadline.Token);
                throw;
            }
            return new(ControlJobStatus.Completed);
        });
        Guid id = (await service.EnqueueAsync(MakeRequest("{}"), deadline.Token)).JobId!.Value;
        try
        {
            await started.Task.WaitAsync(deadline.Token);
            await service.CancelAsync(new(id, scope), deadline.Token);
            await cancellationObserved.Task.WaitAsync(deadline.Token);
            Assert.Equal(ControlJobStatus.Cancelling, (await service.GetAsync(scope, id, deadline.Token))?.Status);
        }
        finally { processExited.TrySetResult(); }
        await WaitForAsync(service, id, ControlJobStatus.Cancelled, deadline.Token);
        var first = await service.ReadChangesAsync(scope, afterSequence: 0, limit: 2, ct: deadline.Token);
        var tail = await service.ReadChangesAsync(scope, first[^1].Sequence, ct: deadline.Token);
        Assert.Equal([ControlJobStatus.Accepted, ControlJobStatus.Running, ControlJobStatus.Cancelling, ControlJobStatus.Cancelled],
            first.Concat(tail).Select(change => change.Job.Status));
        Assert.True(tail[0].Sequence > first[^1].Sequence);
    }

    [Fact]
    public async Task Failed_process_stop_blocks_later_resource_work_until_owner_cleanup_succeeds()
    {
        using var deadline = Deadline();
        var allowCleanup = Checkpoint();
        var secondStarted = Checkpoint();
        await using var service = NewService((context, _) =>
        {
            if (context.Request.Arguments.GetProperty("first").GetBoolean())
                throw new EngineProcessDrainException("Unconfirmed stop.", new IOException(), () =>
                    allowCleanup.Task.IsCompletedSuccessfully
                        ? ValueTask.CompletedTask
                        : ValueTask.FromException(new IOException("Still running.")));
            secondStarted.TrySetResult();
            return Task.FromResult(new ControlJobOutcome(ControlJobStatus.Completed));
        });
        Guid first = (await service.EnqueueAsync(MakeRequest("""{"first":true}"""), deadline.Token)).JobId!.Value;
        await WaitForAsync(service, first, ControlJobStatus.AwaitingCleanup, deadline.Token);
        Guid second = (await service.EnqueueAsync(MakeRequest("""{"first":false}"""), deadline.Token)).JobId!.Value;
        await WaitForAsync(service, second, ControlJobStatus.WaitingForResource, deadline.Token);
        await service.RetryAsync(new(first, scope), deadline.Token);
        Assert.False(secondStarted.Task.IsCompleted);
        allowCleanup.SetResult();
        await service.RetryAsync(new(first, scope), deadline.Token);
        await secondStarted.Task.WaitAsync(deadline.Token);
        await WaitForAsync(service, second, ControlJobStatus.Completed, deadline.Token);
        Assert.Equal(ControlJobStatus.Failed, (await service.GetAsync(scope, first, deadline.Token))?.Status);
    }

    [Fact]
    public async Task Restart_requires_explicit_resumption_and_checks_saved_source_identity()
    {
        using var deadline = Deadline();
        string source = Path.Combine(directory, "input.wav");
        await File.WriteAllTextAsync(source, "original", deadline.Token);
        Guid id = SeedRunning(new ControlJobJournal
        {
            Identities = { ["file:" + source] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) }
        });
        int invocations = 0;
        await using var service = NewService(async (context, token) =>
        {
            Assert.True(context.IsRecovery);
            Interlocked.Increment(ref invocations);
            await context.VerifyInputAsync(source, token);
            return new(ControlJobStatus.Completed);
        });
        var interrupted = await service.GetAsync(scope, id, deadline.Token);
        Assert.Equal(ControlJobStatus.Interrupted, interrupted?.Status);
        Assert.Equal(0, invocations);
        await File.WriteAllTextAsync(source, "changed", deadline.Token);
        await service.ResumeAsync(new(id, scope), deadline.Token);
        var rejected = await WaitForAsync(service, id, ControlJobStatus.Interrupted, deadline.Token);
        Assert.Equal("input_changed", rejected.Error?.Code);
        await File.WriteAllTextAsync(source, "original", deadline.Token);
        await service.ResumeAsync(new(id, scope), deadline.Token);
        await WaitForAsync(service, id, ControlJobStatus.Completed, deadline.Token);
        Assert.Equal(2, invocations);
    }

    [Fact]
    public async Task Resume_recognizes_a_file_published_between_its_intent_and_receipt()
    {
        using var deadline = Deadline();
        string destination = Path.Combine(directory, "saved.srt");
        string stage = Path.Combine(directory, ".owned-stage.srt");
        await File.WriteAllTextAsync(stage, "first subtitle", deadline.Token);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stage)));
        Guid id = SeedRunning(new ControlJobJournal
        {
            Effects = { ["srt"] = new("file", false, destination, hash) }
        });
        File.Move(stage, destination);
        await using var service = NewService(async (context, token) =>
        {
            Assert.True(await context.TryRecoverFileAsync("srt", destination, overwrite: false, token));
            return new(ControlJobStatus.Completed);
        });
        await service.ResumeAsync(new(id, scope), deadline.Token);
        await WaitForAsync(service, id, ControlJobStatus.Completed, deadline.Token);
        Assert.Equal("first subtitle", await File.ReadAllTextAsync(destination, deadline.Token));
        Assert.True(new ControlJobStore(directory).Get(id)?.Journal.Effects["srt"].Completed);
    }

    [Fact]
    public async Task Restart_requires_acknowledgement_before_repeating_an_ambiguous_external_effect()
    {
        using var deadline = Deadline();
        Guid id = SeedRunning(new ControlJobJournal { Effects = { ["anki"] = new("external", false) } });
        int calls = 0;
        await using var service = NewService(async (context, token) =>
        {
            JsonElement result = await context.RunExternalEffectAsync("anki", _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(JsonSerializer.SerializeToElement(new { card = 10 }));
            }, token);
            return new(ControlJobStatus.Completed, result);
        });
        var denied = await service.ResumeAsync(new(id, scope), deadline.Token);
        Assert.Equal("external_effect_unknown", denied.Error?.Code);
        Assert.Equal(0, calls);
        await service.ResumeAsync(new(id, scope, AcknowledgeUnknownEffect: true), deadline.Token);
        var completed = await WaitForAsync(service, id, ControlJobStatus.Completed, deadline.Token);
        Assert.Equal(10, completed.Value?.GetProperty("card").GetInt32());
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("settings.credentials.set")]
    [InlineData("settings.credentials.apply")]
    public async Task Credential_value_is_unavailable_to_job_readback_and_the_durable_journal(string operation)
    {
        const string secret = "private-marker-F20-credential-test";
        using var deadline = Deadline();
        int calls = 0;
        await using var service = NewService((context, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new ControlJobOutcome(ControlJobStatus.Failed, context.Request.Arguments,
                new("provider", secret)));
        }, operation);
        const string urlSecret = "private-marker-F20-url-test";
        JsonElement arguments = operation == "settings.credentials.apply"
            ? JsonSerializer.SerializeToElement(new { credentials = new[] { secret }, fields = new[] { "https://user:" + urlSecret + "@example.test" } })
            : JsonSerializer.SerializeToElement(new { value = secret });
        var request = new ControlRequest(Guid.NewGuid(), operation, scope, null, arguments);
        Guid id = (await service.EnqueueAsync(request, deadline.Token)).JobId!.Value;
        var failed = await WaitForAsync(service, id, ControlJobStatus.Failed, deadline.Token);
        Assert.Null(failed.Value);
        Assert.Equal("credential_operation_failed", failed.Error?.Code);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(failed));
        Assert.DoesNotContain(urlSecret, JsonSerializer.Serialize(failed));
        Assert.Equal("credential_replay_denied", (await service.RetryAsync(new(id, scope), deadline.Token)).Error?.Code);
        Assert.Equal(id, (await service.EnqueueAsync(request, deadline.Token)).JobId);
        Assert.Equal(1, calls);
        foreach (var path in Directory.EnumerateFiles(directory, "jobs.sqlite*"))
        {
            string raw = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain(secret, raw);
            Assert.DoesNotContain(urlSecret, raw);
        }
    }

    [Fact]
    public async Task Partial_translation_rejects_a_stale_job_revision_and_stays_durable()
    {
        using var deadline = Deadline();
        Guid id;
        await using (var service = NewService((context, _) =>
        {
            context.SaveCheckpoint(Json("""{"cue":"original","draft":null}"""));
            return Task.FromResult(new ControlJobOutcome(ControlJobStatus.AwaitingTranslation));
        }))
        {
            id = (await service.EnqueueAsync(MakeRequest("{}"), deadline.Token)).JobId!.Value;
            var waiting = await WaitForAsync(service, id, ControlJobStatus.AwaitingTranslation, deadline.Token);
            var written = await service.UpdateAwaitingTranslationAsync(scope, id, waiting.Revision,
                _ => new(Json("""{"cue":"original","draft":"translation"}"""), new(ControlJobStatus.AwaitingTranslation)), deadline.Token);
            Assert.Null(written.Error);
            var stale = await service.UpdateAwaitingTranslationAsync(scope, id, waiting.Revision,
                _ => throw new InvalidOperationException("Stale draft was reached."), deadline.Token);
            Assert.Equal("revision_conflict", stale.Error?.Code);
        }
        await using var restarted = NewService(Unexpected);
        var saved = await restarted.ReadCheckpointAsync(scope, id, deadline.Token);
        Assert.Equal("translation", saved.Value?.GetProperty("draft").GetString());
        Assert.Equal(ControlJobStatus.Interrupted, (await restarted.GetAsync(scope, id, deadline.Token))?.Status);
    }

    private ControlRequest MakeRequest(string arguments) => new(Guid.NewGuid(), "test", scope, null, Json(arguments));

    private ControlJobService NewService(ControlJobHandler handler, string operation = "test") =>
        new(directory, new Dictionary<string, ControlJobHandler> { [operation] = handler });

    private Guid SeedRunning(ControlJobJournal journal)
    {
        var store = new ControlJobStore(directory);
        var state = new ControlJob(Guid.NewGuid(), scope, "test", ControlJobStatus.Accepted, 1, DateTimeOffset.UtcNow);
        var row = store.Admit(state, MakeRequest("{}"), RandomNumberGenerator.GetBytes(32), secret: false);
        store.Save(row, state with { Status = ControlJobStatus.Running }, journal);
        return state.JobId;
    }

    private async Task<ControlJob> WaitForAsync(ControlJobService service, Guid id, ControlJobStatus status, CancellationToken token)
    {
        while (true)
        {
            var job = Assert.IsType<ControlJob>(await service.GetAsync(scope, id, token));
            if (job.Status == status)
                return job;
            if (job.Status is ControlJobStatus.Failed or ControlJobStatus.Cancelled or ControlJobStatus.Completed)
                Assert.Fail($"Expected {status}, received {job.Status}: {job.Error}");
            await Task.Delay(15, token);
        }
    }

    private static Task<ControlJobOutcome> Unexpected(ControlJobContext _, CancellationToken __) =>
        throw new InvalidOperationException("The job should not be executed.");

    private static JsonElement Json(string text)
    {
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private static TaskCompletionSource Checkpoint() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static CancellationTokenSource Deadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        return deadline;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
