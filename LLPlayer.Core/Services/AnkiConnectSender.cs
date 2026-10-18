using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlyleafLib.MediaPlayer.AI;

namespace LLPlayer.Services;

/// <summary>
/// Pushes the word list to a running Anki via the AnkiConnect add-on (localhost:8765). Request bodies come
/// from the pure <see cref="AnkiConnectRequests"/> and responses are parsed by the pure
/// <see cref="AnkiConnectResponses"/>; this class only performs the HTTP I/O. It is self-sufficient — it
/// creates the deck and the "LLPlayer" note type first, so the user does not have to import the .apkg
/// beforehand. Fails soft with a clear message when Anki is not running or a step reports an error.
/// </summary>
public sealed class AnkiConnectSender
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly HttpClient http;

    public AnkiConnectSender() : this(Http) { }

    /// <summary>The caller owns the supplied client; no HTTP request occurs before PushAsync.</summary>
    public AnkiConnectSender(HttpClient http) => this.http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<AnkiAddNotesOutcome> PushAsync(
        IReadOnlyList<SavedWord> words, string deckName, CancellationToken token = default)
    {
        if (words.Count == 0)
        {
            return new AnkiAddNotesOutcome(0, 0, "There are no words to push.");
        }

        string deck = string.IsNullOrWhiteSpace(deckName) ? "LLPlayer" : deckName.Trim();

        try
        {
            // Probe — fails fast with a clear message if Anki/AnkiConnect is not reachable.
            string versionReply = await PostAsync(AnkiConnectRequests.BuildVersion(), token);
            string? versionError = GetPreparationError(versionReply, requireVersion: true);
            if (versionError != null)
                return new AnkiAddNotesOutcome(0, 0, $"AnkiConnect version probe failed: {versionError}");

            string deckReply = await PostAsync(AnkiConnectRequests.BuildCreateDeck(deck), token);
            string? deckError = GetPreparationError(deckReply, requireDeckId: true);
            if (deckError != null)
                return new AnkiAddNotesOutcome(0, 0, $"AnkiConnect could not create the deck: {deckError}");

            // createModel reports "Model name already exists" on every run after the first — that is expected and
            // ignored; any OTHER error means the note type could not be set up, so surface it instead of pushing
            // notes against a missing/mismatched model.
            string createModelReply = await PostAsync(AnkiConnectRequests.BuildCreateModel(), token);
            string? modelError = GetPreparationError(createModelReply, requireModel: true);
            if (modelError != null && !AnkiConnectResponses.IsAlreadyExists(modelError))
            {
                return new AnkiAddNotesOutcome(0, 0, $"AnkiConnect could not create the note type: {modelError}");
            }

            string reply = await PostAsync(AnkiConnectRequests.BuildAddNotes(words, deck), token);
            string? notesError = GetPreparationError(reply);
            if (notesError != null) return new AnkiAddNotesOutcome(0, 0, notesError);
            using JsonDocument notesReply = JsonDocument.Parse(reply);
            JsonElement ids = notesReply.RootElement.GetProperty("result");
            if (ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() != words.Count)
                return new AnkiAddNotesOutcome(0, 0, "Unexpected response from AnkiConnect.");
            foreach (JsonElement id in ids.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.Null &&
                    (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out long noteId) || noteId <= 0))
                    return new AnkiAddNotesOutcome(0, 0, "Unexpected response from AnkiConnect.");
            }
            return AnkiConnectResponses.ParseAddNotes(reply, words.Count);
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            return new AnkiAddNotesOutcome(0, 0, "Anki did not respond in time. Is Anki running with the AnkiConnect add-on?");
        }
        catch (HttpRequestException)
        {
            return new AnkiAddNotesOutcome(0, 0,
                "Could not reach Anki. Make sure Anki is running and the AnkiConnect add-on (code 2055492159) is installed.");
        }
    }

    private static string? GetPreparationError(string reply, bool requireVersion = false, bool requireDeckId = false, bool requireModel = false)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(reply);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("error", out JsonElement error) ||
                !root.TryGetProperty("result", out JsonElement result))
                return "Unexpected response from AnkiConnect.";
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            if (error.ValueKind != JsonValueKind.Null)
                return "Unexpected response from AnkiConnect.";
            if (requireVersion && (!result.TryGetInt32(out int version) || version < 6))
                return "AnkiConnect version 6 or newer is required.";
            if (requireDeckId && (result.ValueKind != JsonValueKind.Number || !result.TryGetInt64(out long deckId) || deckId <= 0))
                return "Unexpected response from AnkiConnect.";
            if (requireModel && result.ValueKind != JsonValueKind.Object)
                return "Unexpected response from AnkiConnect.";
            return null;
        }
        catch (JsonException) { return "Unexpected response from AnkiConnect."; }
        catch (InvalidOperationException) { return "Unexpected response from AnkiConnect."; }
    }

    private async Task<string> PostAsync(string json, CancellationToken token)
    {
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage resp = await http.PostAsync(AnkiConnectRequests.DefaultUrl, content, token);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(token);
    }
}
