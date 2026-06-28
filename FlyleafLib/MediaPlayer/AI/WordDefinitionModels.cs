using System;
using System.Collections.Generic;
using System.Text;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// F-11: which provider supplies the dictionary DEFINITION shown in the word-click popup (and used to
/// auto-fill the F-10 word list's Reading/Definition on Save). Persisted on
/// <see cref="Config.SubtitlesConfig.WordDefinitionServiceType"/> as a string (JsonStringEnumConverter), so a
/// pre-F-11 config has no key and deserializes to <see cref="Off"/> — no migration. <see cref="Off"/> is the
/// default and makes the popup byte-identical to before this feature.
/// </summary>
public enum WordDefinitionServiceType
{
    /// <summary>No definition lookup (default). The popup shows only the translation, exactly as before.</summary>
    Off,

    /// <summary>
    /// Owner's "both" choice: dictionaryapi.dev for English source words; the configured LLM for other
    /// languages, and also as a fallback when the free dictionary has no entry for an English word.
    /// </summary>
    Auto,

    /// <summary>Force the free dictionaryapi.dev lookup (English source words only).</summary>
    DictionaryApi,

    /// <summary>Force a configured-LLM definition (any source language; defined in the target language).</summary>
    Llm
}

/// <summary>The provider actually chosen at lookup time by <see cref="WordDefinitionSelector"/>.</summary>
public enum WordDefinitionProvider
{
    /// <summary>No usable provider for this word (skip the lookup entirely).</summary>
    None,
    DictionaryApi,
    Llm
}

/// <summary>One sense of a word: a part of speech, a definition, and an optional usage example.</summary>
public sealed record DictionarySense(string PartOfSpeech, string Definition, string Example);

/// <summary>
/// A parsed dictionary entry for one word, shared by the dictionaryapi.dev and LLM paths so the popup render
/// and the F-10 Save auto-fill have a single shape (mirrors <see cref="VocabularyEntry"/>'s role). All string
/// fields are non-null; <see cref="Senses"/> is empty when nothing was found (<see cref="IsEmpty"/>).
/// </summary>
public sealed record DictionaryEntry(string Term, string Reading, IReadOnlyList<DictionarySense> Senses)
{
    /// <summary>The not-found / no-definition sentinel. Renders to "" and fills Save's fields with "".</summary>
    public static DictionaryEntry Empty { get; } = new(string.Empty, string.Empty, Array.Empty<DictionarySense>());

    /// <summary>True when there is no usable sense (treated as "no definition" — the popup row stays hidden).</summary>
    public bool IsEmpty => Senses.Count == 0;

    /// <summary>
    /// Multi-line text shown in the popup definition row: an optional reading line, then up to
    /// <paramref name="maxSenses"/> senses rendered as "(pos) definition - \"example\"".
    /// </summary>
    public string FlattenForDisplay(int maxSenses = WordDefinitionBudget.MaxDisplaySenses) =>
        Flatten(maxSenses, includeReading: true);

    /// <summary>
    /// Project to the two F-10 fields the word-click Save previously left blank: Reading (the phonetic) and a
    /// flattened Definition (senses only, capped to <paramref name="maxSenses"/> so an Anki card stays small).
    /// Empty entry -> ("", "") so Save stays byte-identical when no definition was fetched.
    /// </summary>
    public (string Reading, string Definition) ToFields(int maxSenses = WordDefinitionBudget.MaxSavedSenses) =>
        IsEmpty ? (string.Empty, string.Empty) : (Reading ?? string.Empty, Flatten(maxSenses, includeReading: false));

    private string Flatten(int maxSenses, bool includeReading)
    {
        StringBuilder sb = new();

        if (includeReading && !string.IsNullOrWhiteSpace(Reading))
            sb.Append(Reading.Trim());

        int n = 0;
        foreach (DictionarySense s in Senses)
        {
            if (n >= maxSenses)
                break;
            if (string.IsNullOrWhiteSpace(s.Definition))
                continue;

            if (sb.Length > 0)
                sb.Append('\n');

            if (!string.IsNullOrWhiteSpace(s.PartOfSpeech))
                sb.Append('(').Append(s.PartOfSpeech.Trim()).Append(") ");

            sb.Append(s.Definition.Trim());

            if (!string.IsNullOrWhiteSpace(s.Example))
                sb.Append(" - \"").Append(s.Example.Trim()).Append('"');

            n++;
        }

        return sb.ToString();
    }
}

/// <summary>
/// Code-level caps for the word-definition feature (not persisted config — like <see cref="AiInsightBudget"/>,
/// the feature reuses the existing translation LLM settings and adds no extra config surface beyond the mode).
/// </summary>
public static class WordDefinitionBudget
{
    /// <summary>Overall HTTP timeout for the dictionaryapi.dev request (a best-effort, low-latency lookup).</summary>
    public const int HttpTimeoutMs = 8000;

    /// <summary>Output token cap for an LLM definition (one short line: reading | definition).</summary>
    public const int MaxOutputTokens = 256;

    /// <summary>Hard cap on a single parsed definition string (guards a runaway LLM reply).</summary>
    public const int MaxDefinitionChars = 600;

    /// <summary>Senses rendered in the popup.</summary>
    public const int MaxDisplaySenses = 6;

    /// <summary>Senses kept in the saved Anki Definition field (card-sized).</summary>
    public const int MaxSavedSenses = 3;
}
