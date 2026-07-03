using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FlyleafLib.MediaPlayer.Translation.Services;

#nullable enable

public abstract class MicrosoftTranslateServiceBase : ITranslateService
{
    #region Region Definition
    internal static Dictionary<string, string> DefaultRegions { get; } = new()
    {
        ["zh"] = "zh-Hans",
        ["fr"] = "fr",
        ["pt"] = "pt",
    };

    internal static List<LanguageRegions> Regions =>
    [
        new()
        {
            Name = "Chinese",
            ISO6391 = "zh",
            Regions =
            [
                new LanguageRegionMember { Name = "Chinese (Simplified)", Code = "zh-Hans" },
                new LanguageRegionMember { Name = "Chinese (Traditional)", Code = "zh-Hant" }
            ],
        },
        new()
        {
            Name = "French",
            ISO6391 = "fr",
            Regions =
            [
                new LanguageRegionMember { Name = "French (French)", Code = "fr" },
                new LanguageRegionMember { Name = "French (Canadian)", Code = "fr-ca" }
            ],
        },
        new()
        {
            Name = "Portuguese",
            ISO6391 = "pt",
            Regions =
            [
                new LanguageRegionMember { Name = "Portuguese (Brazil)", Code = "pt" },
                new LanguageRegionMember { Name = "Portuguese (Portugal)", Code = "pt-pt" }
            ],
        }
    ];
    #endregion

    private readonly MicrosoftTranslateSettings _settings;

    protected const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36 Edg/136.0.0.0";
    private readonly HttpClient _httpClient;

    private string? _srcLang;
    private string? _targetLang;

    private volatile Task<string>? _accessToken;
    private readonly Lock _accessTokenLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    protected MicrosoftTranslateServiceBase(MicrosoftTranslateSettings settings)
    {
        ServiceType = settings.ServiceType;

        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new TranslationConfigException(
                $"Endpoint for {ServiceType} is not configured.");
        }

        _settings = settings;
        _httpClient = TranslateHttpClient.Create(new Uri(settings.Endpoint), TimeSpan.FromMilliseconds(settings.TimeoutMs));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
    }

    public TranslateServiceType ServiceType { get; }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public void Initialize(Language src, TargetLanguage target)
    {
        (TranslateLanguage srcLang, _) = this.TryGetLanguage(src, target);

        _srcLang = ToSourceCode(srcLang.ISO6391);
        _targetLang = ToTargetCode(target);
    }

    public async Task<string> TranslateAsync(string text, CancellationToken token)
    {
        if (_srcLang == null || _targetLang == null)
        {
            throw new InvalidOperationException("must be initialized");
        }

        bool retried = false;

    RETRY_401:

        string jsonResultString = "";
        int statusCode = -1;
        Task<string>? accessTokenTask = null;

        try
        {
            accessTokenTask = GetAccessTokenTask();
            string accessToken = await accessTokenTask.WaitAsync(token).ConfigureAwait(false);

            MicrosoftTranslateRequest[] body = [new() { Text = text }];

            string jsonRequest = JsonSerializer.Serialize(body, JsonOptions);
            using StringContent content = new(jsonRequest, Encoding.UTF8, "application/json");

            string route = $"/translate?api-version=3.0&from={_srcLang}&to={_targetLang}";

            using HttpRequestMessage req = new(HttpMethod.Post, route);
            req.Headers.Add("Authorization", $"Bearer {accessToken}");
            req.Content = content;

            using var result = await _httpClient.SendAsync(req, token).ConfigureAwait(false);
            jsonResultString = await result.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (result.StatusCode == HttpStatusCode.Unauthorized && !retried)
            {
                retried = true;
                lock (_accessTokenLock)
                {
                    // recreate accessToken once
                    if (_accessToken == accessTokenTask)
                    {
                        _accessToken = null;
                    }
                }

                goto RETRY_401;
            }

            statusCode = (int)result.StatusCode;
            result.EnsureSuccessStatusCode();

            MicrosoftTranslateResponse[]? responseData = JsonSerializer.Deserialize<MicrosoftTranslateResponse[]>(jsonResultString);

            // Validate the response shape explicitly. Debug.Assert is compiled out in Release, where a
            // malformed-but-successful (200) body would otherwise NRE / IndexOutOfRange.
            if (responseData is not { Length: > 0 }
                || responseData[0].translations is not { Length: > 0 }
                || responseData[0].translations[0].text == null)
            {
                throw new TranslationException($"{ServiceType} returned an unexpected response body")
                {
                    Data =
                    {
                        ["status_code"] = statusCode.ToString(),
                        ["response"] = jsonResultString
                    }
                };
            }

            return responseData[0].translations[0].text;
        }
        // Distinguish between user cancellation and HttpClient timeout by inspecting the token, not the
        // (locale-dependent) exception message. Do not clear the cached token on a transient request
        // failure — that would invalidate an otherwise-valid token and cause token thrash under
        // concurrency; the 401 path above already clears it with a compare-and-clear guard.
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Evict a FAULTED token-acquisition task so the next call retries; do NOT clear a token that
            // was acquired successfully (a transient translate-request failure must not invalidate a
            // valid cached token). Mirrors the 401 compare-and-clear guard above.
            if (accessTokenTask is { IsCompletedSuccessfully: false })
            {
                lock (_accessTokenLock)
                {
                    if (_accessToken == accessTokenTask)
                    {
                        _accessToken = null;
                    }
                }
            }

            throw new TranslationException($"Cannot request to {ServiceType}: {ex.Message}", ex)
            {
                Data =
                {
                    ["status_code"] = statusCode.ToString(),
                    ["response"] = jsonResultString
                }
            };
        }
    }

    protected abstract Task<string> GetAccessTokenAsync(HttpClient client, CancellationToken token);

    protected static async Task<string> ReadTokenResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        var content = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"cannot get token with {response.StatusCode}: {content}");
        }

        if (string.IsNullOrEmpty(content) || content.Count('.') != 2)
        {
            throw new InvalidOperationException($"invalid token: {content}");
        }

        return content;
    }

    // ref: https://learn.microsoft.com/en-us/azure/ai-services/translator/language-support
    internal string ToSourceCode(string iso6391)
    {
        if (!DefaultRegions.TryGetValue(iso6391, out string? defaultRegion))
        {
            return iso6391 switch
            {
                "lg" => "lug", // Ganda
                "mn" => "mn-Cyrl", // Mongolian
                "ny" => "nya", // Chichewa
                "rn" => "run", // Rundi
                "sr" => "sr-Latn", // Serbian

                "no" => "nb", // Norwegian Bokmal

                _ => iso6391
            };
        }

        return _settings.Regions.GetValueOrDefault(iso6391, defaultRegion);
    }

    internal static string ToTargetCode(TargetLanguage target)
    {
        return target switch
        {
            TargetLanguage.ChineseSimplified => "zh-Hans",
            TargetLanguage.ChineseTraditional => "zh-Hant",
            TargetLanguage.French => "fr",
            TargetLanguage.FrenchCanadian => "fr-ca",
            TargetLanguage.Portuguese => "pt-pt",
            TargetLanguage.PortugueseBrazilian => "pt",

            TargetLanguage.Ganda => "lug",
            TargetLanguage.Mongolian => "mn-Cyrl",
            TargetLanguage.Chichewa => "nya",
            TargetLanguage.Rundi => "run",
            TargetLanguage.Serbian => "sr-Latn",

            _ => target.ToISO6391()
        };
    }

    private Task<string> GetAccessTokenTask()
    {
        Task<string>? accessTokenTask = _accessToken;
        if (accessTokenTask != null)
        {
            return accessTokenTask;
        }

        lock (_accessTokenLock)
        {
            // HC-28: fetch the SHARED token with CancellationToken.None so a single caller cancelling (seek /
            // track switch) cannot cancel the cached fetch and leave a canceled Task in _accessToken that then
            // fails the next translate with "A task was canceled". Each caller still bails via WaitAsync(token)
            // at the call site; the faulted-task eviction guards above remain as a backstop.
            _accessToken ??= GetAccessTokenAsync(_httpClient, CancellationToken.None);
            return _accessToken;
        }
    }

    private class MicrosoftTranslateRequest
    {
        public required string Text { get; init; }
    }

    private class MicrosoftTranslateResponse
    {
        public required Translation[] translations { get; set; }
    }

    private class Translation
    {
        public required string text { get; set; }
        public required string to { get; set; }
    }
}
