using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.Core.Services;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

/// <summary>One click owns both lookups and a detached save snapshot. Closing invalidates all late results.</summary>
public sealed partial class WordPopupViewModel(IWordTranslator translator, Action<string> copyToClipboard) : ObservableObject, IAsyncDisposable
{
    CancellationTokenSource? cts;
    readonly HashSet<Task> consumers = [];
    bool stopping;
    Task? shutdown;
    WordClickSnapshot? snapshot;
    DictionaryEntry entry = DictionaryEntry.Empty;
    public WordLibrary? Library { get; set; }
    public Action? OpenSettings { get; set; }
    [RelayCommand] void ConfigureTranslation() => OpenSettings?.Invoke();
    [ObservableProperty] public partial bool IsOpen { get; private set; }
    [ObservableProperty] public partial string Word { get; private set; } = "";
    [ObservableProperty] public partial string Context { get; private set; } = "";
    [ObservableProperty] public partial string? Translation { get; private set; }
    [ObservableProperty] public partial string Definition { get; private set; } = "";
    [ObservableProperty] public partial string Status { get; private set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial bool DefinitionBusy { get; private set; }
    [ObservableProperty] public partial bool IsNotConfigured { get; private set; }
    [ObservableProperty] public partial bool CanSave { get; private set; }
    [ObservableProperty] public partial double AnchorX { get; private set; }
    [ObservableProperty] public partial double AnchorY { get; private set; }
    public int Slot { get; private set; }

    public Task OpenAsync(string word, string context, int slot, double x, double y, bool translated = false, string? sourceLanguage = null, string? targetLanguage = null)
    {
        if (stopping) return Task.CompletedTask;
        var done = new TaskCompletionSource(); consumers.Add(done.Task);
        var click = translator.Capture(word, context, slot, translated);
        if (sourceLanguage != null) click = click with { SourceLanguage = sourceLanguage };
        if (targetLanguage != null) click = click with { TargetLanguage = targetLanguage };
        return RunAsync(click, x, y, done);
    }
    async Task RunAsync(WordClickSnapshot click, double x, double y, TaskCompletionSource done)
    {
        cts?.Cancel();
        CancellationTokenSource mine = cts = new();
        snapshot = null; CanSave = false; entry = DictionaryEntry.Empty;
        Word = click.Term; Context = click.Context; Slot = click.Slot; AnchorX = x; AnchorY = y;
        Translation = null; Definition = ""; IsNotConfigured = false; Status = "Translating…";
        IsBusy = !click.IsTranslated; DefinitionBusy = !click.IsTranslated; IsOpen = true;
        try
        {
            await Task.WhenAll(Translate(), Define());
            if (!Current()) return;
            snapshot = click;
            CanSave = !string.IsNullOrWhiteSpace(click.Term) && !string.IsNullOrEmpty(click.IsTranslated ? click.TargetLanguage : click.SourceLanguage);
        }
        catch (OperationCanceledException) { if (Current()) Invalidate(); }
        finally
        {
            if (ReferenceEquals(mine, cts)) cts = null;
            mine.Dispose(); consumers.Remove(done.Task); done.SetResult();
        }
        bool Current() => ReferenceEquals(mine, cts) && !mine.IsCancellationRequested && !stopping;
        async Task Translate()
        {
            WordTranslationResult result;
            if (click.IsTranslated) result = new(WordTranslationStatus.NotConfigured, null, "The clicked text is already translated.");
            else
            {
                try { result = await translator.TranslateAsync(click.Term, click.Slot, mine.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { result = new(WordTranslationStatus.Failed, null, "The lookup could not finish. Try again."); }
            }
            if (!Current()) return;
            IsBusy = false;
            Translation = result.Status == WordTranslationStatus.Translated ? result.Text : null;
            IsNotConfigured = result.Status == WordTranslationStatus.NotConfigured;
            Status = result.Status == WordTranslationStatus.Translated ? "" :
                (result.Status == WordTranslationStatus.NotConfigured ? "Translation not configured" : "Translation failed") +
                (string.IsNullOrEmpty(result.Detail) ? "" : ": " + result.Detail);
        }
        async Task Define()
        {
            DictionaryEntry found;
            try { found = click.IsTranslated ? DictionaryEntry.Empty : await translator.DefineAsync(click, mine.Token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { found = DictionaryEntry.Empty; }
            if (!Current()) return;
            entry = found; Definition = found.FlattenForDisplay(); DefinitionBusy = false;
        }
    }
    [RelayCommand]
    void Save()
    {
        if (!CanSave || snapshot is not { } click || Library == null || IsBusy || DefinitionBusy) return;
        var fields = click.IsTranslated ? (Reading: "", Definition: "") : entry.ToFields();
        var word = new SavedWord(click.Term, fields.Reading, click.IsTranslated ? "" : Translation ?? "", fields.Definition,
            click.Context, click.IsTranslated ? click.TargetLanguage : click.SourceLanguage, click.TargetLanguage,
            WordSource.WordClick, DateTime.UtcNow.ToString("O"));
        try { Status = Library.TryAdd(word) ? "Saved" : "Already saved"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = "Could not save the word: " + e.Message; }
    }
    public void Invalidate()
    {
        Close(); Translation = null; Definition = ""; Word = Context = Status = ""; IsNotConfigured = false;
    }
    public ValueTask DisposeAsync()
    {
        if (shutdown != null) return new(shutdown);
        stopping = true; Invalidate(); shutdown = Task.WhenAll(consumers.ToArray()); return new(shutdown);
    }
    [RelayCommand] public void Close()
    {
        cts?.Cancel(); snapshot = null; CanSave = false; IsBusy = DefinitionBusy = false; IsOpen = false;
    }
    [RelayCommand] void Copy() { if (!string.IsNullOrEmpty(Word)) copyToClipboard(Word); }
    [RelayCommand] void CopyTranslation() { if (!string.IsNullOrEmpty(Translation)) copyToClipboard(Translation); }
}
