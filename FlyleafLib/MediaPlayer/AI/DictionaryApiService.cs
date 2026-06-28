using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

#nullable enable

/// <summary>
/// Thin HTTP client for the free dictionaryapi.dev (English-only) endpoint, mirroring
/// <see cref="GoogleV1TranslateService"/>'s transport (shared <see cref="TranslateHttpClient"/> pool,
/// disposeHandler:false). The only untestable lines are the GET + read; the body parse is delegated to the pure
/// <see cref="DictionaryApiParser"/>. Fail-soft: a 404 (word not in the dictionary), any non-success status, or
/// any transport error returns <see cref="DictionaryEntry.Empty"/> rather than throwing — a missing definition
/// is normal on this hot interactive path. Genuine cancellation still propagates.
/// </summary>
public sealed class DictionaryApiService : IDisposable
{
    private const string BaseAddress = "https://api.dictionaryapi.dev";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36";

    private readonly HttpClient _httpClient;

    public DictionaryApiService(int timeoutMs = WordDefinitionBudget.HttpTimeoutMs)
    {
        _httpClient = TranslateHttpClient.Create(new Uri(BaseAddress), TimeSpan.FromMilliseconds(timeoutMs));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
    }

    public async Task<DictionaryEntry> LookupAsync(string word, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(word))
            return DictionaryEntry.Empty;

        try
        {
            string url = $"/api/v2/entries/en/{Uri.EscapeDataString(word.Trim())}";

            using HttpResponseMessage result = await _httpClient.GetAsync(url, token).ConfigureAwait(false);

            // 404 = word not in the dictionary (common); any other non-success = best-effort miss.
            if (result.StatusCode == HttpStatusCode.NotFound || !result.IsSuccessStatusCode)
                return DictionaryEntry.Empty;

            string json = await result.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return DictionaryApiParser.ParseDictionaryApiDev(json);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // genuine cancellation (re-click superseded this lookup)
            throw;
        }
        catch
        {
            // timeout / DNS / transport: degrade to "no definition", never a modal on this path.
            return DictionaryEntry.Empty;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
