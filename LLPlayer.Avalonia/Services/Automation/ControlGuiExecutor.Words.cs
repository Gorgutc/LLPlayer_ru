using System.Text.Json;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Services.Automation;

internal sealed partial class ControlGuiExecutor
{
    async Task<object?> ExecuteWordOperationAsync(ControlRequest request, CancellationToken token)
    {
        T Arguments<T>() where T : class => request.Arguments.Deserialize<T>(ControlProtocol.Json)
            ?? throw new JsonException("The command arguments must be an object.");
        WordLibrary Library() => model.WordLibrary ?? throw Unavailable("The word library has not loaded in this window.");

        token.ThrowIfCancellationRequested();
        switch (request.OperationId)
        {
            case "words.save": return ControlWordOperations.SaveWord(Library(), Arguments<ControlWordSave>(), token);
            case "words.update": return ControlWordOperations.UpdateWord(Library(), Arguments<ControlWordUpdate>(), token);
            case "words.remove": return ControlWordOperations.RemoveWord(Library(), Arguments<ControlWordRemove>(), token);
            case "words.clear": return ControlWordOperations.ClearWords(Library(), Arguments<ControlWordClear>(), token);
            case "insights.vocabulary.apply": return ControlWordOperations.MergeVocabulary(Library(), Arguments<ControlVocabularyMerge>(), token);
            case "pdic.lookup":
                if (!OperatingSystem.IsWindows())
                    throw new ControlException(new("unsupported_os", "PDIC is available only on Windows."));
                var lookup = Arguments<ControlPdicLookup>();
                if (model.PdicSender is not { } send) throw Unavailable("Configure the Windows PDIC bridge in settings.");
                try { await send(lookup.Context, lookup.WordOffset, token); }
                catch (ArgumentException error) { throw Invalid(error.Message); }
                catch (InvalidOperationException error) { throw new ControlException(new("pdic_not_configured", error.Message)); }
                catch (IOException) { throw new ControlException(new("pdic_unavailable", "The Windows PDIC bridge did not accept the lookup.")); }
                return new ControlGuiAction(true);
            default: return null;
        }
    }
}
