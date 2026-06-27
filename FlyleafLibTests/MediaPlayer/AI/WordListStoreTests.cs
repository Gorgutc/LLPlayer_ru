using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

public class WordListStoreTests
{
    private static SavedWord Word(string term, string translation = "x") =>
        new(term, "", translation, "", "", "de", "en", WordSource.WordClick, "2026-06-27T10:00:00Z");

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"llplayer-wordlist-{Guid.NewGuid():N}.json");

    [Fact]
    public void TryAdd_NewWord_ReturnsTrue_AndAppears()
    {
        WordListStore store = new(TempPath());

        store.TryAdd(Word("Hund")).Should().BeTrue();

        store.Count.Should().Be(1);
        store.GetAll().Single().Term.Should().Be("Hund");
    }

    [Fact]
    public void TryAdd_DuplicateTerm_CaseInsensitive_ReturnsFalse()
    {
        WordListStore store = new(TempPath());
        store.TryAdd(Word("Katze"));

        store.TryAdd(Word("KATZE")).Should().BeFalse();

        store.Count.Should().Be(1);
    }

    [Fact]
    public void MergeVocabulary_AddsAll_SkipsDuplicates_ReturnsAddedCount()
    {
        WordListStore store = new(TempPath());
        store.TryAdd(Word("alpha"));

        List<VocabularyEntry> entries =
        [
            new("alpha", "", "a", "", ""),   // duplicate of existing
            new("beta", "", "b", "", ""),
            new("Beta", "", "b2", "", ""),   // duplicate within batch (case-insensitive)
            new("", "", "", "", ""),         // blank term → skipped
        ];

        int added = store.MergeVocabulary(entries, "de", "en", "2026-06-27T12:00:00Z");

        added.Should().Be(1);
        store.GetAll().Select(w => w.Term).Should().BeEquivalentTo(["alpha", "beta"]);
        store.GetAll().Single(w => w.Term == "beta").Source.Should().Be(WordSource.AiInsights);
    }

    [Fact]
    public void Remove_ExistingWord_ReturnsTrue_AndAllowsReAdd()
    {
        WordListStore store = new(TempPath());
        store.TryAdd(Word("Maus"));

        store.Remove(Word("MAUS")).Should().BeTrue();
        store.Count.Should().Be(0);
        // Removing also clears the dedup index so the term can be re-added.
        store.TryAdd(Word("Maus")).Should().BeTrue();
    }

    [Fact]
    public void Remove_MissingWord_ReturnsFalse()
    {
        WordListStore store = new(TempPath());
        store.Remove(Word("ghost")).Should().BeFalse();
    }

    [Fact]
    public void Update_ExistingWord_ReplacesFields()
    {
        WordListStore store = new(TempPath());
        store.TryAdd(Word("Buch", "book"));

        store.Update(Word("Buch"), Word("Buch", "tome")).Should().BeTrue();

        store.GetAll().Single().Translation.Should().Be("tome");
    }

    [Fact]
    public void Update_RenameOntoAnotherExistingTerm_IsRejected()
    {
        WordListStore store = new(TempPath());
        store.TryAdd(Word("eins"));
        store.TryAdd(Word("zwei"));

        // Renaming "eins" → "zwei" would collide with the existing "zwei".
        store.Update(Word("eins"), Word("zwei")).Should().BeFalse();
        store.Count.Should().Be(2);
    }

    [Fact]
    public void Update_MissingOldWord_ReturnsFalse()
    {
        WordListStore store = new(TempPath());
        store.Update(Word("nope"), Word("nope2")).Should().BeFalse();
    }

    [Fact]
    public void JsonRoundTrip_PreservesAllFields()
    {
        WordListStore a = new(TempPath());
        a.TryAdd(new SavedWord("漢字", "kanji", "Chinese characters", "writing system", "日本語の漢字。",
            "ja", "en", WordSource.AiInsights, "2026-06-27T09:30:00Z"));

        WordListStore b = new(TempPath());
        b.LoadJson(a.ToJson());

        SavedWord w = b.GetAll().Single();
        w.Term.Should().Be("漢字");
        w.Reading.Should().Be("kanji");
        w.Translation.Should().Be("Chinese characters");
        w.Definition.Should().Be("writing system");
        w.Example.Should().Be("日本語の漢字。");
        w.SourceLanguage.Should().Be("ja");
        w.TargetLanguage.Should().Be("en");
        w.Source.Should().Be(WordSource.AiInsights);
        w.SavedAtUtc.Should().Be("2026-06-27T09:30:00Z");
    }

    [Fact]
    public void LoadJson_DeduplicatesHandEditedDuplicates()
    {
        WordListStore store = new(TempPath());
        store.LoadJson("""
        [
          {"Term":"dup","Reading":"","Translation":"first","Definition":"","Example":"","SourceLanguage":"de","TargetLanguage":"en","Source":"WordClick","SavedAtUtc":"t"},
          {"Term":"DUP","Reading":"","Translation":"second","Definition":"","Example":"","SourceLanguage":"de","TargetLanguage":"en","Source":"WordClick","SavedAtUtc":"t"}
        ]
        """);

        store.Count.Should().Be(1);
        store.GetAll().Single().Translation.Should().Be("first");
    }

    [Fact]
    public void LoadJson_CorruptPayload_YieldsEmpty_DoesNotThrow()
    {
        WordListStore store = new(TempPath());
        store.TryAdd(Word("pre"));

        store.LoadJson("this is not json {");

        store.Count.Should().Be(0);
    }

    [Fact]
    public void LoadJson_NullOrBlank_YieldsEmpty()
    {
        WordListStore store = new(TempPath());
        store.LoadJson(null);
        store.Count.Should().Be(0);
        store.LoadJson("   ");
        store.Count.Should().Be(0);
    }

    [Fact]
    public void Load_MissingFile_StartsEmpty()
    {
        WordListStore store = new(TempPath()); // path does not exist
        store.Load();
        store.Count.Should().Be(0);
    }

    [Fact]
    public void SaveThenLoad_FileRoundTrip()
    {
        string path = TempPath();
        try
        {
            WordListStore a = new(path);
            a.TryAdd(Word("eins", "one"));
            a.TryAdd(Word("zwei", "two"));
            a.Save();

            File.Exists(path).Should().BeTrue();

            WordListStore b = new(path);
            b.Load();
            b.GetAll().Select(w => w.Term).Should().BeEquivalentTo(["eins", "zwei"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
