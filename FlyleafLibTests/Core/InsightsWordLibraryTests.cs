using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Core.Services;

namespace FlyleafLibTests.Core;

public class InsightsWordLibraryTests
{
    [Fact]
    public void Bulk_merge_is_one_atomic_publish_and_failure_preserves_prior_library()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var library = new WordLibrary(path); int events = 0; library.Changed += () => events++;
            VocabularyEntry[] entries = [new(" word ", "a", "b", "c", "d"), new("WORD", "later", "", "", ""), new("second", "", "", "", "")];
            Assert.Equal(2, library.MergeVocabulary(entries, "en", "ru", "captured")); Assert.Equal(1, events);
            Assert.Equal(0, library.MergeVocabulary(entries, "ja", "de", "later")); Assert.Equal(1, events);
            string original = File.ReadAllText(path);
            var broken = new WordLibrary(path, (_, _) => throw new IOException("full")); broken.Load();
            Assert.Throws<IOException>(() => broken.MergeVocabulary([new("third", "", "", "", "")], "en", "ru", "date"));
            Assert.Equal(2, broken.GetAll().Count); Assert.Equal(original, File.ReadAllText(path));
            Assert.All(broken.GetAll(), word => { Assert.Equal("captured", word.SavedAtUtc); Assert.Equal("en", word.SourceLanguage); Assert.Equal(WordSource.AiInsights, word.Source); });
        }
        finally { File.Delete(path); }
    }
}
