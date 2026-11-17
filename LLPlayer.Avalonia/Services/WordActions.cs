using Avalonia.Input;

namespace LLPlayer.Avalonia.Services;

public enum WordSelectionOrigin { Overlay, Sidebar, Popup }

/// <summary>Input provenance and subtitle metadata captured before another selection can change the popup.</summary>
public sealed record WordSelectionSnapshot(string Word, string Text, int Slot, bool IsWord, bool IsTranslated,
    string? SourceLanguage, string? TargetLanguage, WordSelectionOrigin Origin, MouseButton Button, KeyModifiers Modifiers, int WordOffset = -1);

/// <summary>A menu command owns its row snapshot as well as the immutable selected text.</summary>
public sealed record WordMenuInvocation
{
    readonly WordMenuActionPref action;
    public WordMenuInvocation(WordMenuActionPref action, WordSelectionSnapshot selection)
    {
        this.action = action.Clone();
        Selection = selection;
    }
    public WordMenuActionPref Action => action.Clone();
    public WordSelectionSnapshot Selection { get; }
}

public static class WordActionInput
{
    public static bool TryExpandSearch(string template, WordSelectionSnapshot selection, out Uri? uri, out string error)
        => WordActions.TryExpandSearch(template, selection.Word, selection.Text, out uri, out error);

    public static bool MatchesModifier(WordSearchModifier expected, KeyModifiers actual) => expected switch
    {
        WordSearchModifier.Control => actual == KeyModifiers.Control,
        WordSearchModifier.Shift => actual == KeyModifiers.Shift,
        WordSearchModifier.Alt => actual == KeyModifiers.Alt,
        WordSearchModifier.None => actual == KeyModifiers.None,
        _ => false,
    };
}
