using System.Text.Json;
using FlyleafLib;
using FlyleafLib.MediaPlayer.AI;

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
        LoadWarning = null;
        var candidate = new WordListStore(path);
        try
        {
            try { File.GetAttributes(path); }
            catch (FileNotFoundException) { store = candidate; Changed?.Invoke(); return; }
            catch (DirectoryNotFoundException) { store = candidate; Changed?.Invoke(); return; }
            string json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException();
            // Validate records before using the engine's tolerant, first-wins loader.
            _ = JsonSerializer.Deserialize<List<SavedWord>>(json, new JsonSerializerOptions
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }) ?? throw new JsonException();
            candidate.LoadJson(json);
            store = candidate;
            Changed?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            LoadWarning = "The word library could not be read. Its original file is preserved; saving is disabled until the file is repaired.";
        }
    }
    public bool TryAdd(SavedWord word) => Mutate(candidate => candidate.TryAdd(word));
    public bool Update(SavedWord oldWord, SavedWord newWord) => Mutate(candidate => candidate.Update(oldWord, newWord));
    public bool Remove(SavedWord word) => Mutate(candidate => candidate.Remove(word));
    public void Clear() => Mutate(candidate => { candidate.Clear(); return true; });
    bool Mutate(Func<WordListStore, bool> action)
    {
        if (LoadWarning != null) throw new IOException(LoadWarning);
        var candidate = new WordListStore(path);
        candidate.LoadJson(store.ToJson());
        if (!action(candidate)) return false;
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        write(full, candidate.ToJson());
        store = candidate;
        Changed?.Invoke();
        return true;
    }
}
