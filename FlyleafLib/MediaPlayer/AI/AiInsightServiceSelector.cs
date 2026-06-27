using System.Collections.Generic;
using System.Linq;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Pure selection of which translation service to drive AI insights with (no HTTP). Preference order:
/// the configured subtitle-translation service if it is a configured-and-usable LLM, else the word-lookup
/// service under the same condition, else the first configured-and-usable LLM. Returns null when no usable LLM
/// is available — the caller then shows a known-error popup (AI insights never silently fall back to a non-LLM
/// translator, which cannot summarize). <paramref name="configuredUsableLlms"/> is the set of LLM services whose
/// settings the caller has confirmed are configured (e.g. endpoint/model/API key present).
/// </summary>
public static class AiInsightServiceSelector
{
    public static TranslateServiceType? Select(
        TranslateServiceType translate,
        TranslateServiceType word,
        IReadOnlyList<TranslateServiceType> configuredUsableLlms)
    {
        if (configuredUsableLlms == null || configuredUsableLlms.Count == 0)
            return null;

        // Prefer the user's translate service, then the word-lookup service, but only when it is an LLM that is
        // actually configured (a selected-but-unconfigured Ollama must not shadow a working OpenAI).
        if (translate.IsLLM() && configuredUsableLlms.Contains(translate))
            return translate;

        if (word.IsLLM() && configuredUsableLlms.Contains(word))
            return word;

        // Otherwise the first configured-and-usable LLM.
        foreach (TranslateServiceType t in configuredUsableLlms)
        {
            if (t.IsLLM())
                return t;
        }

        return null;
    }
}
