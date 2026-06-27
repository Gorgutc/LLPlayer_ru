using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Orchestrates AI insight generation: chunk the transcript, map/reduce-summarize and/or extract+merge
/// vocabulary, reporting progress. The LLM call is injected as a <see cref="ChatCompletion"/> delegate so the
/// map/reduce orchestration is unit-testable with a fake (the only untestable part — the real HTTP call — is
/// the one-line <see cref="ForSettings"/> binding to <see cref="OpenAIBaseTranslateService.CompleteAsync"/>).
/// </summary>
public sealed class AiInsightService
{
    /// <summary>Sends a chat completion and returns the reply text. <paramref name="maxOutputTokens"/> bounds output.</summary>
    public delegate Task<string> ChatCompletion(
        IReadOnlyList<OpenAIMessage> messages, int? maxOutputTokens, CancellationToken token);

    private readonly ChatCompletion _complete;

    public AiInsightService(ChatCompletion complete) =>
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));

    /// <summary>Production binding: drive insights through the configured LLM's settings.</summary>
    public static AiInsightService ForSettings(OpenAIBaseTranslateSettings settings) =>
        new((messages, max, token) => OpenAIBaseTranslateService.CompleteAsync(settings, messages, max, token));

    /// <summary>
    /// Generates the requested insight(s) for a transcript built from <paramref name="cueTexts"/>. Throws
    /// <see cref="TranslationException"/> (e.g. Kind=Truncated) / <see cref="TranslationConfigException"/> from the
    /// underlying completion so the caller can surface a precise error (never a silently truncated result).
    /// </summary>
    public async Task<AiInsightResult> GenerateAsync(
        AiInsightMode mode,
        IReadOnlyList<string?> cueTexts,
        string? sourceLangName,
        string targetLangName,
        IProgress<AiInsightProgress>? progress,
        CancellationToken token)
    {
        string transcript = AiTranscript.Build(cueTexts);
        IReadOnlyList<string> chunks =
            AiTranscript.Chunk(transcript, AiInsightBudget.MaxInputCharsPerChunk, out bool partialCoverage);

        bool doSummary = mode is AiInsightMode.Summary or AiInsightMode.Both;
        bool doVocab = mode is AiInsightMode.Vocabulary or AiInsightMode.Both;

        if (chunks.Count == 0)
        {
            // Empty transcript: nothing to send. Return empty (the VM also guards this before calling).
            return new AiInsightResult(doSummary ? string.Empty : null, [], null, partialCoverage);
        }

        string? summary = null;
        IReadOnlyList<VocabularyEntry> vocab = [];
        string? rawVocab = null;

        if (doSummary)
            summary = await SummarizeAsync(chunks, sourceLangName, targetLangName, progress, token);

        if (doVocab)
            (vocab, rawVocab) = await ExtractVocabularyAsync(chunks, sourceLangName, targetLangName, progress, token);

        return new AiInsightResult(summary, vocab, rawVocab, partialCoverage);
    }

    private async Task<string> SummarizeAsync(
        IReadOnlyList<string> chunks, string? src, string tgt,
        IProgress<AiInsightProgress>? progress, CancellationToken token)
    {
        if (chunks.Count == 1)
        {
            progress?.Report(new AiInsightProgress(1, 1, "Summarizing"));
            OpenAIMessage[] msgs = AiInsightPrompts.BuildSummarySingle(new AiInsightOptions(chunks[0], src, tgt));
            return await _complete(msgs, AiInsightBudget.SummaryMaxOutputTokens, token);
        }

        // Map: one short partial summary per chunk.
        List<string> partials = new(chunks.Count);
        for (int i = 0; i < chunks.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new AiInsightProgress(i + 1, chunks.Count, "Summarizing part"));
            OpenAIMessage[] msgs =
                AiInsightPrompts.BuildSummaryMap(new AiInsightOptions(chunks[i], src, tgt), i + 1, chunks.Count);
            partials.Add(await _complete(msgs, AiInsightBudget.SummaryMaxOutputTokens, token));
        }

        // Reduce: merge the (bounded) partials into one final summary.
        token.ThrowIfCancellationRequested();
        progress?.Report(new AiInsightProgress(chunks.Count, chunks.Count, "Merging summary"));
        OpenAIMessage[] reduce = AiInsightPrompts.BuildSummaryReduce(new AiInsightOptions(string.Empty, src, tgt), partials);
        return await _complete(reduce, AiInsightBudget.SummaryMaxOutputTokens, token);
    }

    private async Task<(IReadOnlyList<VocabularyEntry> entries, string raw)> ExtractVocabularyAsync(
        IReadOnlyList<string> chunks, string? src, string tgt,
        IProgress<AiInsightProgress>? progress, CancellationToken token)
    {
        List<IReadOnlyList<VocabularyEntry>> perChunk = new(chunks.Count);
        StringBuilder raw = new();

        for (int i = 0; i < chunks.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new AiInsightProgress(i + 1, chunks.Count, "Extracting vocabulary"));
            OpenAIMessage[] msgs =
                AiInsightPrompts.BuildVocabulary(new AiInsightOptions(chunks[i], src, tgt), AiInsightBudget.VocabularyMaxCount);
            string reply = await _complete(msgs, AiInsightBudget.VocabMaxOutputTokens, token);

            if (raw.Length > 0)
                raw.Append("\n\n");
            raw.Append(reply);
            perChunk.Add(VocabularyParser.Parse(reply));
        }

        IReadOnlyList<VocabularyEntry> merged = VocabularyParser.Merge(perChunk, AiInsightBudget.VocabularyMaxCount);
        return (merged, raw.ToString());
    }
}
