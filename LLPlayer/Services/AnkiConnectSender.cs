using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
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
            await PostAsync(AnkiConnectRequests.BuildVersion(), token);

            await PostAsync(AnkiConnectRequests.BuildCreateDeck(deck), token);

            // createModel reports "Model name already exists" on every run after the first — that is expected and
            // ignored; any OTHER error means the note type could not be set up, so surface it instead of pushing
            // notes against a missing/mismatched model.
            string createModelReply = await PostAsync(AnkiConnectRequests.BuildCreateModel(), token);
            string? modelError = AnkiConnectResponses.GetError(createModelReply);
            if (modelError != null && !AnkiConnectResponses.IsAlreadyExists(modelError))
            {
                return new AnkiAddNotesOutcome(0, 0, $"AnkiConnect could not create the note type: {modelError}");
            }

            string reply = await PostAsync(AnkiConnectRequests.BuildAddNotes(words, deck), token);
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

    private static async Task<string> PostAsync(string json, CancellationToken token)
    {
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage resp = await Http.PostAsync(AnkiConnectRequests.DefaultUrl, content, token);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(token);
    }
}
