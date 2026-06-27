using System;
using System.Collections.Generic;
using System.Net.Http;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Resolves which configured LLM (if any) should drive AI insights, from the live subtitle config. The pure
/// preference logic lives in <see cref="AiInsightServiceSelector"/>; this adds the (untestable) "is it
/// configured" probe via the internal HttpClient factory, so it lives in FlyleafLib where that API is reachable.
/// </summary>
public static class AiInsightLlmResolver
{
    /// <summary>Returns the LLM settings to drive AI insights with, or null when no usable LLM is configured.</summary>
    public static OpenAIBaseTranslateSettings? Resolve(Config.SubtitlesConfig subs)
    {
        List<TranslateServiceType> usable = [];
        foreach (TranslateServiceType t in Enum.GetValues<TranslateServiceType>())
        {
            if (t.IsLLM() && IsConfigured(GetSettings(subs, t)))
                usable.Add(t);
        }

        TranslateServiceType? chosen = AiInsightServiceSelector.Select(
            subs.TranslateServiceType, subs.TranslateWordServiceType, usable);

        return chosen is null ? null : GetSettings(subs, chosen.Value);
    }

    private static OpenAIBaseTranslateSettings GetSettings(Config.SubtitlesConfig subs, TranslateServiceType t) =>
        (OpenAIBaseTranslateSettings)subs.TranslateServiceSettings.GetValueOrDefault(t, (ITranslateSettings)t.DefaultSettings());

    // "Configured" = the client factory does not reject it for a missing endpoint/model/API key, and the
    // endpoint is a parseable URI. It does NOT probe connectivity (a wrong URL / dead server surfaces only at
    // generation time, as a clear request error). A malformed-but-non-empty endpoint (e.g. "127.0.0.1:8080"
    // with no scheme) makes GetHttpClient's `new Uri(Endpoint)` throw UriFormatException; that is a
    // configuration problem, so we classify it "not configured" rather than letting it escape the probe loop
    // (which would otherwise abort resolution / block the dialog from opening). Internal so the resolver's
    // exception tolerance is unit-tested.
    internal static bool IsConfigured(OpenAIBaseTranslateSettings settings)
    {
        try
        {
            using HttpClient _ = settings.GetHttpClient();
            return true;
        }
        catch (TranslationConfigException)
        {
            return false;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }
}
