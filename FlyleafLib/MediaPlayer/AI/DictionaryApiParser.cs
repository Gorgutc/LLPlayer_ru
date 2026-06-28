using System.Collections.Generic;
using System.Text.Json;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Pure parser for the free dictionaryapi.dev v2 response. Extracted from the HTTP call (mirroring
/// <see cref="Translation.Services.GoogleV1TranslateService.ParseGoogleV1"/>) so it is unit-testable without a
/// network. Unlike the translate parser it NEVER throws: a word missing from the dictionary is normal, not an
/// error, so the 404 body (a JSON object {title,message}), an empty array, or any malformed input all return
/// <see cref="DictionaryEntry.Empty"/>.
/// </summary>
public static class DictionaryApiParser
{
    /// <summary>
    /// Parse a dictionaryapi.dev success body (a JSON array of entry objects) into a <see cref="DictionaryEntry"/>.
    /// Returns <see cref="DictionaryEntry.Empty"/> for the not-found object shape, an empty array, or anything
    /// unexpected. Never throws.
    /// </summary>
    public static DictionaryEntry ParseDictionaryApiDev(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return DictionaryEntry.Empty;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            // Success bodies are a non-empty array; the 404 not-found body is an OBJECT {title,message,...}.
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return DictionaryEntry.Empty;

            string term = GetString(root[0], "word");
            string reading = ExtractPhonetic(root);

            List<DictionarySense> senses = new();
            foreach (JsonElement entry in root.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;
                if (!entry.TryGetProperty("meanings", out JsonElement meanings) || meanings.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement meaning in meanings.EnumerateArray())
                {
                    if (meaning.ValueKind != JsonValueKind.Object)
                        continue;

                    string pos = GetString(meaning, "partOfSpeech");
                    if (!meaning.TryGetProperty("definitions", out JsonElement defs) || defs.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (JsonElement d in defs.EnumerateArray())
                    {
                        if (d.ValueKind != JsonValueKind.Object)
                            continue;

                        string definition = GetString(d, "definition");
                        if (string.IsNullOrWhiteSpace(definition))
                            continue;

                        senses.Add(new DictionarySense(pos, definition, GetString(d, "example")));
                    }
                }
            }

            return senses.Count == 0 ? DictionaryEntry.Empty : new DictionaryEntry(term, reading, senses);
        }
        catch
        {
            // Malformed / non-JSON body: a missing definition is best-effort, so degrade to "not found".
            return DictionaryEntry.Empty;
        }
    }

    // The top-level "phonetic" first; else the first non-empty "phonetics[].text" across all entries.
    private static string ExtractPhonetic(JsonElement root)
    {
        foreach (JsonElement entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            string top = GetString(entry, "phonetic");
            if (!string.IsNullOrWhiteSpace(top))
                return top.Trim();
        }

        foreach (JsonElement entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            if (!entry.TryGetProperty("phonetics", out JsonElement phonetics) || phonetics.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement p in phonetics.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object)
                    continue;

                string text = GetString(p, "text");
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
        }

        return string.Empty;
    }

    private static string GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out JsonElement v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
