using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Services;

public enum TranslationDiagnosticKind { Check, GetModels, Hello }
public enum TranslationDiagnosticOutcome { Success, Unsupported }
/// <summary>A detached settings snapshot owned solely by this request; it never refers to the live editor.</summary>
public sealed record TranslationDiagnosticRequest(TranslateServiceType Provider, TranslationSettings Settings, TranslationDiagnosticKind Kind);
public sealed record TranslationDiagnosticResult(TranslationDiagnosticOutcome Outcome, IReadOnlyList<string>? Models = null);
public interface ITranslationDiagnostics
{
    Task<TranslationDiagnosticResult> RunAsync(TranslationDiagnosticRequest request, CancellationToken token);
}

/// <summary>Explicit provider requests only. Response bodies and exception text never become UI statuses.</summary>
public sealed class TranslationDiagnostics : ITranslationDiagnostics
{
    public async Task<TranslationDiagnosticResult> RunAsync(TranslationDiagnosticRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!request.Settings.Providers.TryGetValue(request.Provider, out var settings) || settings is not OpenAIBaseTranslateSettings llm)
            return new(TranslationDiagnosticOutcome.Unsupported);
        if (request.Kind == TranslationDiagnosticKind.Hello)
        {
            await OpenAIBaseTranslateService.CompleteAsync(llm, [new OpenAIMessage { role = "user", content = "Hello" }], null, token);
            return new(TranslationDiagnosticOutcome.Success);
        }
        var models = await OpenAIBaseTranslateService.GetLoadedModels(llm, token);
        return new(TranslationDiagnosticOutcome.Success, request.Kind == TranslationDiagnosticKind.GetModels ? models : null);
    }
}
