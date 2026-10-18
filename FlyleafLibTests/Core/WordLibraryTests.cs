using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.Core;

public class WordLibraryTests
{
    static SavedWord Word(string term) => new(term, "reading", "translation", "definition", "example", "en", "ru", WordSource.WordClick, "2026-10-03T00:00:00Z");
    [Fact]
    public void Atomic_candidate_failure_preserves_memory_bytes_and_event_count()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "words.json");
            var first = new WordLibrary(path); first.TryAdd(Word("first")); string bytes = File.ReadAllText(path);
            var library = new WordLibrary(path, (_, _) => throw new IOException("disk full fixture")); library.Load();
            int changed = 0; library.Changed += () => changed++;
            Assert.Throws<IOException>(() => library.TryAdd(Word("new")));
            Assert.Equal(bytes, File.ReadAllText(path)); Assert.Equal(Word("first"), Assert.Single(library.GetAll())); Assert.Equal(0, changed);
            Assert.Throws<IOException>(() => library.Remove(Word("first")));
            Assert.Equal(bytes, File.ReadAllText(path)); Assert.Single(library.GetAll()); Assert.Equal(0, changed);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void Existing_target_that_cannot_be_replaced_keeps_prior_in_memory_state()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "words.json"); var library = new WordLibrary(path); library.TryAdd(Word("first"));
            File.Delete(path); Directory.CreateDirectory(path); int events = 0; library.Changed += () => events++;
            Exception? failure = Record.Exception(() => library.Update(Word("first"), Word("first") with { Definition = "new" }));
            Assert.True(failure is IOException or UnauthorizedAccessException,
                $"Expected IOException or UnauthorizedAccessException, got {failure?.GetType().FullName ?? "no exception"}.");
            Assert.Equal("definition", Assert.Single(library.GetAll()).Definition); Assert.Equal(0, events);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[{\"Source\":\"invalid\"}]")]
    public void Corrupt_existing_bytes_are_preserved_and_mutations_blocked(string bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"); File.WriteAllText(path, bytes);
        try
        {
            var library = new WordLibrary(path); library.Load(); Assert.NotNull(library.LoadWarning);
            Assert.Throws<IOException>(() => library.TryAdd(Word("hello"))); Assert.Equal(bytes, File.ReadAllText(path)); Assert.Empty(library.GetAll());
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void Existing_array_schema_metadata_and_trimmed_first_wins_survive_roundtrip()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var library = new WordLibrary(path); library.TryAdd(Word(" Hello ")); Assert.False(library.TryAdd(Word("hello")));
            var reload = new WordLibrary(path); reload.Load(); Assert.Null(reload.LoadWarning);
            Assert.Equal(Word(" Hello "), Assert.Single(reload.GetAll())); Assert.StartsWith("[", File.ReadAllText(path));
            Assert.True(reload.Update(Word("hello"), Word(" Hello ") with { Example = "edited" }));
            Assert.Equal("edited", Assert.Single(reload.GetAll()).Example); Assert.True(reload.Remove(Word("HELLO"))); Assert.Empty(reload.GetAll());
        }
        finally { File.Delete(path); }
    }
}
