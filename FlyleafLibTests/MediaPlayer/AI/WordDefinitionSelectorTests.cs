using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

public class WordDefinitionSelectorTests
{
    [Theory]
    // Off -> never anything, regardless of language / LLM.
    [InlineData(WordDefinitionServiceType.Off, "en", true, WordDefinitionProvider.None)]
    [InlineData(WordDefinitionServiceType.Off, "ru", false, WordDefinitionProvider.None)]
    // DictionaryApi -> English only (case-insensitive), LLM availability irrelevant.
    [InlineData(WordDefinitionServiceType.DictionaryApi, "en", false, WordDefinitionProvider.DictionaryApi)]
    [InlineData(WordDefinitionServiceType.DictionaryApi, "EN", false, WordDefinitionProvider.DictionaryApi)]
    [InlineData(WordDefinitionServiceType.DictionaryApi, "ru", true, WordDefinitionProvider.None)]
    // Llm -> only when an LLM is configured.
    [InlineData(WordDefinitionServiceType.Llm, "en", true, WordDefinitionProvider.Llm)]
    [InlineData(WordDefinitionServiceType.Llm, "en", false, WordDefinitionProvider.None)]
    [InlineData(WordDefinitionServiceType.Llm, "ru", true, WordDefinitionProvider.Llm)]
    // Auto -> English prefers the free dictionary (even with an LLM); other languages use the LLM if available.
    [InlineData(WordDefinitionServiceType.Auto, "en", false, WordDefinitionProvider.DictionaryApi)]
    [InlineData(WordDefinitionServiceType.Auto, "en", true, WordDefinitionProvider.DictionaryApi)]
    [InlineData(WordDefinitionServiceType.Auto, "ja", true, WordDefinitionProvider.Llm)]
    [InlineData(WordDefinitionServiceType.Auto, "ja", false, WordDefinitionProvider.None)]
    [InlineData(WordDefinitionServiceType.Auto, null, true, WordDefinitionProvider.Llm)]
    [InlineData(WordDefinitionServiceType.Auto, null, false, WordDefinitionProvider.None)]
    public void Select_MatchesOwnerSemantics(
        WordDefinitionServiceType mode, string? sourceIso, bool llmConfigured, WordDefinitionProvider expected)
    {
        WordDefinitionSelector.Select(mode, sourceIso, llmConfigured).Should().Be(expected);
    }

    [Theory]
    // Fallback fires ONLY for Auto on a word the dictionary handles (DictionaryApi chosen) with an LLM present.
    [InlineData(WordDefinitionServiceType.Auto, WordDefinitionProvider.DictionaryApi, true, true)]
    [InlineData(WordDefinitionServiceType.Auto, WordDefinitionProvider.DictionaryApi, false, false)] // no LLM
    [InlineData(WordDefinitionServiceType.Auto, WordDefinitionProvider.Llm, true, false)]            // already LLM (non-EN)
    [InlineData(WordDefinitionServiceType.DictionaryApi, WordDefinitionProvider.DictionaryApi, true, false)] // explicit: no fallback
    [InlineData(WordDefinitionServiceType.Llm, WordDefinitionProvider.Llm, true, false)]
    [InlineData(WordDefinitionServiceType.Off, WordDefinitionProvider.None, true, false)]
    public void AllowLlmFallback_OnlyAutoDictionaryWithLlm(
        WordDefinitionServiceType mode, WordDefinitionProvider chosen, bool llmConfigured, bool expected)
    {
        WordDefinitionSelector.AllowLlmFallback(mode, chosen, llmConfigured).Should().Be(expected);
    }
}
