using System.Collections.Generic;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>What the AI insights action produces.</summary>
public enum AiInsightMode
{
    Summary,
    Vocabulary,
    Both
}

/// <summary>
/// One vocabulary item extracted from a transcript. Sized so a later Anki/SRS export (F-10) can map it
/// directly: <see cref="Term"/> (+ <see cref="Reading"/>) become the card front, and
/// <see cref="Translation"/>/<see cref="Definition"/>/<see cref="Example"/> the card back. Only
/// <see cref="Term"/> is guaranteed non-empty; the other fields may be "".
/// </summary>
public sealed record VocabularyEntry(
    string Term,
    string Reading,
    string Translation,
    string Definition,
    string Example);

/// <summary>
/// Read-only inputs for one prompt: an already-bounded transcript (single chunk) plus the language names.
/// <see cref="SourceLangName"/> may be null (unknown) — the prompt builders substitute a neutral phrase.
/// </summary>
public sealed record AiInsightOptions(
    string Transcript,
    string? SourceLangName,
    string TargetLangName);

/// <summary>Progress tick for the dialog's busy indicator.</summary>
public sealed record AiInsightProgress(int Current, int Total, string Phase);

/// <summary>
/// Final result of one generation. <see cref="SummaryText"/> and/or <see cref="Vocabulary"/> are populated
/// depending on the requested <see cref="AiInsightMode"/>. <see cref="RawVocabularyReply"/> is the unparsed
/// model text, kept so the UI can show it verbatim when structured parsing yields nothing from a non-empty
/// reply. <see cref="PartialCoverage"/> is true when the transcript was too long and chunks were sampled.
/// </summary>
public sealed record AiInsightResult(
    string? SummaryText,
    IReadOnlyList<VocabularyEntry> Vocabulary,
    string? RawVocabularyReply,
    bool PartialCoverage);

/// <summary>
/// Code-level budgets/caps for AI insights. Deliberately NOT persisted config (see F-07 design): the feature
/// reuses the existing translation LLM settings and adds no config-data surface.
/// </summary>
public static class AiInsightBudget
{
    /// <summary>Rough chars-per-token heuristic (no tokenizer dependency); deliberately conservative.</summary>
    public const int CharsPerToken = 4;

    /// <summary>
    /// Max transcript characters fed to a single LLM call (~6k tokens), leaving headroom for the prompt and
    /// the generated output even on an 8k-context local model.
    /// </summary>
    public const int MaxInputCharsPerChunk = 24_000;

    /// <summary>Output token cap per summary map/reduce step (each is short by construction).</summary>
    public const int SummaryMaxOutputTokens = 1024;

    /// <summary>Output token cap for a vocabulary call (~30 pipe-delimited entries).</summary>
    public const int VocabMaxOutputTokens = 2048;

    /// <summary>Max vocabulary entries kept after merge/dedupe.</summary>
    public const int VocabularyMaxCount = 30;

    /// <summary>
    /// Hard ceiling on chunks; beyond it chunks are sampled evenly across the transcript and the result is
    /// flagged <see cref="AiInsightResult.PartialCoverage"/>. Only triggers on extreme (~4h+) inputs.
    /// </summary>
    public const int MaxChunks = 12;
}
