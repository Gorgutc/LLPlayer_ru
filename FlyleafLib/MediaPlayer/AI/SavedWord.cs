using System;
using System.Text.Json.Serialization;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>How a <see cref="SavedWord"/> entered the global word list.</summary>
public enum WordSource
{
    /// <summary>Saved from the word-click translation popup / right-click menu while watching.</summary>
    WordClick,

    /// <summary>Bulk-added from the AI Insights extracted vocabulary.</summary>
    AiInsights
}

/// <summary>
/// One persisted entry in the global cumulative word list (F-10). Wraps the five Anki-ready fields of
/// <see cref="VocabularyEntry"/> (so existing TSV/Anki export maps directly) and adds provenance metadata.
/// The dedup identity is <see cref="Term"/> (case-insensitive, trimmed). All string fields are non-null;
/// the word-click path leaves <see cref="Reading"/>/<see cref="Definition"/> empty (the LLM did not run) and
/// stores the subtitle cue as <see cref="Example"/> — semantically the word's usage sentence, matching the
/// AI Insights path where <see cref="Example"/> is the LLM-generated example.
/// </summary>
public sealed record SavedWord(
    string Term,
    string Reading,
    string Translation,
    string Definition,
    string Example,
    string SourceLanguage,
    string TargetLanguage,
    WordSource Source,
    string SavedAtUtc)
{
    /// <summary>Case-insensitive, trimmed dedup key. Same first-wins rule as <see cref="VocabularyParser.Merge"/>.</summary>
    [JsonIgnore]
    public string DedupeKey => (Term ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Project back to the five-field card model for TSV / .apkg / AnkiConnect export.</summary>
    public VocabularyEntry ToVocabularyEntry() =>
        new(Term, Reading, Translation, Definition, Example);

    /// <summary>Build a saved word from an AI Insights vocabulary entry plus provenance.</summary>
    public static SavedWord FromVocabularyEntry(
        VocabularyEntry e, string sourceLanguage, string targetLanguage, WordSource source, string savedAtUtc) =>
        new(e.Term ?? "", e.Reading ?? "", e.Translation ?? "", e.Definition ?? "", e.Example ?? "",
            sourceLanguage, targetLanguage, source, savedAtUtc);

    /// <summary>Return a copy with every null string field coerced to "" (tolerant load of partial JSON).</summary>
    public SavedWord NormalizeNulls() =>
        new(Term ?? "", Reading ?? "", Translation ?? "", Definition ?? "", Example ?? "",
            SourceLanguage ?? "", TargetLanguage ?? "", Source, SavedAtUtc ?? "");
}
