using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Pure prompt builders for AI insights (summary + vocabulary), parameterized by source/target language.
/// No HTTP/UI; returns the same <see cref="OpenAIMessage"/> shape the translation path uses. Placeholders are
/// substituted the same way as <c>OpenAIBaseTranslateService.Initialize</c> (literal <c>.Replace</c>).
/// </summary>
public static class AiInsightPrompts
{
    // When the transcript language is unknown, the model is told to infer it rather than being given a name.
    private const string SourceLangFallback = "the source language";

    #region Summary
    private const string SummarySystem =
        "You are a concise study assistant for a language learner. You summarize a transcript of spoken " +
        "dialogue from a video, in {source_lang}. Write the summary in {target_lang}. Be faithful: never " +
        "invent facts not present in the transcript. Output plain prose and bullet points only - no preamble, " +
        "no apologies, no notes about being an AI.";

    private const string SummarySingleUser =
        "Summarize this {source_lang} transcript in {target_lang}. Produce:\n" +
        "1. A 4-8 sentence overview of what happens.\n" +
        "2. 3-6 bullet points of the key topics or plot beats.\n\n" +
        "TRANSCRIPT:\n{transcript}";

    private const string SummaryMapUser =
        "This is part {part}/{total} of a longer {source_lang} transcript. In {target_lang}, give 2-4 plain " +
        "bullet points capturing ONLY this part's content. No headings, no preamble.\n\nTRANSCRIPT:\n{transcript}";

    private const string SummaryReduceUser =
        "Below are ordered partial notes for one {source_lang} video. Merge them into a single coherent summary " +
        "in {target_lang}: a 4-8 sentence overview followed by 3-6 key bullet points, in chronological order. " +
        "Remove duplication.\n\n{partials}";
    #endregion

    #region Vocabulary
    private const string VocabularySystem =
        "You are a {source_lang} vocabulary tutor for a learner whose study language is {target_lang}.\n" +
        "From the transcript, select the {count} most useful words/phrases to study (prefer mid-to-advanced, " +
        "idiomatic, topic-relevant items; skip trivial function words and proper names).\n" +
        "Output ONE entry per line, pipe-delimited, EXACTLY these 5 fields and nothing else:\n" +
        "term | reading | translation | definition | example\n" +
        "- term: the {source_lang} word/phrase in its dictionary (base) form.\n" +
        "- reading: pronunciation/romanization if helpful (e.g. pinyin/romaji), else leave empty.\n" +
        "- translation: a short gloss in {target_lang}.\n" +
        "- definition: a short definition in {target_lang} (may be empty).\n" +
        "- example: the transcript sentence using term, in {source_lang}.\n" +
        "Use a literal \" | \" separator. Do not number lines. No header row, no markdown, no commentary.";

    private const string VocabularyUser =
        "TRANSCRIPT ({source_lang}):\n{transcript}";
    #endregion

    /// <summary>Single-call summary (transcript fits one chunk).</summary>
    public static OpenAIMessage[] BuildSummarySingle(AiInsightOptions o) =>
    [
        System(SummarySystem, o),
        User(ApplyTranscript(ApplyLangs(SummarySingleUser, o), o.Transcript)),
    ];

    /// <summary>Map step: summarize one chunk (part <paramref name="part"/> of <paramref name="total"/>).</summary>
    public static OpenAIMessage[] BuildSummaryMap(AiInsightOptions o, int part, int total)
    {
        string user = ApplyLangs(SummaryMapUser, o)
            .Replace("{part}", part.ToString())
            .Replace("{total}", total.ToString());
        return
        [
            System(SummarySystem, o),
            User(ApplyTranscript(user, o.Transcript)),
        ];
    }

    /// <summary>Reduce step: merge the ordered partial summaries into one final summary.</summary>
    public static OpenAIMessage[] BuildSummaryReduce(AiInsightOptions o, IReadOnlyList<string> partials)
    {
        string joined = string.Join("\n\n", partials.Select((p, i) => $"Part {i + 1}:\n{p}"));
        string user = ApplyLangs(SummaryReduceUser, o).Replace("{partials}", joined);
        return
        [
            System(SummarySystem, o),
            User(user),
        ];
    }

    /// <summary>Vocabulary extraction for one chunk (up to <paramref name="maxCount"/> entries).</summary>
    public static OpenAIMessage[] BuildVocabulary(AiInsightOptions o, int maxCount)
    {
        string system = ApplyLangs(VocabularySystem, o).Replace("{count}", maxCount.ToString());
        return
        [
            System(system, o, applyLangs: false),
            User(ApplyTranscript(ApplyLangs(VocabularyUser, o), o.Transcript)),
        ];
    }

    private static OpenAIMessage System(string template, AiInsightOptions o, bool applyLangs = true) =>
        new() { role = "system", content = applyLangs ? ApplyLangs(template, o) : template };

    private static OpenAIMessage User(string content) =>
        new() { role = "user", content = content };

    private static string ApplyLangs(string template, AiInsightOptions o) =>
        template
            .Replace("{source_lang}", string.IsNullOrWhiteSpace(o.SourceLangName) ? SourceLangFallback : o.SourceLangName)
            .Replace("{target_lang}", o.TargetLangName);

    // Substitute the transcript LAST so cue text containing a literal "{target_lang}" etc. is left verbatim.
    private static string ApplyTranscript(string template, string transcript) =>
        template.Replace("{transcript}", transcript);
}
