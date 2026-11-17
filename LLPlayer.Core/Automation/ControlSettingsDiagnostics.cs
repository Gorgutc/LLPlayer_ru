using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Core.Automation;

public sealed record ControlTranslationProviderRequest(TranslateServiceType Provider);
public sealed record ControlTranslationProviderStatus(TranslateServiceType Provider, bool Configured, bool SupportsDiagnostics);

public sealed partial class ControlSettingsService
{
    public IReadOnlyList<ControlTranslationProviderStatus> TranslationProviders()
    {
        var current = CaptureTranslation();
        return Enum.GetValues<TranslateServiceType>().Select(provider => new ControlTranslationProviderStatus(provider,
            current.Providers.ContainsKey(provider), provider.DefaultSettings() is OpenAIBaseTranslateSettings)).ToArray();
    }

    public async Task<TranslationDiagnosticResult> RunTranslationDiagnosticAsync(ControlTranslationProviderRequest request,
        TranslationDiagnosticKind kind, CancellationToken token)
    {
        if (!Enum.IsDefined(request.Provider)) throw Error("invalid_provider", "Choose a known translation provider.");
        var settings = CaptureTranslation();
        if (!settings.Providers.ContainsKey(request.Provider) && request.Provider.DefaultSettings() is OpenAIBaseTranslateSettings defaults)
            settings.Providers.Add(request.Provider, defaults);

        try
        {
            var result = await new TranslationDiagnostics().RunAsync(new(request.Provider, settings, kind), token).ConfigureAwait(false);
            if (result.Outcome == TranslationDiagnosticOutcome.Unsupported)
                throw Error("unsupported_provider_diagnostic", "The selected provider does not implement this diagnostic.");

            // Never send a configured credential back even if a diagnostic endpoint echoes it in the models payload.
            string[] secrets = settings.Providers.Values.Select(GetProviderKey).OfType<string>().Where(key => key.Length > 0).ToArray();
            return result.Models is null ? result : result with
            {
                Models = result.Models.Where(model => !secrets.Any(key => model.Contains(key, StringComparison.Ordinal))).ToArray()
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException or System.Text.Json.JsonException
            or TranslationConfigException or TranslationException)
        {
            throw Error("provider_diagnostic_failed", "The configured provider did not complete the requested diagnostic.", retryable: true);
        }
    }

    static string? GetProviderKey(ITranslateSettings settings) => settings switch
    {
        AzureTranslateSettings value => value.ApiKey,
        DeepLTranslateSettings value => value.ApiKey,
        OpenAITranslateSettings value => value.ApiKey,
        OpenAILikeTranslateSettings value => value.ApiKey,
        ClaudeTranslateSettings value => value.ApiKey,
        _ => null
    };
}
