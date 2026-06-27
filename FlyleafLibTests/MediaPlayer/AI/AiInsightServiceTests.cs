using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

public class AiInsightServiceTests
{
    // Fake LLM: records each prompt + the max-output cap, and replies via a (joinedPrompt, callIndex) function.
    private sealed class FakeLlm
    {
        public readonly List<string> Prompts = [];
        public readonly List<int?> MaxTokens = [];
        private readonly Func<string, int, string> _respond;

        public FakeLlm(Func<string, int, string> respond) => _respond = respond;

        public AiInsightService.ChatCompletion Delegate => (msgs, max, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            string all = string.Join("\n", msgs.Select(m => $"{m.role}:{m.content}"));
            int idx = Prompts.Count;
            Prompts.Add(all);
            MaxTokens.Add(max);
            return Task.FromResult(_respond(all, idx));
        };
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        public readonly List<T> Reports = [];
        public void Report(T value) => Reports.Add(value);
    }

    // 30 cues * 1000 chars => ~30k chars => exactly 2 chunks at the 24k budget.
    private static List<string> TwoChunkCues() =>
        Enumerable.Range(0, 30).Select(_ => new string('a', 1000)).ToList();

    [Fact]
    public async Task SingleChunk_Summary_OneCall_ReturnsReply()
    {
        var fake = new FakeLlm((_, _) => "THE SUMMARY");
        var svc = new AiInsightService(fake.Delegate);

        AiInsightResult r = await svc.GenerateAsync(
            AiInsightMode.Summary, ["hello", "world"], "English", "Russian", null, CancellationToken.None);

        r.SummaryText.Should().Be("THE SUMMARY");
        r.Vocabulary.Should().BeEmpty();
        r.PartialCoverage.Should().BeFalse();
        fake.Prompts.Should().ContainSingle();
        fake.MaxTokens[0].Should().Be(AiInsightBudget.SummaryMaxOutputTokens);
    }

    [Fact]
    public async Task SingleChunk_Both_SummaryThenVocabulary()
    {
        var fake = new FakeLlm((all, _) =>
            all.Contains("vocabulary tutor") ? "casa | | house | | en la casa" : "SUMMARY TEXT");
        var svc = new AiInsightService(fake.Delegate);

        AiInsightResult r = await svc.GenerateAsync(
            AiInsightMode.Both, ["hola"], "Spanish", "English", null, CancellationToken.None);

        r.SummaryText.Should().Be("SUMMARY TEXT");
        r.Vocabulary.Should().ContainSingle().Which.Term.Should().Be("casa");
        fake.Prompts.Should().HaveCount(2);
        // vocab call uses the vocab cap
        fake.MaxTokens.Should().Contain(AiInsightBudget.VocabMaxOutputTokens);
    }

    [Fact]
    public async Task MultiChunk_Summary_MapsThenReduces()
    {
        var fake = new FakeLlm((all, _) => all.Contains("Merge them") ? "MERGED" : "partial note");
        var svc = new AiInsightService(fake.Delegate);

        AiInsightResult r = await svc.GenerateAsync(
            AiInsightMode.Summary, TwoChunkCues(), "English", "Russian", null, CancellationToken.None);

        r.SummaryText.Should().Be("MERGED");
        fake.Prompts.Count(p => p.Contains("part ")).Should().Be(2);      // 2 map calls
        fake.Prompts.Count(p => p.Contains("Merge them")).Should().Be(1); // 1 reduce call
        fake.Prompts.Should().HaveCount(3);
    }

    [Fact]
    public async Task MultiChunk_Vocabulary_MergesAndDeduplicatesAcrossChunks()
    {
        var fake = new FakeLlm((_, idx) => idx == 0
            ? "one | | 1\ntwo | | 2"
            : "TWO | | dos\nthree | | 3");
        var svc = new AiInsightService(fake.Delegate);

        AiInsightResult r = await svc.GenerateAsync(
            AiInsightMode.Vocabulary, TwoChunkCues(), "English", "Russian", null, CancellationToken.None);

        r.Vocabulary.Select(e => e.Term).Should().Equal("one", "two", "three"); // "TWO" deduped
        r.RawVocabularyReply.Should().Contain("one").And.Contain("three");
        fake.Prompts.Should().HaveCount(2); // one vocab call per chunk, no reduce call
    }

    [Fact]
    public async Task Progress_IsReported_PerStep()
    {
        var fake = new FakeLlm((all, _) => all.Contains("Merge them") ? "M" : "p");
        var svc = new AiInsightService(fake.Delegate);
        var progress = new SyncProgress<AiInsightProgress>();

        await svc.GenerateAsync(AiInsightMode.Summary, TwoChunkCues(), "English", "Russian", progress, CancellationToken.None);

        progress.Reports.Should().NotBeEmpty();
        progress.Reports.Should().Contain(p => p.Phase.Contains("Summarizing part"));
        progress.Reports.Should().Contain(p => p.Phase.Contains("Merging"));
    }

    [Fact]
    public async Task EmptyTranscript_NoLlmCall()
    {
        var fake = new FakeLlm((_, _) => "should not be called");
        var svc = new AiInsightService(fake.Delegate);

        AiInsightResult r = await svc.GenerateAsync(
            AiInsightMode.Both, [null, "   ", ""], "English", "Russian", null, CancellationToken.None);

        r.SummaryText.Should().BeEmpty();
        r.Vocabulary.Should().BeEmpty();
        fake.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var fake = new FakeLlm((_, _) => "x");
        var svc = new AiInsightService(fake.Delegate);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => svc.GenerateAsync(
            AiInsightMode.Summary, ["a", "b"], "English", "Russian", null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task VeryLongTranscript_FlagsPartialCoverage()
    {
        // >12 chunks worth of content (12 * 24k) forces sampling + the partial-coverage flag.
        List<string> cues = Enumerable.Range(0, 320).Select(_ => new string('a', 1000)).ToList();
        var fake = new FakeLlm((all, _) => all.Contains("Merge them") ? "M" : "p");
        var svc = new AiInsightService(fake.Delegate);

        AiInsightResult r = await svc.GenerateAsync(
            AiInsightMode.Summary, cues, "English", "Russian", null, CancellationToken.None);

        r.PartialCoverage.Should().BeTrue();
        fake.Prompts.Count(p => p.Contains("part ")).Should().BeLessThanOrEqualTo(AiInsightBudget.MaxChunks);
    }
}
