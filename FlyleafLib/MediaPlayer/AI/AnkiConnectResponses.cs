using System;
using System.Text.Json;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>Parsed outcome of an AnkiConnect <c>addNotes</c> call.</summary>
public sealed record AnkiAddNotesOutcome(int Added, int Skipped, string? Error);

/// <summary>
/// Pure parsers for AnkiConnect (v6) JSON responses — no HttpClient, so the parsing is unit-testable.
/// AnkiConnect always replies with HTTP 200 and a body <c>{"result": ..., "error": null|"..."}</c>; a non-null
/// <c>error</c> string carries the failure reason.
/// </summary>
public static class AnkiConnectResponses
{
    /// <summary>Returns the <c>error</c> string from a response body, or null if there was no error / the body
    /// is unparseable. Tolerant: never throws.</summary>
    public static string? GetError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out JsonElement err) && err.ValueKind == JsonValueKind.String)
            {
                return err.GetString();
            }
        }
        catch (JsonException)
        {
            // Unparseable → treat as "no structured error" (caller decides).
        }
        return null;
    }

    /// <summary>True when an AnkiConnect error means the deck/model already exists (an expected, benign result).</summary>
    public static bool IsAlreadyExists(string? error) =>
        error != null && error.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses an <c>addNotes</c> reply. On a non-null <c>error</c> → <see cref="AnkiAddNotesOutcome.Error"/> set.
    /// Otherwise counts numeric ids in <c>result</c> as added; nulls (duplicates AnkiConnect skipped) as skipped.
    /// A malformed body yields a clear error rather than a false success.
    /// </summary>
    public static AnkiAddNotesOutcome ParseAddNotes(string? json, int total)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AnkiAddNotesOutcome(0, 0, "Empty response from AnkiConnect.");
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement err) && err.ValueKind == JsonValueKind.String)
            {
                return new AnkiAddNotesOutcome(0, 0, err.GetString());
            }

            int added = 0;
            if (root.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in result.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number)
                    {
                        added++;
                    }
                }
            }
            return new AnkiAddNotesOutcome(added, Math.Max(0, total - added), null);
        }
        catch (JsonException)
        {
            return new AnkiAddNotesOutcome(0, 0, "Unexpected response from AnkiConnect.");
        }
    }
}
