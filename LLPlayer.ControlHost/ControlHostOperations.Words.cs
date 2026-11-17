using System.Text.Json;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Services;
using LLPlayer.ControlApi;

namespace LLPlayer.ControlHost;

internal sealed partial class ControlHostOperations
{
    private void RegisterWords(string configDirectory)
    {
        var path = Path.Combine(configDirectory, "LLPlayer.WordList.json");
        var words = new ControlWordOperations(path);
        Read<ControlWordList, ControlWordPage>("words.list", "words", "Read a page of saved words at a stable revision.",
            (_, args, _) => Task.FromResult(words.List(args)));
        Read<ControlWordLookup, ControlWordLookupResult>("words.lookup", "words", "Look up a saved word by its normalized term.",
            (_, args, _) => Task.FromResult(words.Lookup(args)));
        Job<ControlWordsExport>("words.export", "words", "Export selected saved words as TSV or an Anki package.",
            (context, args, ct) => words.ExportAsync(context, args with { Path = Absolute(args.Path) }, ct));
        Job<ControlWordsSend>("anki.send", "words", "Send selected saved words through the existing AnkiConnect service.",
            words.SendAsync);
        RegisterWordMutation<ControlWordSave>("words.save", "Save a detached word in the library.", path, ControlWordOperations.SaveWord);
        RegisterWordMutation<ControlWordUpdate>("words.update", "Edit a saved word at the specified library revision.", path, ControlWordOperations.UpdateWord);
        RegisterWordMutation<ControlWordRemove>("words.remove", "Remove one saved word at the specified library revision.", path, ControlWordOperations.RemoveWord);
        RegisterWordMutation<ControlWordClear>("words.clear", "Clear the word library at the specified revision.", path, ControlWordOperations.ClearWords);
        RegisterInsights(path);
    }

    private void RegisterWordMutation<T>(string operation, string description, string path,
        Func<WordLibrary, T, CancellationToken, ControlWordsChanged> edit) where T : class =>
        Job<T>(operation, "words", description,
            (context, args, ct) => WriteWordsAsync(context, operation, args, writeToken => edit(new WordLibrary(path), args, writeToken), ct));

    private async Task<ControlJobOutcome> WriteWordsAsync<T>(ControlJobContext context, string guiOperation, T args,
        Func<CancellationToken, ControlWordsChanged> write, CancellationToken token) where T : class
    {
        JsonElement receipt = await context.RunExternalEffectAsync("word_library", async ct =>
        {
            ControlResult outcome;
            if (context.Request.Scope.SessionId.HasValue)
            {
                outcome = await broker.ExecuteAsync(context.Request with
                {
                    OperationId = guiOperation,
                    Arguments = ControlProtocol.Encode(args),
                    ExpectedRevision = null,
                }, ct);
                if (outcome.Error?.Code == "session_disconnected")
                    throw new ControlJobException("external_effect_unknown", "The word library may have changed before the window disconnected.",
                        ControlJobStatus.Interrupted);
            }
            else
            {
                try { outcome = new(Value: ControlProtocol.Encode(write(ct))); }
                catch (ControlException error) { outcome = new(Error: error.Error); }
            }
            return ControlProtocol.Encode(outcome);
        }, token);
        return FromResult(receipt.Deserialize<ControlResult>(ControlProtocol.Json)!);
    }
}
