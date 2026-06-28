using System;
using System.Linq;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Pure prompt builder + reply parser for the LLM word-definition path, mirroring
/// <see cref="AiInsightPrompts"/> (literal <c>.Replace</c> placeholder substitution, same
/// <see cref="OpenAIMessage"/> shape) and <see cref="VocabularyParser"/> (tolerant pipe parsing that never
/// throws). The model is asked for ONE line "reading | definition" so the parse is trivial and bounded.
/// </summary>
public static class WordDefinitionPrompts
{
    private const string SourceLangFallback = "the source language";

    private const string DefinitionSystem =
        "You are a concise bilingual dictionary for a language learner. You are given a single {source_lang} " +
        "word or phrase (optionally with the sentence it appeared in for disambiguation). Reply with EXACTLY " +
        "one line and nothing else, in this pipe-delimited form:\n" +
        "reading | definition\n" +
        "- reading: pronunciation/romanization of the {source_lang} term if helpful (e.g. IPA, pinyin, romaji), " +
        "else leave empty.\n" +
        "- definition: a short, clear definition of the term written in {target_lang} (one sentence). Define the " +
        "sense used in the context when given.\n" +
        "Use a literal \" | \" separator. No headings, no preamble, no quotes around the line, no extra lines.";

    private const string DefinitionUser =
        "Term ({source_lang}): {word}\nContext sentence: {context}";

    /// <summary>Build the system+user messages defining <paramref name="word"/> in the target language.</summary>
    public static OpenAIMessage[] BuildDefinition(string word, string? sourceLangName, string targetLangName, string? context)
    {
        string system = ApplyLangs(DefinitionSystem, sourceLangName, targetLangName);
        string user = ApplyLangs(DefinitionUser, sourceLangName, targetLangName)
            .Replace("{word}", word ?? string.Empty)
            // Context substituted LAST so a sentence containing a literal "{target_lang}" etc. is left verbatim.
            .Replace("{context}", string.IsNullOrWhiteSpace(context) ? "(none)" : context!.Trim());

        return
        [
            new OpenAIMessage { role = "system", content = system },
            new OpenAIMessage { role = "user", content = user },
        ];
    }

    /// <summary>
    /// Parse the model's "reading | definition" reply into a single-sense <see cref="DictionaryEntry"/>. Tolerant:
    /// a reply without a pipe is treated as the definition only; an empty/blank reply or one with no definition
    /// yields <see cref="DictionaryEntry.Empty"/>. The definition is capped at
    /// <see cref="WordDefinitionBudget.MaxDefinitionChars"/>. Never throws.
    /// </summary>
    public static DictionaryEntry ParseLlmReply(string reply, string word)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return DictionaryEntry.Empty;

        string? line = reply
            .Replace("\r", string.Empty)
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (string.IsNullOrEmpty(line))
            return DictionaryEntry.Empty;

        string reading = string.Empty;
        string definition;

        int pipe = line.IndexOf('|');
        if (pipe >= 0)
        {
            reading = line[..pipe].Trim();
            definition = line[(pipe + 1)..].Trim();
        }
        else
        {
            definition = line.Trim();
        }

        if (definition.Length > WordDefinitionBudget.MaxDefinitionChars)
            definition = definition[..WordDefinitionBudget.MaxDefinitionChars].TrimEnd();

        if (string.IsNullOrWhiteSpace(definition))
            return DictionaryEntry.Empty;

        return new DictionaryEntry(word ?? string.Empty, reading, [new DictionarySense(string.Empty, definition, string.Empty)]);
    }

    private static string ApplyLangs(string template, string? sourceLangName, string targetLangName) =>
        template
            .Replace("{source_lang}", string.IsNullOrWhiteSpace(sourceLangName) ? SourceLangFallback : sourceLangName)
            .Replace("{target_lang}", targetLangName);
}
