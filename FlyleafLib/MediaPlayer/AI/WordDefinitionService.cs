using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Orchestrates a single word-definition lookup, routing to the dictionaryapi.dev path or the LLM path and
/// applying the Auto fallback (English word missing from the free dictionary -> LLM when configured). Both
/// backends are injected as delegates (mirroring <see cref="AiInsightService"/>'s <c>ChatCompletion</c>) so the
/// routing/fallback logic is unit-testable with fakes; only the two production bindings in
/// <see cref="ForConfig"/> touch HTTP. Fail-soft: LLM transport/config/truncation errors degrade to
/// <see cref="DictionaryEntry.Empty"/>; genuine cancellation propagates.
/// </summary>
public sealed class WordDefinitionService : IDisposable
{
    /// <summary>Look up a word in the (English) dictionary API. Returns Empty when not found.</summary>
    public delegate Task<DictionaryEntry> DictionaryLookup(string word, CancellationToken token);

    /// <summary>Send a chat completion and return the raw reply (mirrors <see cref="AiInsightService.ChatCompletion"/>).</summary>
    public delegate Task<string> LlmComplete(IReadOnlyList<OpenAIMessage> messages, CancellationToken token);

    private readonly DictionaryLookup? _dict;
    private readonly LlmComplete? _llm;
    private readonly IDisposable? _owned;

    public WordDefinitionService(DictionaryLookup? dict, LlmComplete? llm, IDisposable? owned = null)
    {
        _dict = dict;
        _llm = llm;
        _owned = owned;
    }

    /// <summary>True when a usable LLM is configured (drives the selector + Auto fallback).</summary>
    public bool LlmAvailable => _llm != null;

    /// <summary>
    /// Production binding: dictionaryapi.dev for the dictionary path and the configured LLM (via
    /// <see cref="AiInsightLlmResolver.Resolve"/>, reused) for the LLM path. The returned service owns the
    /// dictionary HttpClient and disposes it.
    /// </summary>
    public static WordDefinitionService ForConfig(Config.SubtitlesConfig subs)
    {
        DictionaryApiService dictApi = new();

        OpenAIBaseTranslateSettings? llm = AiInsightLlmResolver.Resolve(subs);
        LlmComplete? llmDelegate = llm is null
            ? null
            : (messages, token) => OpenAIBaseTranslateService.CompleteAsync(llm, messages, WordDefinitionBudget.MaxOutputTokens, token);

        return new WordDefinitionService(dictApi.LookupAsync, llmDelegate, dictApi);
    }

    /// <summary>
    /// Fetch a definition for <paramref name="word"/> via <paramref name="provider"/>. When
    /// <paramref name="allowLlmFallback"/> is set and the dictionary path returns nothing, retry via the LLM
    /// (used by Auto for English words the free dictionary lacks). Returns <see cref="DictionaryEntry.Empty"/>
    /// when nothing is found or no provider is usable.
    /// </summary>
    public async Task<DictionaryEntry> GetDefinitionAsync(
        WordDefinitionProvider provider,
        bool allowLlmFallback,
        string word,
        string? sourceLangName,
        string targetLangName,
        string? context,
        CancellationToken token)
    {
        if (provider == WordDefinitionProvider.DictionaryApi && _dict != null)
        {
            DictionaryEntry entry = await _dict(word, token).ConfigureAwait(false);
            if (!entry.IsEmpty)
                return entry;

            if (allowLlmFallback && _llm != null)
                return await LlmDefineAsync(word, sourceLangName, targetLangName, context, token).ConfigureAwait(false);

            return DictionaryEntry.Empty;
        }

        if (provider == WordDefinitionProvider.Llm && _llm != null)
            return await LlmDefineAsync(word, sourceLangName, targetLangName, context, token).ConfigureAwait(false);

        return DictionaryEntry.Empty;
    }

    private async Task<DictionaryEntry> LlmDefineAsync(
        string word, string? sourceLangName, string targetLangName, string? context, CancellationToken token)
    {
        try
        {
            OpenAIMessage[] messages = WordDefinitionPrompts.BuildDefinition(word, sourceLangName, targetLangName, context);
            string reply = await _llm!(messages, token).ConfigureAwait(false);
            return WordDefinitionPrompts.ParseLlmReply(reply, word);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A missing definition is never surfaced on this hot interactive path. The production LLM binding
            // (CompleteAsync) wraps its failures in TranslationException/TranslationConfigException, but catch
            // ANY non-cancellation exception (e.g. a malformed-endpoint UriFormatException from GetHttpClient's
            // new Uri(Endpoint)) so fail-soft is self-contained, mirroring DictionaryApiService's blanket catch.
            return DictionaryEntry.Empty;
        }
    }

    public void Dispose() => _owned?.Dispose();
}
