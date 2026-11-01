using System.Text.Json;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class SettingsViewModel
{
    sealed record ToolsBaseline(string ClickAction, string Deck, string Menu, bool CopyOnSelected,
        bool SearchOnSelected, WordSearchModifier Modifier, bool AutoCopy, SubtitleAutoCopyTarget Target);
    ToolsBaseline initialTools = null!;

    void CaptureToolsBaseline(AppPrefs current) => initialTools = new(current.WordClickAction, current.AnkiDeck,
        SerializeMenu(WordActions.NormalizeRows(current.WordMenuActions)), current.WordCopyOnSelected,
        current.WordLastSearchOnSelected, current.WordLastSearchModifier, current.SubsAutoTextCopy, current.SubsAutoTextCopyTarget);

    static string SerializeMenu(IEnumerable<WordMenuActionPref> rows) => JsonSerializer.Serialize(rows, PrefsStore.JsonOptions);

    /// <summary>Unchanged Tools drafts do not overwrite fresh Word/Anki values during a hotkey-only Save.</summary>
    internal void MergeToolsEditsInto(AppPrefs candidate)
    {
        candidate.WordMenuActions = WordActions.NormalizeRows(candidate.WordMenuActions);
        if (WordClickAction != initialTools.ClickAction) candidate.WordClickAction = WordClickAction;
        if (AnkiDeck != initialTools.Deck) candidate.AnkiDeck = AnkiDeck;
        if (WordMenuResetRequested || SerializeMenu(ValidatedWordMenuActions) != initialTools.Menu)
            candidate.WordMenuActions = ValidatedWordMenuActions.Select(row => row.Clone()).ToList();
        if (WordCopyOnSelected != initialTools.CopyOnSelected) candidate.WordCopyOnSelected = WordCopyOnSelected;
        if (WordLastSearchOnSelected != initialTools.SearchOnSelected) candidate.WordLastSearchOnSelected = WordLastSearchOnSelected;
        if (WordLastSearchModifier != initialTools.Modifier) candidate.WordLastSearchModifier = WordLastSearchModifier;
        if (SubsAutoTextCopy != initialTools.AutoCopy) candidate.SubsAutoTextCopy = SubsAutoTextCopy;
        if (SubsAutoTextCopyTarget != initialTools.Target) candidate.SubsAutoTextCopyTarget = SubsAutoTextCopyTarget;
    }
}
