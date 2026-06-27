using System.Collections.Generic;
using System.Text.Json;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Pure builders for the AnkiConnect HTTP API (v6) request bodies — no HttpClient dependency, so the request
/// shape is unit-testable. The WPF layer (AnkiConnectSender) POSTs these strings to the local endpoint.
/// Property names are written with the exact casing AnkiConnect expects (note the capitalized
/// <c>Name</c>/<c>Front</c>/<c>Back</c> inside cardTemplates), so NO naming policy is applied.
/// </summary>
public static class AnkiConnectRequests
{
    /// <summary>Default AnkiConnect endpoint (the add-on listens here when Anki is running).</summary>
    public const string DefaultUrl = "http://localhost:8765";

    /// <summary>The note type LLPlayer creates/uses. Matches the .apkg model so both export paths agree.</summary>
    public const string ModelName = AnkiApkgModel.ModelName;

    private static readonly JsonSerializerOptions Plain = new();

    /// <summary><c>{"action":"version","version":6}</c> — used to probe that AnkiConnect is reachable.</summary>
    public static string BuildVersion() =>
        JsonSerializer.Serialize(new { action = "version", version = 6 }, Plain);

    /// <summary><c>createDeck</c> — idempotent; creating an existing deck is a no-op for AnkiConnect.</summary>
    public static string BuildCreateDeck(string deckName) =>
        JsonSerializer.Serialize(new { action = "createDeck", version = 6, @params = new { deck = deckName } }, Plain);

    /// <summary>
    /// <c>createModel</c> for the 5-field LLPlayer note type. Lets the AnkiConnect path work without first
    /// importing the .apkg. If the model already exists AnkiConnect returns an error which the caller ignores.
    /// </summary>
    public static string BuildCreateModel() =>
        JsonSerializer.Serialize(new
        {
            action = "createModel",
            version = 6,
            @params = new
            {
                modelName = ModelName,
                inOrderFields = AnkiApkgModel.FieldNames,
                css = AnkiApkgModel.CardCss,
                isCloze = false,
                cardTemplates = new[]
                {
                    new
                    {
                        Name = "Card 1",
                        Front = AnkiApkgModel.CardFront,
                        Back = AnkiApkgModel.CardBack
                    }
                }
            }
        }, Plain);

    /// <summary>
    /// <c>addNotes</c> for the given words into <paramref name="deckName"/>. Duplicates are rejected per-deck
    /// (AnkiConnect returns a null id for each skipped note). Tagged <c>llplayer</c>.
    /// </summary>
    public static string BuildAddNotes(IReadOnlyList<SavedWord> words, string deckName)
    {
        var notes = new List<object>(words.Count);
        foreach (SavedWord w in words)
        {
            notes.Add(new
            {
                deckName,
                modelName = ModelName,
                fields = new Dictionary<string, string>
                {
                    ["Term"] = w.Term ?? "",
                    ["Reading"] = w.Reading ?? "",
                    ["Translation"] = w.Translation ?? "",
                    ["Definition"] = w.Definition ?? "",
                    ["Example"] = w.Example ?? ""
                },
                options = new { allowDuplicate = false, duplicateScope = "deck" },
                tags = new[] { "llplayer" }
            });
        }

        return JsonSerializer.Serialize(new
        {
            action = "addNotes",
            version = 6,
            @params = new { notes }
        }, Plain);
    }
}
