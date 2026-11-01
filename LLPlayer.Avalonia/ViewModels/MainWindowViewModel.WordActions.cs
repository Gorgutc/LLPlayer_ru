using Avalonia.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    WordMenuActionPref? lastWordSearch;
    WordSelectionSnapshot? popupSelection;
    readonly HashSet<Task> browserOperations = [];
    readonly CancellationTokenSource browserWaitCancellation = new();
    bool wordActionsStopping;

    public void BeginWordActionsShutdown()
    {
        if (wordActionsStopping) return;
        wordActionsStopping = true;
        browserWaitCancellation.Cancel();
    }

    public IReadOnlyList<WordMenuInvocation> CreateWordMenu(WordSelectionSnapshot selection)
        => disposed || wordActionsStopping ? [] : WordActions.NormalizeRows(prefs.WordMenuActions)
            .Where(row => row.IsEnabled && row.Kind != WordMenuActionKind.Unknown)
            .Select(row => new WordMenuInvocation(row, selection)).ToList();

    public IReadOnlyList<WordMenuInvocation> CreatePopupWordMenu()
        => popupSelection is { } selection && WordPopup.IsOpen
            ? CreateWordMenu(selection with { Origin = WordSelectionOrigin.Popup, Button = MouseButton.Right }) : [];

    public async Task InvokeWordMenuAsync(WordMenuInvocation invocation)
    {
        if (disposed || wordActionsStopping || !invocation.Action.IsEnabled) return;
        WordMenuActionPref action = invocation.Action;
        WordSelectionSnapshot selection = invocation.Selection;
        if (!WordActions.TryValidateRows([action], out _, out _)) return;
        if (action.Kind is WordMenuActionKind.Clipboard or WordMenuActionKind.ClipboardAll)
        {
            string text = action.Kind == WordMenuActionKind.Clipboard ? selection.Word : selection.Text;
            if (action.ToLower) text = text.ToLower(System.Globalization.CultureInfo.CurrentCulture);
            if (!string.IsNullOrWhiteSpace(text)) shell.SetClipboardText(text);
            return;
        }
        if (action.Kind != WordMenuActionKind.Search
            || !WordActions.TryExpandSearch(action.Url, selection, out Uri? uri, out _)
            || uri == null) return;
        if (WordActions.IsWordSearch(action)) lastWordSearch = action.Clone();
        bool opened;
        try { opened = await OpenBrowserSafelyAsync(uri).WaitAsync(browserWaitCancellation.Token); }
        catch (OperationCanceledException) { return; }
        if (!opened && !disposed && !wordActionsStopping) ShowToast("Search unavailable", "The browser could not open this search.", ToastVariant.Destructive);
    }

    async Task<bool> OpenBrowserSafelyAsync(Uri uri)
    {
        // Observe the backend even after our cancellable ownership wait is retired during window shutdown.
        try { return await shell.OpenBrowserAsync(uri); }
        catch (Exception) { return false; }
    }

    public void OnWordClicked(WordSelectionSnapshot selection, double x, double y)
    {
        if (disposed || wordActionsStopping || selection.Button != MouseButton.Left) return;
        if (WordClickAction is "Clipboard" or "ClipboardAll")
        {
            if (player.IsPlaying) player.Pause();
            string text = WordClickAction == "Clipboard" ? selection.Word : selection.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                shell.SetClipboardText(text);
                ShowToast("Copied", WordClickAction == "Clipboard" ? "Word copied to the clipboard." : "Cue copied to the clipboard.");
            }
            return;
        }
        if (player.IsPlaying) player.Pause();
        popupSelection = selection;
        if (prefs.WordCopyOnSelected && !string.IsNullOrWhiteSpace(selection.Word)) shell.SetClipboardText(selection.Word);
        RefreshLastWordSearch();
        if (prefs.WordLastSearchOnSelected && WordActions.MatchesModifier(prefs.WordLastSearchModifier, selection.Modifiers)
            && lastWordSearch is { } search)
            TrackBrowserOperation(InvokeWordMenuAsync(new WordMenuInvocation(search, selection)));
        _ = WordPopup.OpenAsync(selection.Word, selection.Text, selection.Slot, x, y,
            selection.IsTranslated, selection.SourceLanguage, selection.TargetLanguage);
    }

    public void TrackBrowserOperation(Task operation)
    {
        browserOperations.Add(operation);
        _ = ObserveAsync();
        async Task ObserveAsync()
        {
            try { await operation; }
            catch (Exception) { if (!disposed) ShowToast("Search unavailable", "The search could not finish.", ToastVariant.Destructive); }
            finally { browserOperations.Remove(operation); }
        }
    }
    public Task DrainBrowserOperationsAsync() => Task.WhenAll(browserOperations.ToArray());

    void RefreshLastWordSearch()
    {
        List<WordMenuActionPref> rows = WordActions.NormalizeRows(prefs.WordMenuActions);
        WordMenuActionPref? enabled = rows.FirstOrDefault(row => WordActions.IsWordSearch(row)
            && row.IsEnabled && lastWordSearch != null && row.Url == lastWordSearch.Url);
        lastWordSearch = (enabled ?? rows.FirstOrDefault(row => row.IsEnabled && WordActions.IsWordSearch(row)))?.Clone();
    }

    void CopyChangedSubtitle(string? property)
    {
        if (disposed || wordActionsStopping || !prefs.SubsAutoTextCopy) return;
        bool primary = property == nameof(IPlaybackController.PrimaryText);
        string changed = primary ? PrimaryText : SecondaryText;
        if (string.IsNullOrWhiteSpace(changed)) return;
        if (prefs.SubsAutoTextCopyTarget == SubtitleAutoCopyTarget.Primary && !primary
            || prefs.SubsAutoTextCopyTarget == SubtitleAutoCopyTarget.Secondary && primary) return;
        string text = WordActions.BuildAutoCopyText(prefs.SubsAutoTextCopyTarget, PrimaryText, SecondaryText);
        if (!string.IsNullOrWhiteSpace(text)) shell.SetClipboardText(text);
    }
}
