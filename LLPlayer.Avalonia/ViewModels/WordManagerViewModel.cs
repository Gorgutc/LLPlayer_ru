using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Avalonia.Services;
using LLPlayer.Core.Services;
using LLPlayer.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>Session editor over the durable library; export tasks use detached snapshots and drain on close.</summary>
public sealed partial class WordManagerViewModel : ObservableObject, IAsyncDisposable
{
    readonly WordLibrary library;
    readonly Func<IReadOnlyList<SavedWord>, string, CancellationToken, Task<AnkiAddNotesOutcome>> send;
    readonly Func<IReadOnlyList<SavedWord>, string, byte[]?> buildPackage;
    CancellationTokenSource? operation;
    Task? active;
    bool loading, publishing, stopping;
    public Func<string, Task<string?>>? PickExportPath { get; set; }
    public ObservableCollection<SavedWord> Words { get; } = [];
    [ObservableProperty] string filter = "";
    [ObservableProperty] SavedWord? selected;
    [ObservableProperty] string reading = "";
    [ObservableProperty] string translation = "";
    [ObservableProperty] string definition = "";
    [ObservableProperty] string example = "";
    [ObservableProperty] string deck = AppPrefs.DefaultAnkiDeck;
    [ObservableProperty] string message = "";
    [ObservableProperty] bool busy;
    public string Term => Selected?.Term ?? "";
    public string SourceText => Selected == null ? "" : Selected.Source switch { WordSource.AiInsights => "AI Insights", _ => "Word click" };
    public string SourceLanguageText => Selected?.SourceLanguage ?? "";
    public string SavedText => Selected == null ? ""
        : DateTimeOffset.TryParse(Selected.SavedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset saved)
            ? saved.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : Selected.SavedAtUtc;
    public string CountText => Words.Count == library.GetAll().Count
        ? (Words.Count == 1 ? "1 word" : $"{Words.Count} words")
        : $"{Words.Count} of {library.GetAll().Count} words";
    public bool HasSelection => Selected != null;
    public WordManagerViewModel(WordLibrary library,
        Func<IReadOnlyList<SavedWord>, string, CancellationToken, Task<AnkiAddNotesOutcome>>? send = null,
        Func<IReadOnlyList<SavedWord>, string, byte[]?>? buildPackage = null,
        string? defaultDeck = null)
    {
        this.library = library;
        this.send = send ?? new AnkiConnectSender().PushAsync;
        this.buildPackage = buildPackage ?? AnkiApkgWriter.TryBuild;
        Deck = string.IsNullOrWhiteSpace(defaultDeck) ? AppPrefs.DefaultAnkiDeck : defaultDeck;
        library.Changed += Refresh;
        Message = library.LoadWarning ?? "";
        Refresh();
    }
    partial void OnFilterChanged(string value) => Refresh();
    partial void OnSelectedChanged(SavedWord? value)
    {
        if (publishing) return;
        OnPropertyChanged(nameof(Term)); OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SourceText)); OnPropertyChanged(nameof(SourceLanguageText)); OnPropertyChanged(nameof(SavedText));
        ClearConfirming = false;
        loading = true;
        Reading = value?.Reading ?? ""; Translation = value?.Translation ?? "";
        Definition = value?.Definition ?? ""; Example = value?.Example ?? "";
        loading = false;
    }
    partial void OnReadingChanged(string value) => PersistEdit();
    partial void OnTranslationChanged(string value) => PersistEdit();
    partial void OnDefinitionChanged(string value) => PersistEdit();
    partial void OnExampleChanged(string value) => PersistEdit();
    void PersistEdit()
    {
        if (loading || stopping || Selected is not { } old) return;
        var updated = old with { Reading = Reading, Translation = Translation, Definition = Definition, Example = Example };
        publishing = true;
        try
        {
            if (!library.Update(old, updated)) { Message = "The word is no longer in the library."; return; }
            int index = Words.IndexOf(old);
            if (index >= 0) Words[index] = updated;
            Selected = updated; Message = "Saved";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Message = "Edit was not saved: " + e.Message; }
        finally { publishing = false; }
    }
    void Refresh()
    {
        if (stopping || publishing) return;
        string? key = Selected?.DedupeKey;
        Words.Clear();
        foreach (var word in library.GetAll().Where(w => Matches(w, Filter))) Words.Add(word);
        if (!publishing) Selected = Words.FirstOrDefault(w => w.DedupeKey == key);
        ClearConfirming = false;
        OnPropertyChanged(nameof(CountText));
    }
    public static bool Matches(SavedWord word, string filter) => string.IsNullOrWhiteSpace(filter) ||
        new[] { word.Term, word.Reading, word.Translation, word.Definition, word.Example }
            .Any(field => field.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));
    [RelayCommand] void Delete()
    {
        if (Selected is not { } word || stopping) return;
        ClearConfirming = false;
        try { library.Remove(word); Message = "Deleted"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Message = "Delete failed: " + e.Message; }
    }
    [ObservableProperty] bool clearConfirming;
    public string ClearButtonText => ClearConfirming ? "Confirm clear" : "Clear all words";
    partial void OnClearConfirmingChanged(bool value) => OnPropertyChanged(nameof(ClearButtonText));
    [RelayCommand] void Clear()
    {
        if (stopping) return;
        if (library.GetAll().Count == 0)
        {
            ClearConfirming = false;
            Message = "The library is empty.";
            return;
        }
        if (!ClearConfirming)
        {
            ClearConfirming = true;
            Message = "Click Confirm clear to delete every saved word; this cannot be undone.";
            return;
        }
        ClearConfirming = false;
        try { library.Clear(); Message = "Library cleared"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Message = "Clear failed: " + e.Message; }
    }
    [RelayCommand] Task ExportTsvAsync() => Start("tsv");
    [RelayCommand] Task ExportApkgAsync() => Start("apkg");
    [RelayCommand] Task SendAnkiAsync() => Start("anki");
    public Task Start(string format)
    {
        if (Busy || stopping) return Task.CompletedTask;
        ClearConfirming = false;
        var snapshot = library.GetAll().ToArray();
        if (snapshot.Length == 0) { Message = "The library is empty."; return Task.CompletedTask; }
        Busy = true; operation = new();
        return active = RunAsync(format, snapshot, Deck.Trim().Length == 0 ? AppPrefs.DefaultAnkiDeck : Deck.Trim(), operation);
    }
    async Task RunAsync(string format, SavedWord[] snapshot, string deckName, CancellationTokenSource mine)
    {
        try
        {
            if (format == "anki")
            {
                var outcome = await send(snapshot, deckName, mine.Token);
                mine.Token.ThrowIfCancellationRequested();
                Message = outcome.Error == null ? $"Anki: {outcome.Added} added, {outcome.Skipped} skipped." : "Anki failed: " + outcome.Error;
            }
            else
            {
                string? path = PickExportPath == null ? null : await PickExportPath(format);
                mine.Token.ThrowIfCancellationRequested();
                if (path == null) { Message = "Export cancelled."; return; }
                await Task.Run(() =>
                {
                    mine.Token.ThrowIfCancellationRequested();
                    if (format == "tsv") AtomicFile.WriteAllText(path, VocabularyParser.ToTsv(snapshot.Select(w => w.ToVocabularyEntry()).ToArray()));
                    else
                    {
                        byte[] bytes = buildPackage(snapshot, deckName) ?? throw new IOException("The Anki package could not be built.");
                        mine.Token.ThrowIfCancellationRequested();
                        // Stage before replacing an existing export; a failed build leaves it intact.
                        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try { File.WriteAllBytes(temp, bytes); mine.Token.ThrowIfCancellationRequested(); File.Move(temp, path, true); }
                        finally { if (File.Exists(temp)) File.Delete(temp); }
                    }
                }, mine.Token);
                mine.Token.ThrowIfCancellationRequested();
                Message = "Export saved.";
            }
        }
        catch (OperationCanceledException) { if (!stopping) Message = "Operation cancelled."; }
        catch (Exception e) { if (!stopping) Message = "Operation failed: " + e.Message; }
        finally { mine.Dispose(); operation = null; Busy = false; }
    }
    [RelayCommand] void Cancel() => operation?.Cancel();
    public async ValueTask DisposeAsync()
    {
        stopping = true; library.Changed -= Refresh; operation?.Cancel();
        if (active != null) await active;
    }
}
