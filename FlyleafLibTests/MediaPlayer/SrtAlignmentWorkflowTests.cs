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

    sealed class Fixture : IDisposable
    {
        public const string Original = "14\r\n00:00:01,000 --> 00:00:02,000\r\n<i>red brick wall</i>\r\n";

        public string Root { get; } = Directory.CreateTempSubdirectory("llplayer-align-test-").FullName;
        public string SourcePath => Path.Combine(Root, "input.srt");
        public string Output => Path.Combine(Root, "input.aligned.srt");
        public ISrtAlignmentSession Session { get; }
        public bool Current { get; set; } = true;
        public Action? BeforePublish { get; set; }
        public ImmutableArray<SrtTimingCueMetadata> Metadata { get; set; } = [];
        public LanguageResourceCoordinator Resources { get; } = new();
        public Func<Config, SrtAlignmentAudioRequest, CancellationToken, Task<IReadOnlyList<WordTiming>>>? Reader { get; set; }

        public Fixture()
        {
            FlyleafLib.Utils.IsTesting = true;
            File.WriteAllText(SourcePath, Original);
            SrtTimingDocument document = SrtTimingDocument.Parse(Encoding.UTF8.GetBytes(Original));
            SrtAlignmentWorkflow workflow = new(null, Resources, (config, request, token) =>
                Reader?.Invoke(config, request, token) ?? Task.FromResult<IReadOnlyList<WordTiming>>([
                    new("red", TimeSpan.FromSeconds(3.4), TimeSpan.FromSeconds(3.6)),
                    new("brick", TimeSpan.FromSeconds(3.6), TimeSpan.FromSeconds(4)),
                    new("wall", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4.3))
                ]));
            Session = workflow.CreateSession(new(SourcePath, document, "media.mp4", 1, TimeSpan.FromSeconds(10),
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
