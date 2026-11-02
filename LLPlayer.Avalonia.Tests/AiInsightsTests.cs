using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Tests;

public class AiInsightsTests
{
    sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }
    sealed class Fixture : IDisposable
    {
        public string DirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        public WordLibrary Library;
        public InsightsTranscriptSnapshot Snapshot = new(1, "first movie", DateTimeOffset.Parse("2026-11-02T00:00:00Z"), 0, false,
            "English", "en", "Russian", "ru", ["original cue"], false);
        public Fixture() { Directory.CreateDirectory(DirectoryPath); Library = new(Path.Combine(DirectoryPath, "words.json")); }
        public AiInsightsViewModel Create(AiInsightService? service) => new((slot, translated) => Snapshot with { Slot = slot, UseTranslated = translated },
            () => service == null ? null : new("Fixture model", service), Library);
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
    [Fact]
    public async Task No_configured_llm_or_empty_transcript_sends_no_request_and_refresh_enables_arriving_cues()
    {
        using var fixture = new Fixture(); int calls = 0;
        await using var missing = fixture.Create(null);
        Assert.False(missing.CanGenerate); await missing.StartGenerationAsync(); Assert.Contains("LLM", missing.Message);
        fixture.Snapshot = fixture.Snapshot with { CueTexts = [] };
        await using var vm = fixture.Create(new((_, _, _) => { calls++; return Task.FromResult("summary"); }));
        Assert.False(vm.CanGenerate); await vm.StartGenerationAsync(); Assert.Equal(0, calls);
        fixture.Snapshot = fixture.Snapshot with { CueTexts = ["arrived"] }; vm.Refresh(); Assert.True(vm.CanGenerate);
        vm.SelectedMode = AiInsightMode.Summary; await vm.StartGenerationAsync(); Assert.Equal("summary", vm.SummaryText);
    }
    [Fact]
    public async Task Modes_copy_save_raw_fallback_and_word_provenance_use_generation_snapshot()
    {
        using var fixture = new Fixture(); int calls = 0;
        await using var vm = fixture.Create(new((_, _, _) => Task.FromResult(++calls % 2 == 1 ? "Unstructured model prose." : "apple|reading|meaning|definition|example")));
        string? copied = null, saved = null;
        vm.CopyTextAsync = text => { copied = text; return Task.CompletedTask; };
        vm.SaveTextAsync = text => { saved = text; return Task.FromResult<string?>("chosen.txt"); };
        await vm.StartGenerationAsync(); Assert.True(vm.HasVocabulary); Assert.Equal("Unstructured model prose.", vm.SummaryText);
        fixture.Snapshot = fixture.Snapshot with { SourceLanguageCode = "ja", TargetLanguageCode = "de" };
        vm.AddToWordListCommand.Execute(null); vm.AddToWordListCommand.Execute(null);
        var word = Assert.Single(fixture.Library.GetAll()); Assert.Equal("en", word.SourceLanguage); Assert.Equal("ru", word.TargetLanguage);
        Assert.Equal(WordSource.AiInsights, word.Source);
        await vm.CopyCommand.ExecuteAsync(null); await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("Unstructured model prose.", copied); Assert.Contains("apple\treading\tmeaning", copied); Assert.Equal(copied, saved); Assert.Contains("chosen.txt", vm.Message);
        vm.SelectedMode = AiInsightMode.Vocabulary;
        await vm.StartGenerationAsync(); Assert.True(vm.ShowRawVocabulary); Assert.Equal("Unstructured model prose.", vm.RawVocabularyReply); Assert.False(vm.HasSummary);
    }
    [Fact]
    public async Task Cancel_close_and_media_invalidation_reject_uncooperative_late_provider_results_and_drain()
    {
        using var fixture = new Fixture(); var held = new TaskCompletionSource<string>(); CancellationToken token = default;
        var vm = fixture.Create(new((_, _, cancellation) => { token = cancellation; return held.Task; })); vm.SelectedMode = AiInsightMode.Summary;
        Task run = vm.StartGenerationAsync(); vm.Invalidate(); Assert.True(token.IsCancellationRequested);
        Task close = vm.DisposeAsync().AsTask(); Assert.False(close.IsCompleted);
        held.SetResult("stale"); await run; await close;
        Assert.False(vm.HasSummary); Assert.False(vm.CanGenerate); Assert.Empty(vm.Vocabulary);
    }
    [Fact]
    public async Task Input_edits_during_request_do_not_relabel_results_and_failed_word_write_stays_empty()
    {
        using var fixture = new Fixture(); var held = new TaskCompletionSource<string>();
        await using var vm = fixture.Create(new((_, _, _) => held.Task)); vm.SelectedMode = AiInsightMode.Vocabulary;
        Task run = vm.StartGenerationAsync(); vm.SelectedSubIndex = 1; vm.UseTranslated = true;
        held.SetResult("apple|reading|meaning|definition|example"); await run;
        Assert.Contains("Primary", vm.ResultSourceText); Assert.Contains("original", vm.ResultSourceText);
        Directory.CreateDirectory(Path.Combine(fixture.DirectoryPath, "words.json")); vm.AddToWordListCommand.Execute(null);
        Assert.Contains("not saved", vm.Message); Assert.Empty(fixture.Library.GetAll());
    }
    [Fact]
    public async Task Changed_media_without_ui_refresh_rejects_late_completion()
    {
        using var fixture = new Fixture(); var held = new TaskCompletionSource<string>();
        await using var vm = fixture.Create(new((_, _, _) => held.Task)); vm.SelectedMode = AiInsightMode.Summary;
        Task run = vm.StartGenerationAsync(); fixture.Snapshot = fixture.Snapshot with { MediaGeneration = 2, MediaCaption = "new movie" };
        held.SetResult("stale"); await run; Assert.False(vm.HasSummary);
    }
    [Theory]
    [InlineData(AiInsightMode.Summary, 1, true, false)]
    [InlineData(AiInsightMode.Vocabulary, 1, false, true)]
    [InlineData(AiInsightMode.Both, 2, true, true)]
    public async Task Selected_mode_controls_actual_service_requests_and_output(AiInsightMode mode, int expectedCalls, bool summary, bool vocabulary)
    {
        using var fixture = new Fixture(); int calls = 0;
        await using var vm = fixture.Create(new((_, _, _) => { calls++; return Task.FromResult("apple|a|b|c|d"); }));
        vm.SelectedMode = mode; await vm.StartGenerationAsync();
        Assert.Equal(expectedCalls, calls); Assert.Equal(summary, vm.HasSummary); Assert.Equal(vocabulary, vm.HasVocabulary);
    }
    [Fact]
    public async Task Large_transcript_uses_existing_chunk_budget_and_exposes_partial_coverage()
    {
        using var fixture = new Fixture();
        fixture.Snapshot = fixture.Snapshot with { CueTexts = Enumerable.Range(0, 14).Select(i => (string?)(i + new string('x', 14000))).ToArray(), IsGenerating = true };
        int calls = 0;
        await using var vm = fixture.Create(new((_, _, _) => { calls++; return Task.FromResult("apple|a|b|c|d"); }));
        vm.SelectedMode = AiInsightMode.Vocabulary;
        Assert.True(vm.IncompleteTranscriptWarning); Assert.Contains("Partial", vm.BudgetText);
        await vm.StartGenerationAsync(); Assert.Equal(AiInsightBudget.MaxChunks, calls); Assert.True(vm.PartialCoverageWarning); Assert.Single(vm.Vocabulary);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configuration_and_truncation_errors_are_actionable_without_publishing_partial_results(bool truncated)
    {
        using var fixture = new Fixture();
        Exception failure = truncated ? new TranslationException("cut") { Kind = TranslationFailureKind.Truncated } : new TranslationConfigException("missing model");
        await using var vm = fixture.Create(new((_, _, _) => Task.FromException<string>(failure)));
        await vm.StartGenerationAsync(); Assert.False(vm.Busy); Assert.False(vm.HasResults);
        Assert.Contains(truncated ? "token limit" : "missing model", vm.Message);
    }
    [Fact]
    public async Task Save_cancellation_and_failure_keep_results_and_close_drains_pending_picker()
    {
        using var fixture = new Fixture();
        var vm = fixture.Create(new((_, _, _) => Task.FromResult("A summary."))); vm.SelectedMode = AiInsightMode.Summary;
        await vm.StartGenerationAsync(); vm.SaveTextAsync = _ => Task.FromResult<string?>(null);
        await vm.SaveCommand.ExecuteAsync(null); Assert.Contains("cancelled", vm.Message); Assert.True(vm.HasSummary);
        vm.SaveTextAsync = _ => Task.FromException<string?>(new IOException("disk full"));
        await vm.SaveCommand.ExecuteAsync(null); Assert.Contains("disk full", vm.Message); Assert.True(vm.HasSummary);
        var picker = new TaskCompletionSource<string?>(); vm.SaveTextAsync = _ => picker.Task;
        Task save = vm.SaveCommand.ExecuteAsync(null); Task close = vm.DisposeAsync().AsTask(); Assert.False(close.IsCompleted);
        picker.SetResult(null); await save; await close; Assert.True(vm.IsClosing);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retired_capture_during_progress_or_error_does_not_escape_or_publish(bool providerFails)
    {
        using var fixture = new Fixture(); int captures = 0;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineContext());
        try
        {
            await using var vm = new AiInsightsViewModel((_, _) => ++captures <= 2 ? fixture.Snapshot : throw new InvalidOperationException("retired media"),
                () => new("fake", new((_, _, _) => providerFails ? Task.FromException<string>(new IOException("offline")) : Task.FromResult("stale summary"))), fixture.Library);
            vm.SelectedMode = AiInsightMode.Summary;
            await vm.StartGenerationAsync();
            Assert.False(vm.HasResults); Assert.Empty(vm.ProgressText); Assert.Empty(vm.Message); Assert.False(vm.Busy);
            Assert.True(captures > 2);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cheap_identity_probe_rejects_stale_or_throwing_progress_without_recapturing_cues(bool probeThrows)
    {
        using var fixture = new Fixture(); int captures = 0, probes = 0;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineContext());
        try
        {
            await using var vm = new AiInsightsViewModel((_, _) => { captures++; return fixture.Snapshot; },
                () => new("fake", new((_, _, _) => Task.FromResult("stale summary"))), fixture.Library,
                _ => { probes++; return probeThrows ? throw new InvalidOperationException("retired media") : false; });
            vm.SelectedMode = AiInsightMode.Summary;
            await vm.StartGenerationAsync();
            Assert.Equal(2, captures); Assert.True(probes >= 2); Assert.Empty(vm.ProgressText); Assert.False(vm.HasResults); Assert.Empty(vm.Message);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    [Fact]
    public async Task Failed_initial_refresh_and_slot_change_are_retryable_unready_states_without_provider_requests()
    {
        using var fixture = new Fixture(); bool unavailable = true; int requests = 0;
        await using var vm = new AiInsightsViewModel((slot, translated) => unavailable
                ? throw new InvalidOperationException("native source replaced") : fixture.Snapshot with { Slot = slot, UseTranslated = translated },
            () => new("fake", new((_, _, _) => { requests++; return Task.FromResult("A summary."); })), fixture.Library,
            _ => true);
        Assert.False(vm.CanGenerate); Assert.Contains("unavailable", vm.Message); Assert.Equal(0, requests);
        unavailable = false; vm.Refresh(); Assert.True(vm.CanGenerate); Assert.Empty(vm.Message);
        vm.SelectedMode = AiInsightMode.Summary; await vm.StartGenerationAsync(); Assert.True(vm.HasSummary); Assert.Equal(1, requests);
        unavailable = true; vm.SelectedSubIndex = 1;
        Assert.False(vm.CanGenerate); Assert.False(vm.HasResults); Assert.Empty(vm.ResultSourceText); Assert.Equal(1, requests);
        unavailable = false; vm.Refresh(); Assert.True(vm.CanGenerate);
        await vm.StartGenerationAsync(); Assert.Equal(2, requests); Assert.Contains("Secondary", vm.ResultSourceText);
    }
    [Fact]
    public async Task Failed_generate_capture_sends_nothing_retires_prior_results_and_can_retry()
    {
        using var fixture = new Fixture(); bool unavailable = false; int requests = 0;
        await using var vm = new AiInsightsViewModel((_, _) => unavailable
                ? throw new InvalidOperationException("native source replaced") : fixture.Snapshot,
            () => new("fake", new((_, _, _) => { requests++; return Task.FromResult("A summary."); })), fixture.Library,
            _ => true);
        vm.SelectedMode = AiInsightMode.Summary; await vm.StartGenerationAsync(); Assert.Equal(1, requests); Assert.True(vm.HasSummary);
        unavailable = true; await vm.StartGenerationAsync();
        Assert.Equal(1, requests); Assert.False(vm.CanGenerate); Assert.False(vm.HasResults); Assert.False(vm.Busy); Assert.Contains("unavailable", vm.Message);
        unavailable = false; vm.Refresh(); await vm.StartGenerationAsync(); Assert.Equal(2, requests); Assert.True(vm.HasSummary);
    }
}
