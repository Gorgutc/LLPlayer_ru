using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// In-memory model for the global cumulative word list (F-10), persisted to a JSON file beside the exe.
/// Pure logic (dedup / merge / JSON round-trip) lives here so it is unit-testable; the WPF layer only owns
/// the file path and the DI lifetime. Dedup is by <see cref="SavedWord.DedupeKey"/> (Term, case-insensitive,
/// first-wins). Mutations do NOT auto-persist — callers batch then call <see cref="Save"/> (matching the
/// AppConfig.Save pattern). A <see cref="Changed"/> event lets an open Word Manager refresh when words are
/// added from another surface (the word-click popup or AI Insights). All members are expected to be called on
/// the UI thread; no internal locking.
/// </summary>
public sealed class WordListStore
{
    private readonly string _path;
    private readonly List<SavedWord> _words = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal); // already-lowercased DedupeKeys

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Raised after the in-memory list changes (add / remove / update / clear / load).</summary>
    public event Action? Changed;

    /// <param name="path">Absolute path of the wordlist.json file (beside the exe in production).</param>
    public WordListStore(string path)
    {
        _path = path;
    }

    public int Count => _words.Count;

    /// <summary>Snapshot of the current words in insertion order. Safe to enumerate/sort by the caller.</summary>
    public IReadOnlyList<SavedWord> GetAll() => _words.ToList();

    /// <summary>Adds a word unless its Term already exists (case-insensitive). Returns true if added.</summary>
    public bool TryAdd(SavedWord word)
    {
        if (!TryAddCore(word))
        {
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    private bool TryAddCore(SavedWord word)
    {
        SavedWord w = word.NormalizeNulls();
        if (!_seen.Add(w.DedupeKey))
        {
            return false;
        }
        _words.Add(w);
        return true;
    }

    /// <summary>
    /// Bulk-adds AI Insights vocabulary, skipping duplicates. Returns the number actually added. Raises
    /// <see cref="Changed"/> once if anything was added.
    /// </summary>
    public int MergeVocabulary(
        IReadOnlyList<VocabularyEntry> entries, string sourceLanguage, string targetLanguage, string savedAtUtc)
    {
        int added = 0;
        foreach (VocabularyEntry e in entries)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.Term))
            {
                continue;
            }
            if (TryAddCore(SavedWord.FromVocabularyEntry(e, sourceLanguage, targetLanguage, WordSource.AiInsights, savedAtUtc)))
            {
                added++;
            }
        }
        if (added > 0)
        {
            Changed?.Invoke();
        }
        return added;
    }

    /// <summary>Removes the entry matching by Term (case-insensitive). Returns true if removed.</summary>
    public bool Remove(SavedWord word)
    {
        string key = word.DedupeKey;
        int idx = _words.FindIndex(w => w.DedupeKey == key);
        if (idx < 0)
        {
            return false;
        }
        _words.RemoveAt(idx);
        _seen.Remove(key);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Replaces an existing entry (matched by the OLD term, case-insensitive) with an edited one. Returns false
    /// if the old entry is missing, or if the edit would collide with a different existing term.
    /// </summary>
    public bool Update(SavedWord oldWord, SavedWord newWord)
    {
        string oldKey = oldWord.DedupeKey;
        int idx = _words.FindIndex(w => w.DedupeKey == oldKey);
        if (idx < 0)
        {
            return false;
        }

        SavedWord updated = newWord.NormalizeNulls();
        string newKey = updated.DedupeKey;
        if (newKey != oldKey && _seen.Contains(newKey))
        {
            // Renaming onto another existing term would create a duplicate — reject.
            return false;
        }

        _words[idx] = updated;
        if (newKey != oldKey)
        {
            _seen.Remove(oldKey);
            _seen.Add(newKey);
        }
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        ClearCore();
        Changed?.Invoke();
    }

    private void ClearCore()
    {
        _words.Clear();
        _seen.Clear();
    }

    /// <summary>Serializes the current list to indented JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(_words, JsonOpts);

    /// <summary>
    /// Replaces the list from a JSON array. Tolerant: a null/blank/corrupt payload yields an empty list and
    /// never throws. Re-applies dedup (a hand-edited file with duplicate terms keeps the first occurrence).
    /// </summary>
    public void LoadJson(string? json)
    {
        ClearCore();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                List<SavedWord>? loaded = JsonSerializer.Deserialize<List<SavedWord>>(json, JsonOpts);
                if (loaded != null)
                {
                    foreach (SavedWord w in loaded)
                    {
                        if (w != null && !string.IsNullOrWhiteSpace(w.Term))
                        {
                            TryAddCore(w);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Corrupt file → start fresh rather than crash the app.
                ClearCore();
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Loads from the configured path. Missing or corrupt file → empty list (never throws).</summary>
    public void Load()
    {
        if (!File.Exists(_path))
        {
            Clear();
            return;
        }
        try
        {
            LoadJson(File.ReadAllText(_path));
        }
        catch (IOException)
        {
            Clear();
        }
        catch (UnauthorizedAccessException)
        {
            Clear();
        }
    }

    /// <summary>Writes the current list to the configured path. Best-effort: swallows IO errors.</summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(_path, ToJson(), new UTF8Encoding(false));
        }
        catch (IOException)
        {
            // disk full / file locked — best-effort, same posture as crash-log writes.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
