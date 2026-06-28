using System;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Pure selection of which definition provider to use for a clicked word (no HTTP), mirroring
/// <see cref="AiInsightServiceSelector"/>. Encodes the owner's "both" decision: English source words use the
/// free dictionaryapi.dev; other languages use the configured LLM. The <paramref name="llmConfigured"/> bool is
/// supplied by the caller via the existing <see cref="AiInsightLlmResolver.Resolve"/> probe (reused, not
/// reinvented). The dictionaryapi.dev fallback when an English word is missing is handled at the call site
/// (<see cref="WordDefinitionService"/>), not here.
/// </summary>
public static class WordDefinitionSelector
{
    public static WordDefinitionProvider Select(WordDefinitionServiceType configured, string? sourceIso6391, bool llmConfigured)
    {
        bool isEnglish = string.Equals(sourceIso6391, "en", StringComparison.OrdinalIgnoreCase);

        switch (configured)
        {
            case WordDefinitionServiceType.Off:
                return WordDefinitionProvider.None;

            case WordDefinitionServiceType.DictionaryApi:
                // dictionaryapi.dev is English-only.
                return isEnglish ? WordDefinitionProvider.DictionaryApi : WordDefinitionProvider.None;

            case WordDefinitionServiceType.Llm:
                return llmConfigured ? WordDefinitionProvider.Llm : WordDefinitionProvider.None;

            case WordDefinitionServiceType.Auto:
                if (isEnglish)
                    return WordDefinitionProvider.DictionaryApi;
                return llmConfigured ? WordDefinitionProvider.Llm : WordDefinitionProvider.None;

            default:
                return WordDefinitionProvider.None;
        }
    }

    /// <summary>
    /// Whether the dictionary path should fall back to the LLM when it returns no entry. True only for
    /// <see cref="WordDefinitionServiceType.Auto"/> on a word the dictionary handles (so
    /// <see cref="WordDefinitionProvider.DictionaryApi"/> was chosen) with an LLM available — i.e. an English
    /// word the free dictionary lacks still gets an LLM definition. Explicit <c>DictionaryApi</c>/<c>Llm</c>
    /// modes never fall back. Kept pure (no HTTP) so the routing decision is unit-testable.
    /// </summary>
    public static bool AllowLlmFallback(WordDefinitionServiceType configured, WordDefinitionProvider chosen, bool llmConfigured) =>
        configured == WordDefinitionServiceType.Auto
        && chosen == WordDefinitionProvider.DictionaryApi
        && llmConfigured;
}
