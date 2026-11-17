using System.Text;
using System.Text.Json;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace FlyleafLibTests.Automation;

public sealed class ControlCueTranslationTests
{
    [Fact]
    public void Translation_range_uses_full_document_indices_with_bitmap_cues()
    {
        var original = ControlCueDocument.Capture(Guid.NewGuid(), "mixed-source", "en", MakeCues());
        var document = original with
        {
            Cues = [original.Cues[0] with { IsBitmap = true, Text = null }, original.Cues[1]]
        };

        var unavailable = Assert.Throws<ControlCueException>(() => ControlTranslationDraft.Start(document, "fr", 0, 1));
        Assert.Equal("no_text_cues", unavailable.Code);

        var draft = ControlTranslationDraft.Start(document, "fr", 1, 1);
        var page = draft.Read(document.Revision, 0, 1);
        Assert.Equal(document.Cues[1].CueId, Assert.Single(page.Cues).CueId);
    }

    [Fact]
    public void Harness_translation_accepts_exact_cue_revision()
    {
        SubtitleData[] cues = MakeCues();
        Guid documentId = Guid.NewGuid();
        var document = ControlCueDocument.Capture(documentId, "media-1/slot-0/delay-0", "en", cues);
        var draft = ControlTranslationDraft.Start(document, "fr");
        var page = draft.Read(document.Revision, offset: 0, limit: 1);
        Assert.Equal(document.Cues[0].CueId, Assert.Single(page.Cues).CueId);
        Assert.Equal(1, page.NextOffset);

        cues[0].StartTime += TimeSpan.FromMilliseconds(1);
        var changed = ControlCueDocument.Capture(documentId, "media-1/slot-0/delay-0", "en", cues);
        Assert.NotEqual(document.Revision, changed.Revision);
        Assert.Equal(document.Cues[0].CueId, changed.Cues[0].CueId);
        Assert.Equal("revision_conflict", Assert.Throws<ControlCueException>(() => draft.Commit(document.Revision, changed)).Code);

        var reopened = ControlCueDocument.Capture(documentId, "media-2/slot-0/delay-0", "en", MakeCues());
        Assert.NotEqual(document.Revision, reopened.Revision);
    }

    [Fact]
    public void Duplicate_or_missing_cue_ids_fail_atomically()
    {
        var document = ControlCueDocument.Capture(Guid.NewGuid(), "source-1", "en", MakeCues());
        var draft = ControlTranslationDraft.Start(document, "fr");
        Guid first = document.Cues[0].CueId;
        Guid last = document.Cues[1].CueId;

        Assert.Equal("duplicate_cue", Assert.Throws<ControlCueException>(() => draft.Submit(document.Revision,
            [new(first, "Un"), new(first, "Deux")])).Code);
        Assert.Equal("unknown_cue", Assert.Throws<ControlCueException>(() => draft.Submit(document.Revision,
            [new(first, "Un"), new(Guid.NewGuid(), "Deux")])).Code);
        Assert.Empty(draft.Submitted);

        draft = draft.Submit(document.Revision, [new(last, "Deux")]);
        Assert.Equal("translation_incomplete", Assert.Throws<ControlCueException>(() => draft.Commit(document.Revision, document)).Code);
        Assert.Equal("Deux", Assert.Single(draft.Submitted).Value);
        Assert.Equal("translation_conflict", Assert.Throws<ControlCueException>(() => draft.Submit(document.Revision,
            [new(last, "New text")])).Code);
    }

    [Fact]
    public void Malformed_translation_preserves_source_text_styles_and_times()
    {
        SubtitleData[] authored = MakeCues();
        var document = ControlCueDocument.Capture(Guid.NewGuid(), "source-1", "en", authored);
        var draft = ControlTranslationDraft.Start(document, "fr");

        Assert.Equal("invalid_translation", Assert.Throws<ControlCueException>(() => draft.Submit(document.Revision,
            [new(document.Cues[0].CueId, "Un"), new(document.Cues[1].CueId, " \t ")])).Code);
        Assert.Empty(draft.Submitted);

        draft = draft.Submit(document.Revision, [new(document.Cues[0].CueId, "Un"), new(document.Cues[1].CueId, "Deux")]);
        var translated = draft.Commit(document.Revision, document);
        Assert.Equal("fr", translated.TargetLanguage);
        Assert.Equal(document.Cues[0] with { TranslatedText = "Un" }, translated.Cues[0]);
        Assert.Equal(document.Cues[1] with { TranslatedText = "Deux" }, translated.Cues[1]);
        Assert.Equal(document.Cues[2], translated.Cues[2]);
        Assert.Null(authored[0].TranslatedText);
        Assert.Equal(SubStyles.ITALIC, Assert.Single(authored[0].SubStyles!).style);
        Assert.NotEqual(document.Revision, translated.Revision);
        Assert.Equal(translated.Revision, draft.Commit(document.Revision, document).Revision);
    }

    [Fact]
    public void Partial_response_can_resume_explicitly()
    {
        var document = ControlCueDocument.Capture(Guid.NewGuid(), "source-1", "en", MakeCues());
        var draft = ControlTranslationDraft.Start(document, "fr").Submit(document.Revision, [new(document.Cues[0].CueId, "Un")]);
        var recovered = JsonSerializer.Deserialize<ControlTranslationDraft>(JsonSerializer.Serialize(draft));
        Assert.NotNull(recovered);
        Assert.Same(recovered, recovered.Submit(document.Revision, [new(document.Cues[0].CueId, "Un")]));

        var page = recovered.Read(document.Revision, 1, 1);
        Assert.Equal("Deux", recovered.Submit(document.Revision, [new(Assert.Single(page.Cues).CueId, "Deux")])
            .Commit(document.Revision, recovered.Source).Cues[1].TranslatedText);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public async Task Export_preserves_existing_file_without_overwrite_flag()
    {
        var document = ControlCueDocument.Capture(Guid.NewGuid(), "source-1", "en", MakeCues());
        string folder = Directory.CreateTempSubdirectory().FullName;
        string path = Path.Combine(folder, "output.srt");
        try
        {
            await File.WriteAllTextAsync(path, "keep me", TestContext.Current.CancellationToken);
            var result = await Assert.ThrowsAsync<ControlCueException>(() => ControlSubtitleExport.WriteAsync(document,
                path, SubtitleExportFormat.Srt, ControlSubtitleText.Original, false, false, true, TestContext.Current.CancellationToken));

            Assert.Equal("output_exists", result.Code);
            Assert.Equal("keep me", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(folder)));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Explicit_overwrite_uses_atomic_writer()
    {
        var document = ControlCueDocument.Capture(Guid.NewGuid(), "source-1", "en", MakeCues());
        string folder = Directory.CreateTempSubdirectory().FullName;
        string path = Path.Combine(folder, "output.srt");
        try
        {
            await File.WriteAllTextAsync(path, "replace me", TestContext.Current.CancellationToken);
            await ControlSubtitleExport.WriteAsync(document, path, SubtitleExportFormat.Srt, ControlSubtitleText.Original,
                true, true, true, TestContext.Current.CancellationToken);

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
            string text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("00:00:00,125 --> 00:00:01,000\r\n[Speaker 1] <i>One</i>", text);
            Assert.Equal(path, Assert.Single(Directory.GetFiles(folder)));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Batch_and_manual_outputs_are_equivalent()
    {
        SubtitleData[] cues = MakeCues().Where(cue => cue.IsText).ToArray();
        cues[0].TranslatedText = "Un\r\n\r\nAutre";
        var document = ControlCueDocument.Capture(Guid.NewGuid(), "source-1", "en", cues, "fr");
        string folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string batch = Path.Combine(folder, "batch.srt");
            string manual = Path.Combine(folder, "manual.srt");
            var token = TestContext.Current.CancellationToken;
            await new SrtSubtitleWriter(new UTF8Encoding(true)).WriteAsync(cues, batch, overwrite: false, token);
            await ControlSubtitleExport.WriteAsync(document, manual, SubtitleExportFormat.Srt,
                ControlSubtitleText.TranslatedWithFallback, false, true, false, token);
            Assert.Equal(await File.ReadAllBytesAsync(batch, token), await File.ReadAllBytesAsync(manual, token));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Persisted_draft_resumes_only_explicitly_and_commits_once()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        string folder = Directory.CreateTempSubdirectory().FullName;
        var scope = new ControlScope("test");
        const string operation = "recognition.start";
        Guid jobId;
        string revision;
        int recognitionCalls = 0;
        var handlers = new Dictionary<string, ControlJobHandler>
        {
            [operation] = (context, _) =>
            {
                context.VerifyIdentity("input", "source-1");
                if (ControlTranslationOperations.RestoreHarnessTranslation(context) is { } restored) return Task.FromResult(restored);
                Interlocked.Increment(ref recognitionCalls);
                var document = ControlCueDocument.Capture(context.JobId, "source-1", "en", MakeCues());
                return Task.FromResult(ControlTranslationOperations.AwaitHarnessTranslation(context, document, "fr"));
            }
        };
        try
        {
            await using (var jobs = new ControlJobService(folder, handlers))
            {
                var created = await jobs.EnqueueAsync(new(Guid.NewGuid(), operation, scope, null, ControlProtocol.Encode(new { })), token);
                jobId = Assert.IsType<Guid>(created.JobId);
                var ready = await WaitAsync(jobs, scope, jobId, ControlJobStatus.AwaitingTranslation, token);
                revision = ready.Value!.Value.GetProperty("revision").GetString()!;
                var service = new ControlTranslationOperations(jobs);
                var page = await service.ReadDraftAsync(scope, new(jobId, revision, Limit: 1), token);
                var first = new ControlCueTranslation(page.Value!.Value.GetProperty("cues")[0].GetProperty("cueId").GetGuid(), "Un");
                var submitted = await service.SubmitAsync(scope, ready.Revision, new(jobId, revision, [first]), token);
                Assert.Null(submitted.Error);
                var replay = await service.SubmitAsync(scope, submitted.Revision, new(jobId, revision, [first]), token);
                Assert.Equal(submitted.Revision, replay.Revision);
                Assert.Equal("translation_incomplete", (await service.CommitAsync(scope, submitted.Revision, new(jobId, revision), token)).Error?.Code);
            }

            await using var recovered = new ControlJobService(folder, handlers);
            Assert.Equal(ControlJobStatus.Interrupted, (await recovered.GetAsync(scope, jobId, token))?.Status);
            Assert.Equal(1, Volatile.Read(ref recognitionCalls));
            await recovered.ResumeAsync(new(jobId, scope), token);
            var active = await WaitAsync(recovered, scope, jobId, ControlJobStatus.AwaitingTranslation, token);
            var resumed = new ControlTranslationOperations(recovered);
            var tail = await resumed.ReadDraftAsync(scope, new(jobId, revision, Offset: 1), token);
            Assert.Equal(1, tail.Value!.Value.GetProperty("receivedCount").GetInt32());
            var second = new ControlCueTranslation(tail.Value.Value.GetProperty("cues")[0].GetProperty("cueId").GetGuid(), "Deux");
            var submittedTail = await resumed.SubmitAsync(scope, active.Revision, new(jobId, revision, [second]), token);
            var committed = await resumed.CommitAsync(scope, submittedTail.Revision, new(jobId, revision), token);
            Assert.Null(committed.Error);
            var replayedCommit = await resumed.CommitAsync(scope, submittedTail.Revision, new(jobId, revision), token);
            Assert.Equal(committed.Revision, replayedCommit.Revision);
            Assert.Equal(committed.Value!.Value.GetRawText(), replayedCommit.Value!.Value.GetRawText());
            Assert.Equal(1, Volatile.Read(ref recognitionCalls));
            string translatedRevision = committed.Value.Value.GetProperty("revision").GetString()!;
            var finalDocument = await resumed.GetDocumentAsync(scope, new(jobId, translatedRevision), token);
            Assert.Equal(new[] { "Un", "Deux", null }, finalDocument.Cues.Select(cue => cue.TranslatedText));
        }
        finally { Directory.Delete(folder, recursive: true); }
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

    static SubtitleData[] MakeCues() =>
    [
        new() { Text = "One", StartTime = TimeSpan.FromMilliseconds(125), EndTime = TimeSpan.FromSeconds(1),
            AssignedVoiceId = "voice-1", SpeakerId = "Speaker 1", SubStyles = [new(SubStyles.ITALIC, 0, 3)] },
        new() { Text = "Two", StartTime = TimeSpan.FromSeconds(2), EndTime = TimeSpan.FromSeconds(3) },
        new() { IsBitmap = true, StartTime = TimeSpan.FromSeconds(4), EndTime = TimeSpan.FromSeconds(5) },
    ];
}
