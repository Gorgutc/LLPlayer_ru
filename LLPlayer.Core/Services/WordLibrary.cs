using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlyleafLib;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.ControlApi;

namespace LLPlayer.Core.Services;

/// <summary>UI-thread word library: publish mutations only after the atomic write succeeds.</summary>
public sealed class WordLibrary
{
    readonly string path;
    readonly Action<string, string> write;
    WordListStore store;
    public WordLibrary(string path) : this(path, (target, json) => AtomicFile.WriteAllText(target, json)) { }
    internal WordLibrary(string path, Action<string, string> write)
    {
        this.path = path; this.write = write; store = new(path);
    }
    public string? LoadWarning { get; private set; }
    public event Action? Changed;
    public IReadOnlyList<SavedWord> GetAll() => store.GetAll();
    public void Load()
    {
        var candidate = ReadStore(path);
        if (candidate is null) return;
        store = candidate;
        Changed?.Invoke();
    }

    WordListStore? ReadStore(string source)
    {
        LoadWarning = null;
        var candidate = new WordListStore(source);
        try
        {
            FileStream input;
            try { input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
            catch (FileNotFoundException) { return candidate; }
            catch (DirectoryNotFoundException) { return candidate; }
            using var reader = new StreamReader(input);
            string json = reader.ReadToEnd();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException();
            // Validate records before using the engine's tolerant, first-wins loader.
            _ = JsonSerializer.Deserialize<List<SavedWord>>(json, new JsonSerializerOptions
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }) ?? throw new JsonException();
            candidate.LoadJson(json);
            return candidate;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            LoadWarning = "The word library could not be read. Its original file is preserved; saving is disabled until the file is repaired.";
            return null;
        }
    }
    public bool TryAdd(SavedWord word) => Mutate(candidate => candidate.TryAdd(word));
    public bool Update(SavedWord oldWord, SavedWord newWord) => Mutate(candidate => candidate.Update(oldWord, newWord));
    public bool Remove(SavedWord word) => Mutate(candidate => candidate.Remove(word));
    /// <summary>Persist an entire AI vocabulary merge before publishing any added words.</summary>
    public int MergeVocabulary(IReadOnlyList<VocabularyEntry> entries, string sourceLanguage, string targetLanguage, string savedAtUtc)
    {
        int added = 0;
        Mutate(candidate => (added = candidate.MergeVocabulary(entries, sourceLanguage, targetLanguage, savedAtUtc)) > 0);
        return added;
    }
    public void Clear() => Mutate(candidate => { candidate.Clear(); return true; });
    bool Mutate(Func<WordListStore, bool> action) => Commit(candidate =>
    {
        bool changed = action(candidate);
        return (changed, changed);
    });

    /// <summary>Serializes fresh-read, revision validation and publication across owners of the same library file.</summary>
    internal T Commit<T>(Func<WordListStore, (T Value, bool Changed)> edit)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        string identity = Path.Combine(ControlPaths.ResolveDirectory(directory), Path.GetFileName(full));
        if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        using var ownership = new Mutex(false, "LLPlayer.WordLibrary." + key,
            new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false });
        try
        {
            if (!ownership.WaitOne(TimeSpan.FromSeconds(2))) throw new IOException("The word library is busy. Try again.");
        }
        catch (AbandonedMutexException) { /* The failed owner's staged write could not replace the live file. */ }

        T value;
        bool notify;
        try
        {
            WordListStore candidate = ReadStore(full) ?? throw new IOException(LoadWarning);
            bool changed;
            (value, changed) = edit(candidate);
            if (changed) write(full, candidate.ToJson());
            notify = changed || !store.GetAll().SequenceEqual(candidate.GetAll());
            store = candidate;
        }
        finally { ownership.ReleaseMutex(); }
        if (notify) Changed?.Invoke();
        return value;
    }
}
