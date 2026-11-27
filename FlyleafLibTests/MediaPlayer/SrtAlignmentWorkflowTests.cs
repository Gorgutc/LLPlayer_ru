using System.Collections.Immutable;
using System.Text;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.MediaPlayer;

public sealed class SrtAlignmentWorkflowTests
{
    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavePublishedCopyKeepsAuthorTextAndInput()
    {
        using Fixture fixture = new();
        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);
        Assert.True(draft.CanSaveOriginal);
        Assert.Equal(1, draft.MatchedCount);

        await fixture.Session.SaveAsync(draft, fixture.Output, translated: false, acceptUnmatched: false, Token);

        Assert.Equal("14\r\n00:00:03,400 --> 00:00:04,300\r\n<i>red brick wall</i>\r\n",
            await File.ReadAllTextAsync(fixture.Output, Token));
        Assert.Equal(Fixture.Original, await File.ReadAllTextAsync(fixture.SourcePath, Token));
    }

    [Fact]
    public async Task SourceReplacementBeforePrepareRequiresReload()
    {
        using Fixture fixture = new();
        await File.WriteAllTextAsync(fixture.SourcePath, Fixture.Original.Replace("red", "blue"), Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token));
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public async Task SwitchDuringStagingRejectsAndRemovesProvisionalOutput()
    {
        using Fixture fixture = new();
        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);
        fixture.BeforePublish = () => fixture.Current = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Session.SaveAsync(draft, fixture.Output, translated: false, acceptUnmatched: false, Token));
        Assert.False(File.Exists(fixture.Output));
        Assert.Single(Directory.EnumerateFiles(fixture.Root));
    }

    [Fact]
    public async Task ExistingDestinationIsNeverReplaced()
    {
        using Fixture fixture = new();
        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);
        await File.WriteAllTextAsync(fixture.Output, "keep", Token);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            fixture.Session.SaveAsync(draft, fixture.Output, translated: false, acceptUnmatched: false, Token));
        Assert.Equal("keep", await File.ReadAllTextAsync(fixture.Output, Token));
    }

    [Fact]
    public async Task LinkedTranslationOnlyPublishesTheSameProposedInterval()
    {
        using Fixture fixture = new();
        fixture.Metadata = [new(0, "red brick wall", "Красная стена", null, "SPEAKER_00", "voice", [])];
        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);
        Assert.True(draft.CanSaveTranslated);
        string translated = Path.Combine(fixture.Root, "translated.aligned.srt");
        await fixture.Session.SaveAsync(draft, fixture.Output, translated: false, acceptUnmatched: false, Token);
        await fixture.Session.SaveAsync(draft, translated, translated: true, acceptUnmatched: false, Token);

        SrtTimingCue originalCue = Assert.Single(SrtTimingDocument.Parse(await File.ReadAllBytesAsync(fixture.Output, Token)).Cues);
        SrtTimingCue translatedCue = Assert.Single(SrtTimingDocument.Parse(await File.ReadAllBytesAsync(translated, Token)).Cues);
        Assert.Equal((originalCue.Start, originalCue.End), (translatedCue.Start, translatedCue.End));
        Assert.Equal("Красная стена", translatedCue.Text);
        Assert.Equal(Fixture.Original, await File.ReadAllTextAsync(fixture.SourcePath, Token));
    }

    [Fact]
    public async Task ChangedLinkedTranslationRejectsThePreparedOutput()
    {
        using Fixture fixture = new();
        fixture.Metadata = [new(0, "red brick wall", "Первый вариант", null, null, null, [])];
        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);
        fixture.Metadata = [fixture.Metadata[0] with { TranslatedText = "Новый вариант" }];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Session.SaveAsync(draft, fixture.Output, translated: true, acceptUnmatched: false, Token));
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public async Task CanceledAtPublicationLeavesNoProvisionalFile()
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancellation = new();
        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);
        fixture.BeforePublish = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Session.SaveAsync(draft, fixture.Output, translated: false, acceptUnmatched: false, cancellation.Token));
        Assert.Single(Directory.EnumerateFiles(fixture.Root));
    }

    [Fact]
    public async Task ShutdownWaitsForInFlightSaveToCancelAndRemoveStagingFile()
    {
        using Fixture fixture = new();
        using ManualResetEventSlim resumePublication = new();
        TaskCompletionSource reachedPublication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = Token;
        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, token);
        fixture.BeforePublish = () =>
        {
            reachedPublication.SetResult();
            resumePublication.Wait(token);
        };

        Task save = Task.Run(() => fixture.Session.SaveAsync(draft, fixture.Output, translated: false,
            acceptUnmatched: false, token), token);
        Task drain;
        bool drainedEarly;
        try
        {
            await reachedPublication.Task.WaitAsync(token);
            Assert.Single(Directory.EnumerateFiles(fixture.Root, ".llplayer-alignment-*.tmp"));
            fixture.Workflow.BeginShutdown();
            drain = fixture.Workflow.DrainAsync();
            drainedEarly = drain.IsCompleted;
        }
        finally { resumePublication.Set(); }

        Exception? failure = await Record.ExceptionAsync(() => save.WaitAsync(token));
        await drain.WaitAsync(token);

        Assert.False(drainedEarly);
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.False(File.Exists(fixture.Output));
        Assert.Single(Directory.EnumerateFiles(fixture.Root));
        Assert.Equal(Fixture.Original, await File.ReadAllTextAsync(fixture.SourcePath, token));
    }

    [Fact]
    public async Task ShutdownRejectsNewSaveOfPreparedDraft()
    {
        using Fixture fixture = new();
        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);
        fixture.Workflow.BeginShutdown();
        await fixture.Workflow.DrainAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Session.SaveAsync(draft, fixture.Output, translated: false, acceptUnmatched: false, Token));

        Assert.Single(Directory.EnumerateFiles(fixture.Root));
    }

    [Fact]
    public async Task CpuDrainRetainsModelUseUntilConfirmed()
    {
        using Fixture fixture = new();
        fixture.Reader = (_, _, _) => throw new EngineProcessDrainException("Engine still running.",
            new IOException("Stop timed out."), () =>
            {
                Assert.True(fixture.Resources.IsInUse);
                return ValueTask.CompletedTask;
            });

        await Assert.ThrowsAsync<EngineProcessDrainException>(() =>
            fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token));
        Assert.True(fixture.Resources.IsInUse);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token));

        await fixture.Session.CancelAndDrainAsync();
        Assert.False(fixture.Resources.IsInUse);
    }

    [Fact]
    public void ResolvedCueCannotOverlapUnresolvedOriginal()
    {
        static TimeSpan S(int seconds) => TimeSpan.FromSeconds(seconds);
        SrtAlignmentRow unchanged = new(1, "unresolved text", S(8), S(10), null, null, 0, "No match");
        SrtAlignmentRow matched = new(0, "resolved text", S(3), S(5), S(7), S(9), 1, null);
        Assert.False(SrtAlignmentWorkflow.HasValidOrder([matched, unchanged]));
        Assert.True(SrtAlignmentWorkflow.HasValidOrder([matched with { ProposedEnd = S(8) }, unchanged]));
    }

    [Fact]
    public async Task MixedTimingConflictBlocksBothExportsAndIsReportedInTheDraft()
    {
        const string source = Fixture.Original + "\r\n15\r\n00:00:03,800 --> 00:00:04,800\r\n[silence]\r\n";
        using Fixture fixture = new(source);
        fixture.Metadata =
        [
            new(0, "red brick wall", "Красная стена", null, null, null, []),
            new(1, "[silence]", "[тишина]", null, null, null, [])
        ];

        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);

        Assert.Equal(1, draft.MatchedCount);
        Assert.True(draft.HasTimingConflicts);
        Assert.False(draft.CanSaveOriginal);
        Assert.False(draft.CanSaveTranslated);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Session.SaveAsync(draft, fixture.Output, translated: false, acceptUnmatched: true, Token));
        Assert.False(File.Exists(fixture.Output));
        Assert.Equal(source, await File.ReadAllTextAsync(fixture.SourcePath, Token));
    }

    [Fact]
    public async Task RetriesMissingSpeechBetweenExactAnchorsOnTheSameSelectedStream()
    {
        using Fixture fixture = new("""
            1
            00:00:01,000 --> 00:00:02,000
            begin the sequence

            2
            00:00:03,000 --> 00:00:04,000
            open another panel

            3
            00:00:05,000 --> 00:00:06,000
            confirm the changes
            """);
        List<SrtAlignmentAudioRequest> requests = [];
        fixture.Reader = (_, request, _) =>
        {
            Assert.True(fixture.Resources.IsInUse);
            requests.Add(request);
            return Task.FromResult<IReadOnlyList<WordTiming>>(request.Window == null
                ? [.. Spoken(2, "begin the sequence"), .. Spoken(7, "confirm the changes")]
                : [.. Spoken(2.1, "open another panel"), .. Spoken(4.1, "open another panel")]);
        };

        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);

        Assert.Equal(2, requests.Count);
        SrtAlignmentAudioWindow window = Assert.IsType<SrtAlignmentAudioWindow>(requests[1].Window);
        Assert.Equal(requests[0] with { Window = window }, requests[1]);
        Assert.True(window.Start < TimeSpan.FromSeconds(4.1));
        Assert.True(window.End > TimeSpan.FromSeconds(4.7));
        Assert.Equal(3, draft.MatchedCount);
        Assert.Equal(TimeSpan.FromSeconds(4.1), draft.Rows[1].ProposedStart);
        Assert.Equal(TimeSpan.FromSeconds(2), draft.Rows[0].ProposedStart);
        Assert.Equal(TimeSpan.FromSeconds(7), draft.Rows[2].ProposedStart);
        Assert.True(draft.CanSaveOriginal);
    }

    private static IEnumerable<WordTiming> Spoken(double start, string phrase) =>
        phrase.Split(' ').Select((word, index) => new WordTiming(word,
            TimeSpan.FromSeconds(start + index * .2), TimeSpan.FromSeconds(start + (index + 1) * .2)));

    [Fact]
    public async Task RetryDoesNotBlockAnOriginallyExportableDraft()
    {
        using Fixture fixture = new("""
            1
            00:00:01,000 --> 00:00:02,000
            begin the sequence

            2
            00:00:03,000 --> 00:00:04,000
            open another panel

            3
            00:00:04,500 --> 00:00:05,400
            focus the camera

            4
            00:00:06,000 --> 00:00:07,000
            confirm the changes
            """);
        fixture.Reader = (_, request, _) => Task.FromResult<IReadOnlyList<WordTiming>>(request.Window == null
            ? [.. Spoken(1, "begin the sequence"), .. Spoken(7, "confirm the changes")]
            : [.. Spoken(4.2, "open another panel")]);

        SrtAlignmentDraft draft = await fixture.Session.PrepareAsync("en", SrtAlignmentDevice.Cpu, Token);

        Assert.True(draft.CanSaveOriginal);
        Assert.False(draft.HasTimingConflicts);
        Assert.Equal(2, draft.MatchedCount);
        Assert.Null(draft.Rows[1].ProposedStart);
        await fixture.Session.SaveAsync(draft, fixture.Output, translated: false, acceptUnmatched: true, Token);
        var saved = SrtTimingDocument.Parse(await File.ReadAllBytesAsync(fixture.Output, Token));
        Assert.Equal(TimeSpan.FromSeconds(3), saved.Cues[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(4.5), saved.Cues[2].Start);
    }

    [Fact]
    public async Task RetryDoesNotAcceptOneOccurrenceWhenAnotherWindowHeardThePhraseTwice()
    {
        var cues = new[]
        {
            new SrtTimingCue(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "begin the sequence"),
            new SrtTimingCue(1, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), "open another panel"),
            new SrtTimingCue(2, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(26), "confirm the changes")
        };
        WordTiming[] primary = [.. Spoken(2, "begin the sequence"), .. Spoken(25, "confirm the changes")];
        int windows = 0;
        var firstPass = SrtAudioAlignment.Align(cues, primary, TimeSpan.FromSeconds(30), Token);
        var proposals = await SrtAlignmentSpeechRetry.RetryAsync(firstPass, TimeSpan.FromSeconds(30), window =>
        {
            windows++;
            return Task.FromResult<IReadOnlyList<WordTiming>>(window.Start < TimeSpan.FromSeconds(5)
                ? [.. Spoken(5, "open another panel"), .. Spoken(13, "open another panel")]
                : [.. Spoken(13, "open another panel")]);
        }, Token);

        Assert.Equal(3, windows);
        Assert.Null(proposals[1].Start);
        Assert.Equal(SrtAlignmentRejection.Ambiguous, proposals[1].Rejection);
        Assert.Equal(TimeSpan.FromSeconds(2), proposals[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(25), proposals[2].Start);
    }

    [Fact]
    public async Task InvalidOptionalRetryEvidenceDoesNotDiscardConfirmedSpeech()
    {
        var cues = new[]
        {
            new SrtTimingCue(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "begin the sequence"),
            new SrtTimingCue(1, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), "open another panel"),
            new SrtTimingCue(2, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(9), "confirm the changes")
        };
        WordTiming[] primary = [.. Spoken(2, "begin the sequence"), .. Spoken(8, "confirm the changes")];
        var firstPass = SrtAudioAlignment.Align(cues, primary, TimeSpan.FromSeconds(10), Token);
        var proposals = await SrtAlignmentSpeechRetry.RetryAsync(firstPass, TimeSpan.FromSeconds(10), _ =>
            throw new InvalidDataException("Malformed word evidence."), Token);

        Assert.Equal(TimeSpan.FromSeconds(2), proposals[0].Start);
        Assert.Null(proposals[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(8), proposals[2].Start);
    }

    sealed class Fixture : IDisposable
    {
        public const string Original = "14\r\n00:00:01,000 --> 00:00:02,000\r\n<i>red brick wall</i>\r\n";

        public string Root { get; } = Directory.CreateTempSubdirectory("llplayer-align-test-").FullName;
        public string SourcePath => Path.Combine(Root, "input.srt");
        public string Output => Path.Combine(Root, "input.aligned.srt");
        public SrtAlignmentWorkflow Workflow { get; }
        public ISrtAlignmentSession Session { get; }
        public bool Current { get; set; } = true;
        public Action? BeforePublish { get; set; }
        public ImmutableArray<SrtTimingCueMetadata> Metadata { get; set; } = [];
        public LanguageResourceCoordinator Resources { get; } = new();
        public Func<Config, SrtAlignmentAudioRequest, CancellationToken, Task<IReadOnlyList<WordTiming>>>? Reader { get; set; }

        public Fixture(string source = Original)
        {
            FlyleafLib.Utils.IsTesting = true;
            File.WriteAllText(SourcePath, source);
            SrtTimingDocument document = SrtTimingDocument.Parse(Encoding.UTF8.GetBytes(source));
            Workflow = new(null, Resources, (config, request, token) =>
                Reader?.Invoke(config, request, token) ?? Task.FromResult<IReadOnlyList<WordTiming>>([
                    new("red", TimeSpan.FromSeconds(3.4), TimeSpan.FromSeconds(3.6)),
                    new("brick", TimeSpan.FromSeconds(3.6), TimeSpan.FromSeconds(4)),
                    new("wall", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4.3))
                ]));
            Session = Workflow.CreateSession(new(SourcePath, document, "media.mp4", 1, TimeSpan.FromSeconds(10),
                "English", "en", () => new FlyleafLib.Config(test: true), () => Current,
                () => Metadata,
                (owns, publish) =>
                {
                    BeforePublish?.Invoke();
                    if (!Current || !owns()) return false;
                    publish();
                    return true;
                }));
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
