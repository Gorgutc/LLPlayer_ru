using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FlyleafLib.MediaPlayer.Translation.Services;

#nullable enable

// All LLM translation use this class
// Currently only supports OpenAI compatible API
public class OpenAIBaseTranslateService : ITranslateService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIBaseTranslateSettings _settings;
    private readonly TranslateChatConfig _chatConfig;
    private readonly bool _wordMode;

    private ChatTranslateMethod TranslateMethod => _chatConfig.TranslateMethod;

    public OpenAIBaseTranslateService(OpenAIBaseTranslateSettings settings, TranslateChatConfig chatConfig, bool wordMode)
    {
        _httpClient = settings.GetHttpClient();
        _settings = settings;
        _chatConfig = chatConfig;
        _wordMode = wordMode;
    }

    private string? _basePrompt;
    private readonly ConcurrentQueue<OpenAIMessage> _messageQueue = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public TranslateServiceType ServiceType => _settings.ServiceType;

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public void Initialize(Language src, TargetLanguage target)
    {
        (TranslateLanguage srcLang, TranslateLanguage targetLang) = this.TryGetLanguage(src, target);

        // setup prompt
        string prompt = !_wordMode && TranslateMethod == ChatTranslateMethod.KeepContext
            ? _chatConfig.PromptKeepContext
            : _chatConfig.PromptOneByOne;

        string targetLangName = _chatConfig.IncludeTargetLangRegion
            ? target.DisplayName() : targetLang.Name;

        _basePrompt = prompt
            .Replace("{source_lang}", srcLang.Name)
            .Replace("{target_lang}", targetLangName);
    }

    public async Task<string> TranslateAsync(string text, CancellationToken token)
    {
        if (!_wordMode && TranslateMethod == ChatTranslateMethod.KeepContext)
        {
            return await DoKeepContext(text, token);
        }

        return await DoOneByOne(text, token);
    }

    private async Task<string> DoKeepContext(string text, CancellationToken token)
    {
        if (_basePrompt == null)
            throw new InvalidOperationException("must be initialized");

        // Trim message history if required
        while (_messageQueue.Count / 2 > _chatConfig.SubtitleContextCount)
        {
            if (_chatConfig.ContextRetainPolicy == ChatContextRetainPolicy.KeepSize)
            {
                Debug.Assert(_messageQueue.Count >= 2);

                // user
                _messageQueue.TryDequeue(out _);
                // assistant
                _messageQueue.TryDequeue(out _);
            }
            else if (_chatConfig.ContextRetainPolicy == ChatContextRetainPolicy.Reset)
            {
                // clear
                _messageQueue.Clear();
            }
        }

        List<OpenAIMessage> messages = new(_messageQueue.Count + 2)
        {
            new OpenAIMessage { role = "system", content = _basePrompt },
        };

        // add history
        messages.AddRange(_messageQueue);

        // add new message
        OpenAIMessage newMessage = new() { role = "user", content = text };
        messages.Add(newMessage);

        string reply = await SendChatRequest(
            _httpClient, _settings, messages.ToArray(), token);

        // Anti-poisoning gate: only feed a sane reply back into the context window. A degenerate
        // (looping) reply re-fed as few-shot context primes the model to repeat the same pattern on
        // the following subtitles. SendChatRequest already throws on empty/truncated replies, so by
        // here `reply` is non-empty; we still guard against degeneration before caching it.
        if (!ChatReplyParser.IsDegenerate(reply))
        {
            _messageQueue.Enqueue(newMessage);
            _messageQueue.Enqueue(new OpenAIMessage { role = "assistant", content = reply });
        }

        return reply;
    }

    private async Task<string> DoOneByOne(string text, CancellationToken token)
    {
        if (_basePrompt == null)
            throw new InvalidOperationException("must be initialized");

        string prompt = _basePrompt.Replace("{source_text}", text);

        OpenAIMessage[] messages =
        [
            new() { role = "user", content = prompt }
        ];

        return await SendChatRequest(_httpClient, _settings, messages, token);
    }

    public static async Task<string> Hello(OpenAIBaseTranslateSettings settings)
    {
        using HttpClient client = settings.GetHttpClient();

        OpenAIMessage[] messages =
        [
            new() { role = "user", content = "Hello" }
        ];

        return await SendChatRequest(client, settings, messages, CancellationToken.None);
    }

    private static async Task<string> SendChatRequest(
        HttpClient client,
        OpenAIBaseTranslateSettings settings,
        OpenAIMessage[] messages,
        CancellationToken token)
    {
        string jsonResultString = string.Empty;
        int statusCode = -1;

        // Create the request payload
        OpenAIRequest request = new()
        {
            model = settings.Model,
            stream = false,
            messages = messages,

            temperature = settings.TemperatureManual ? settings.Temperature : null,
            top_p = settings.TopPManual ? settings.TopP : null,
            frequency_penalty = settings.FrequencyPenaltyManual ? settings.FrequencyPenalty : null,
            presence_penalty = settings.PresencePenaltyManual ? settings.PresencePenalty : null,
            max_completion_tokens = settings.MaxCompletionTokens,
            max_tokens = settings.MaxTokens,
        };

        if (!settings.ModelRequired && string.IsNullOrWhiteSpace(settings.Model))
        {
            request.model = null;
        }

        try
        {
            // Convert to JSON
            string jsonContent = JsonSerializer.Serialize(request, JsonOptions);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            using var result = await client.PostAsync(settings.ChatPath, content, token);

            jsonResultString = await result.Content.ReadAsStringAsync(token);

            statusCode = (int)result.StatusCode;
            result.EnsureSuccessStatusCode();

            OpenAIResponse? chatResponse = JsonSerializer.Deserialize<OpenAIResponse>(jsonResultString);

            // Null-safe parsing: some OpenAI-compatible/reasoning endpoints return an empty choices
            // array or a null content (reasoning-only / tool-call responses). Treat these as a
            // recoverable translation failure instead of throwing NullReferenceException, which
            // previously bubbled up and disabled the whole translation track.
            if (chatResponse?.choices is not { Length: > 0 })
            {
                throw new TranslationException($"Empty or invalid response from {settings.ServiceType}")
                {
                    Data = { ["status_code"] = statusCode.ToString(), ["response"] = jsonResultString }
                };
            }

            OpenAIChoice choice = chatResponse.choices[0];
            string? rawContent = choice.message?.content;
            if (rawContent == null)
            {
                throw new TranslationException($"No content in response from {settings.ServiceType}")
                {
                    Data = { ["status_code"] = statusCode.ToString(), ["response"] = jsonResultString }
                };
            }

            string reply = settings.ReasonStripRequired
                ? ChatReplyParser.StripReasoning(rawContent).Trim().ToString()
                : rawContent.Trim();

            // The model was cut off at the token cap: the visible text is truncated (lost text) and,
            // in KeepContext mode, a half-finished reply would poison subsequent subtitles. Surface it
            // as a recoverable failure rather than silently accepting/caching the partial output.
            if (string.Equals(choice.finish_reason, "length", StringComparison.OrdinalIgnoreCase))
            {
                throw new TranslationException(
                    $"Response from {settings.ServiceType} was truncated (finish_reason=length); increase max tokens")
                {
                    Data = { ["status_code"] = statusCode.ToString(), ["response"] = jsonResultString }
                };
            }

            // An empty reply (e.g. a reasoning-only response fully consumed by StripReasoning) is not a
            // usable translation; fail so the caller can retry / fall back to the source text.
            if (reply.Length == 0)
            {
                throw new TranslationException($"Empty translation from {settings.ServiceType}")
                {
                    Data = { ["status_code"] = statusCode.ToString(), ["response"] = jsonResultString }
                };
            }

            return reply;
        }
        // Distinguish between user cancellation and HttpClient timeout by inspecting the token,
        // not the (locale-dependent) exception message.
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // genuine cancellation
            throw;
        }
        catch (Exception ex)
        {
            // timeout and other error
            throw new TranslationException($"Cannot request to {settings.ServiceType}: {ex.Message}", ex)
            {
                Data =
                {
                    ["status_code"] = statusCode.ToString(),
                    ["response"] = jsonResultString
                }
            };
        }
    }

    public static async Task<List<string>> GetLoadedModels(OpenAIBaseTranslateSettings settings)
    {
        using HttpClient client = settings.GetHttpClient(true);

        string jsonResultString = string.Empty;
        int statusCode = -1;

        // getting models
        try
        {
            using var result = await client.GetAsync("/v1/models");

            jsonResultString = await result.Content.ReadAsStringAsync();

            statusCode = (int)result.StatusCode;
            result.EnsureSuccessStatusCode();

            JsonNode? node = JsonNode.Parse(jsonResultString);
            List<string> models = node!["data"]!.AsArray()
                .Select(model => model!["id"]!.GetValue<string>())
                .Order()
                .ToList();

            return models;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"get models error: {ex.Message}", ex)
            {
                Data =
                {
                    ["status_code"] = statusCode.ToString(),
                    ["response"] = jsonResultString
                }
            };
        }
    }
}

public class OpenAIMessage
{
    public required string role { get; init; }
    public required string content { get; init; }
}

public class OpenAIRequest
{
    public string? model { get; set; }
    public required OpenAIMessage[] messages { get; init; }
    public required bool stream { get; init; }
    public double? temperature { get; set; }
    public double? top_p { get; set; }
    public double? frequency_penalty { get; set; }
    public double? presence_penalty { get; set; }
    public int? max_completion_tokens { get; set; }
    public int? max_tokens { get; set; }
}

public class OpenAIResponse
{
    public OpenAIChoice[]? choices { get; init; }
}

public class OpenAIChoice
{
    public OpenAIMessage? message { get; init; }
    public string? finish_reason { get; init; }
}

public static class ChatReplyParser
{
    // Target tag names to remove (lowercase)
    private static readonly string[] Tags = ["think", "reason", "reasoning", "thought"];

    // open/close tag strings from tag names
    private static readonly string[] OpenTags;
    private static readonly string[] CloseTags;

    static ChatReplyParser()
    {
        OpenTags = new string[Tags.Length];
        CloseTags = new string[Tags.Length];
        for (int i = 0; i < Tags.Length; i++)
        {
            OpenTags[i] = $"<{Tags[i]}>";       // e.g. "<think>"
            CloseTags[i] = $"</{Tags[i]}>";    // e.g. "</think>"
        }
    }

    /// <summary>
    /// Removes a leading reasoning tag if present and returns only the generated message portion.
    /// </summary>
    public static ReadOnlySpan<char> StripReasoning(ReadOnlySpan<char> input)
    {
        // Return immediately if it doesn't start with a tag
        if (input.Length == 0 || input[0] != '<')
        {
            return input;
        }

        for (int i = 0; i < OpenTags.Length; i++)
        {
            if (input.StartsWith(OpenTags[i], StringComparison.OrdinalIgnoreCase))
            {
                int endIdx = input.IndexOf(CloseTags[i], StringComparison.OrdinalIgnoreCase);
                if (endIdx >= 0)
                {
                    int next = endIdx + CloseTags[i].Length;
                    // Skip over any consecutive line breaks and whitespace
                    while (next < input.Length && char.IsWhiteSpace(input[next]))
                    {
                        next++;
                    }
                    return input.Slice(next);
                }

                // Open reasoning tag with no matching close tag (truncated reasoning): there is no
                // usable answer portion. Return empty so the caller treats it as a failed reply
                // instead of leaking the raw chain-of-thought as the "translation".
                return ReadOnlySpan<char>.Empty;
            }
        }

        // Return original string if no tag matched
        return input;
    }

    /// <summary>
    /// Heuristically detects a degenerate (looping) model reply: the same short word n-gram repeated
    /// consecutively many times. Used to avoid feeding a looping reply back into the chat context,
    /// which would prime the model to keep repeating on subsequent subtitles. Thresholds are
    /// deliberately conservative to avoid flagging legitimately repetitive subtitles.
    /// </summary>
    public static bool IsDegenerate(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 40)
        {
            return false;
        }

        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 8)
        {
            return false;
        }

        // More than `maxRepeat` consecutive identical n-grams (n = 1..3) is treated as a loop.
        const int maxRepeat = 4;
        for (int n = 1; n <= 3; n++)
        {
            int repeat = 1;
            for (int i = n; i + n <= words.Length; i += n)
            {
                bool same = true;
                for (int j = 0; j < n; j++)
                {
                    if (!string.Equals(words[i + j], words[i - n + j], StringComparison.OrdinalIgnoreCase))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    repeat++;
                    if (repeat > maxRepeat)
                    {
                        return true;
                    }
                }
                else
                {
                    repeat = 1;
                }
            }
        }

        return false;
    }
}
