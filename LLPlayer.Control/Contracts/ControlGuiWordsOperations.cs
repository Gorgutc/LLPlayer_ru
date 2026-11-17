namespace LLPlayer.ControlApi;

public sealed record ControlWordInput(string Term, string Reading = "", string Translation = "", string Definition = "",
    string Example = "", string SourceLanguage = "", string TargetLanguage = "");
public sealed record ControlWordLookup(string Term, string? Revision = null);
public sealed record ControlWordLookupResult(bool Found, ControlWordInput? Word, string Revision);
public sealed record ControlWordSave(ControlWordInput Word, string? Revision = null);
public sealed record ControlWordUpdate(string OldTerm, ControlWordInput Word, string Revision);
public sealed record ControlWordRemove(string Term, string Revision);
public sealed record ControlWordClear(string Revision);
public sealed record ControlWordsChanged(bool Changed, string Revision, int Affected);
public sealed record ControlPdicLookup(string Context, int WordOffset);
public sealed record ControlVocabularyItem(string Term, string Reading, string Translation, string Definition, string Example);
public sealed record ControlVocabularyMerge(IReadOnlyList<ControlVocabularyItem> Vocabulary, string SourceLanguage,
    string TargetLanguage, string Revision);

internal static class ControlGuiWordsOperations
{
    public static IReadOnlyList<ControlGuiOperationSchema> All { get; } =
    [
        Define<ControlWordSave, ControlWordsChanged>("words.save", "words", "Save a detached word through the current library."),
        Define<ControlWordUpdate, ControlWordsChanged>("words.update", "words", "Edit a saved word at the specified library revision."),
        Define<ControlWordRemove, ControlWordsChanged>("words.remove", "words", "Remove one saved word at the specified library revision."),
        Define<ControlWordClear, ControlWordsChanged>("words.clear", "words", "Clear the word library at the specified revision."),
        Define<ControlPdicLookup, ControlGuiAction>("pdic.lookup", "pdic", "Send the selected word and context to the configured Windows PDIC bridge.", true),
    ];

    private static ControlGuiOperationSchema Define<TArguments, TResult>(string id, string family, string description, bool windowsOnly = false)
        => new(new(id, family, description, true, Context: ControlOperationContext.Gui, WindowsOnly: windowsOnly),
            typeof(TArguments), typeof(TResult));
}
