using FlyleafLib;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Core.Services;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public class WordLibraryUiTests
{
    sealed class Lookup : IWordTranslator
    {
        public string Source = "en", Target = "ru";
        public int Translations, Definitions;
        public Func<CancellationToken, Task<WordTranslationResult>> Translate = _ => Task.FromResult(new WordTranslationResult(WordTranslationStatus.Translated, "translation"));
        public Func<CancellationToken, Task<DictionaryEntry>> Define = _ => Task.FromResult(DictionaryEntry.Empty);
        public WordClickSnapshot Capture(string term, string context, int slot, bool translated) => new(term, context, slot, Source, Target, translated);
        public Task<WordTranslationResult> TranslateAsync(string word, int slot, CancellationToken token) { Translations++; return Translate(token); }
        public Task<DictionaryEntry> DefineAsync(WordClickSnapshot click, CancellationToken token) { Definitions++; return Define(token); }
    }
    sealed class TempLibrary : IDisposable
    {
        public string Dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        public WordLibrary Library;
        public TempLibrary() { Directory.CreateDirectory(Dir); Library = new(Path.Combine(Dir, "words.json")); Library.Load(); }
        public void Dispose() => Directory.Delete(Dir, true);
    }
    static SavedWord Word(string term) => new(term, "reading", "translation", "definition", "example", "en", "ru", WordSource.WordClick, "date");
    [Fact]
    public async Task Both_lookups_parallel_save_waits_for_both_and_captures_metadata_on_click()
    {
        using var temp = new TempLibrary();
        var translation = new TaskCompletionSource<WordTranslationResult>(); var definition = new TaskCompletionSource<DictionaryEntry>();
        var lookup = new Lookup { Translate = _ => translation.Task, Define = _ => definition.Task };
        await using var popup = new WordPopupViewModel(lookup, _ => { }) { Library = temp.Library };
        Task pending = popup.OpenAsync("word", "original context", 1, 0, 0);
        Assert.Equal(1, lookup.Translations); Assert.Equal(1, lookup.Definitions); Assert.False(popup.CanSave);
        popup.SaveCommand.Execute(null); Assert.Empty(temp.Library.GetAll());
        translation.SetResult(new(WordTranslationStatus.Translated, "translated"));
        await Task.Yield(); Assert.Equal("translated", popup.Translation); Assert.False(popup.CanSave); Assert.True(popup.DefinitionBusy);
        lookup.Source = "ja"; lookup.Target = "de";
        definition.SetResult(new("word", "phonetic", [new("noun", "meaning", "usage")])); await pending;
        Assert.True(popup.CanSave); popup.SaveCommand.Execute(null);
        var saved = Assert.Single(temp.Library.GetAll()); Assert.Equal("en", saved.SourceLanguage); Assert.Equal("ru", saved.TargetLanguage);
        Assert.Equal("original context", saved.Example); Assert.Equal("phonetic", saved.Reading); Assert.Contains("meaning", saved.Definition);
    }
    [Fact]
    public async Task Translated_text_skips_all_providers_and_saves_target_term_with_blank_lookup_fields()
    {
        using var temp = new TempLibrary(); var lookup = new Lookup();
        await using var popup = new WordPopupViewModel(lookup, _ => { }) { Library = temp.Library };
        await popup.OpenAsync("слово", "контекст", 0, 0, 0, true);
        Assert.Equal(0, lookup.Translations); Assert.Equal(0, lookup.Definitions); popup.SaveCommand.Execute(null);
        var saved = Assert.Single(temp.Library.GetAll()); Assert.Equal("ru", saved.SourceLanguage); Assert.Equal("ru", saved.TargetLanguage);
        Assert.Equal("", saved.Translation); Assert.Equal("", saved.Reading); Assert.Equal("", saved.Definition);
    }
    [Fact]
    public async Task Definition_failure_does_not_hide_translation_and_invalidation_rejects_late_save()
    {
        using var temp = new TempLibrary(); var lookup = new Lookup { Define = _ => Task.FromException<DictionaryEntry>(new IOException("offline")) };
        await using var popup = new WordPopupViewModel(lookup, _ => { }) { Library = temp.Library };
        await popup.OpenAsync("word", "", 0, 0, 0); Assert.Equal("translation", popup.Translation); Assert.True(popup.CanSave);
        var held = new TaskCompletionSource<DictionaryEntry>(); lookup.Define = _ => held.Task;
        Task active = popup.OpenAsync("late", "", 0, 0, 0); popup.Invalidate();
        held.SetResult(new("late", "", [new("", "stale", "")])); await active;
        Assert.False(popup.CanSave); Assert.Equal("", popup.Definition); popup.SaveCommand.Execute(null); Assert.Empty(temp.Library.GetAll());
    }
    [Fact]
    public async Task Same_language_source_defines_empty_results_cache_and_cancelled_results_do_not_cache()
    {
        var held = new TaskCompletionSource<DictionaryEntry>(); int calls = 0;
        var settings = new TranslationSettings { TargetLanguage = TargetLanguage.EnglishAmerican, DictionaryMode = WordDefinitionServiceType.Auto };
        await using var translator = new FlyleafWordTranslator(_ => Language.English, settings, (_, _) => throw new Exception("translation must skip"),
            _ => new WordDefinitionService((_, _) => { calls++; return calls == 1 ? held.Task : Task.FromResult(DictionaryEntry.Empty); }, null));
        var click = translator.Capture("word", "context", 0, false);
        Assert.Equal(WordTranslationStatus.NotConfigured, (await translator.TranslateAsync("word", 0, CancellationToken.None)).Status);
        using var cancel = new CancellationTokenSource(); Task first = translator.DefineAsync(click, cancel.Token); cancel.Cancel(); held.SetResult(new("word", "", [new("", "late", "")]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.True((await translator.DefineAsync(click, CancellationToken.None)).IsEmpty);
        Assert.True((await translator.DefineAsync(click, CancellationToken.None)).IsEmpty); Assert.Equal(2, calls);
    }
    sealed class Owned : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public async Task Default_off_never_constructs_definition_provider_and_disposal_drains_before_owned_service()
    {
        int creates = 0;
        await using (var off = new FlyleafWordTranslator(_ => Language.English, new(), (_, _) => throw new Exception(),
            _ => { creates++; throw new Exception("Off must not create"); }))
        {
            Assert.True((await off.DefineAsync(off.Capture("word", "context", 0, false), CancellationToken.None)).IsEmpty);
            Assert.Equal(0, creates);
        }
        var held = new TaskCompletionSource<DictionaryEntry>(); var owned = new Owned(); CancellationToken captured = default;
        var translator = new FlyleafWordTranslator(_ => Language.English, new() { DictionaryMode = WordDefinitionServiceType.Auto },
            (_, _) => throw new Exception(), _ => new WordDefinitionService((_, token) => { captured = token; return held.Task; }, null, owned));
        Task active = translator.DefineAsync(translator.Capture("word", "context", 0, false), CancellationToken.None);
        Task drain = translator.DisposeAsync().AsTask(); Assert.False(drain.IsCompleted); Assert.False(owned.Disposed);
        Assert.True(captured.IsCancellationRequested); using var registration = captured.Register(() => { });
        held.SetResult(new("word", "", [new("", "late", "")]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active); await drain; Assert.True(owned.Disposed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => translator.DefineAsync(new("word", "context", 0, "en", "ru", false), CancellationToken.None));
    }
    [Fact]
    public async Task Manager_filters_five_fields_edits_durably_and_reports_export_failure()
    {
        using var temp = new TempLibrary(); temp.Library.TryAdd(Word("term"));
        await using var vm = new WordManagerViewModel(temp.Library, (_, _, _) => Task.FromResult(new AnkiAddNotesOutcome(0, 0, "refused")), (_, _) => null);
        foreach (string filter in new[] { "term", "reading", "translation", "definition", "example" }) { vm.Filter = filter; Assert.Single(vm.Words); }
        vm.Filter = ""; vm.Selected = vm.Words[0]; vm.Definition = "edited";
        Assert.Equal("edited", Assert.Single(temp.Library.GetAll()).Definition); Assert.Equal("term", vm.Term);
        var reload = new WordLibrary(Path.Combine(temp.Dir, "words.json")); reload.Load(); Assert.Equal("edited", Assert.Single(reload.GetAll()).Definition);
        string output = Path.Combine(temp.Dir, "deck.apkg"); File.WriteAllText(output, "preserve"); vm.PickExportPath = _ => Task.FromResult<string?>(output);
        await vm.Start("apkg"); Assert.Contains("failed", vm.Message); Assert.Equal("preserve", File.ReadAllText(output));
        await vm.Start("anki"); Assert.Contains("refused", vm.Message);
    }
    [Fact]
    public async Task Manager_close_cancels_and_drains_uncooperative_sender()
    {
        using var temp = new TempLibrary(); temp.Library.TryAdd(Word("term"));
        var held = new TaskCompletionSource<AnkiAddNotesOutcome>(); CancellationToken captured = default;
        var vm = new WordManagerViewModel(temp.Library, (_, _, token) => { captured = token; return held.Task; });
        Task send = vm.Start("anki"); Task close = vm.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); Assert.True(captured.IsCancellationRequested);
        held.SetResult(new(1, 0, null)); await send; await close; Assert.DoesNotContain("added", vm.Message);
    }
}
