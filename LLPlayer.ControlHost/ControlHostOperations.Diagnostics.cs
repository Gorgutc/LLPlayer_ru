using LLPlayer.Avalonia.Services;
using LLPlayer.Core.Automation;

namespace LLPlayer.ControlHost;

internal sealed partial class ControlHostOperations
{
    private sealed record ListTranslationProvidersRequest;

    private void RegisterDiagnostics()
    {
        Read<ListTranslationProvidersRequest, IReadOnlyList<ControlTranslationProviderStatus>>(
            "translation.providers.list", "translation", "List the player's translation providers and available diagnostics.",
            (_, _, _) => Task.FromResult(settings.TranslationProviders()));

        Job<ControlTranslationProviderRequest>("translation.providers.check", "translation", "Check the configured endpoint explicitly.",
            async (_, args, ct) => Done(await settings.RunTranslationDiagnosticAsync(args, TranslationDiagnosticKind.Check, ct)));
        Job<ControlTranslationProviderRequest>("translation.providers.models", "translation", "Request model names from the configured provider.",
            async (_, args, ct) => Done(await settings.RunTranslationDiagnosticAsync(args, TranslationDiagnosticKind.GetModels, ct)));
        Job<ControlTranslationProviderRequest>("translation.providers.hello", "translation", "Send the configured model an explicit test message.",
            async (_, args, ct) => Done(await settings.RunTranslationDiagnosticAsync(args, TranslationDiagnosticKind.Hello, ct)));

        Job<ControlCredentialPatch>("settings.credentials.apply", "settings",
            "Atomically save a translation or online-video draft with its credential changes.",
            (context, args, _) => Task.FromResult(Done(settings.ApplyCredentialPatch(args, context.Request.ExpectedRevision))),
            settings.ValidateCredentialPatchAdmission);
    }
}
