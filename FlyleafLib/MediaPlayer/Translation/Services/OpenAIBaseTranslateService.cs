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

    // Anti-loop retry sampling. A deterministic argmax loop (temperature 0) reproduces the SAME loop on a
    // plain retry, so the recovery retry forces a non-zero temperature plus a frequency penalty to escape it.
    private const double AntiLoopTemperature = 0.7;
    private const double AntiLoopFrequencyPenalty = 0.5;

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

        OpenAIMessage systemMessage = new() { role = "system", content = _basePrompt };
        OpenAIMessage newMessage = new() { role = "user", content = text };

        List<OpenAIMessage> messages = new(_messageQueue.Count + 2) { systemMessage };
        // add history
        messages.AddRange(_messageQueue);
        // add new message
        messages.Add(newMessage);

        // On a looping/truncated reply, retry once from a clean context (drop the possibly-poisoned history),
        // clearing the queue first. SendChatWithRecovery returns only a sane reply or throws a recoverable failure.
        // Pass the raw subtitle `text` as the source so the degeneration check is source-aware (a faithful
        // translation of a legitimately repetitive line is not mistaken for a loop).
        OpenAIMessage[] retryMessages = [systemMessage, newMessage];
        string reply = await SendChatWithRecovery(
            messages.ToArray(), retryMessages, onBeforeRetry: _messageQueue.Clear, source: text, token);

        // Anti-poisoning gate: only feed a sane (non-degenerate) reply back into the context window, so a
        // looping reply cannot prime the model to repeat the same pattern on the following subtitles.
        _messageQueue.Enqueue(newMessage);
        _messageQueue.Enqueue(new OpenAIMessage { role = "assistant", content = reply });

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

        // OneByOne has no context to poison, but the model can still loop on a given line; recover the same way.
        // Source is the raw subtitle `text` (NOT the prompt-wrapped string) so the degeneration budget is
        // measured against the actual line, not the instruction boilerplate.
        return await SendChatWithRecovery(messages, messages, onBeforeRetry: null, source: text, token);
    }

    /// <summary>
    /// Sends <paramref name="initialMessages"/>; if the reply loops (<see cref="ChatReplyParser.IsDegenerate"/>)
    /// or was cut off by the token cap (<see cref="TranslationFailureKind.Truncated"/> — both signal a runaway
    /// loop on a local model), retries ONCE with <paramref name="retryMessages"/> using anti-loop sampling
    /// (non-zero temperature + frequency penalty) so a deterministic argmax loop can actually break. Throws a
    /// recoverable <see cref="TranslationFailureKind.Degenerate"/> failure if it still loops, so the caller
    /// falls back to the source text instead of returning/caching the loop. The token cap is what keeps the
    /// first attempt from running to the HTTP timeout when the model loops.
    /// <paramref name="source"/> is the raw subtitle line being translated; it makes the degeneration check
    /// source-aware so a faithful translation of a legitimately repetitive source is not flagged as a loop.
    /// </summary>
    private async Task<string> SendChatWithRecovery(
        OpenAIMessage[] initialMessages,
        OpenAIMessage[] retryMessages,
        Action? onBeforeRetry,
        string? source,
        CancellationToken token)
    {
        try
        {
            string reply = await SendChatRequest(_httpClient, _settings, initialMessages, token);
            if (!ChatReplyParser.IsDegenerate(reply, source))
            {
                return reply;
            }
            // Completed but looping -> fall through to the anti-loop retry.
        }
        catch (TranslationException ex) when (ex.Kind == TranslationFailureKind.Truncated)
        {
            // Capped mid-loop (or a genuinely over-long reply) -> treat as a loop and retry.
        }

        onBeforeRetry?.Invoke();

        string retry;
        try
        {
            retry = await SendChatRequest(_httpClient, _settings, retryMessages, token, antiLoop: true);
        }
        catch (TranslationException ex) when (ex.Kind == TranslationFailureKind.Truncated)
        {
            throw new TranslationException(
                $"Looping or over-long reply from {_settings.ServiceType} after anti-loop retry", ex)
            {
                Kind = TranslationFailureKind.Degenerate
            };
        }

        if (ChatReplyParser.IsDegenerate(retry, source))
        {
            throw new TranslationException(
                $"Degenerate (looping) reply from {_settings.ServiceType} after retry")
            {
                Kind = TranslationFailureKind.Degenerate
            };
        }

        return retry;
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
        CancellationToken token,
        bool antiLoop = false)
    {
        string jsonResultString = string.Empty;
        int statusCode = -1;

        // Create the request payload
        OpenAIRequest request = new()
        {
            model = settings.Model,
            stream = false,
            messages = messages,

            // antiLoop (the degeneration retry) forces non-zero temperature + a frequency penalty to break a
            // deterministic argmax loop, regardless of the user's manual settings. It also clears a
            // user-pinned near-greedy top_p that would otherwise keep selection deterministic on the retry.
            temperature = antiLoop
                ? AntiLoopTemperature
                : (settings.TemperatureManual ? settings.Temperature : null),
            top_p = antiLoop ? null : (settings.TopPManual ? settings.TopP : null),
            frequency_penalty = antiLoop
                ? AntiLoopFrequencyPenalty
                : (settings.FrequencyPenaltyManual ? settings.FrequencyPenalty : null),
            presence_penalty = settings.PresencePenaltyManual ? settings.PresencePenalty : null,
            max_completion_tokens = settings.MaxCompletionTokens,
            // Bound generation so a runaway loop fails fast (finish_reason=length -> recoverable) instead of
            // running to the HTTP timeout. Only inject the fallback cap when the user set NEITHER token limit
            // (an explicit max_completion_tokens must not be silently overridden, and cloud backends keep the
            // fallback null to avoid breaking o-series max_tokens rules). The anti-loop retry gets extra
            // headroom so a legitimately long (e.g. reasoning-model) reply that truncated attempt 1 can finish.
            max_tokens = settings.MaxTokens ?? (settings.MaxCompletionTokens is null
                ? (antiLoop && settings.DefaultMaxTokensFallback is int cap ? cap * 2 : settings.DefaultMaxTokensFallback)
                : null),
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

            return ParseChatResponse(jsonResultString, settings.ServiceType, settings.ReasonStripRequired, statusCode);
        }
        // Distinguish between user cancellation and HttpClient timeout by inspecting the token,
        // not the (locale-dependent) exception message.
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // genuine cancellation
            throw;
        }
        catch (TranslationException ex)
        {
            ex.Data["status_code"] = statusCode.ToString();
            ex.Data["response"] = jsonResultString;
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

    /// <summary>
    /// Parses an OpenAI chat-completions response body into the reply text. Throws a (recoverable)
    /// <see cref="TranslationException"/> on an empty/invalid response, null content, a truncated reply
    /// (finish_reason=length), or an empty reply. Extracted from SendChatRequest so it can be unit-tested
    /// without performing an HTTP request.
    /// </summary>
    public static string ParseChatResponse(string jsonResultString, TranslateServiceType serviceType, bool reasonStripRequired, int statusCode = -1)
    {
        OpenAIResponse? chatResponse = JsonSerializer.Deserialize<OpenAIResponse>(jsonResultString);

        // Null-safe parsing: some OpenAI-compatible/reasoning endpoints return an empty choices array or
        // a null content (reasoning-only / tool-call responses). Treat these as a recoverable translation
        // failure instead of throwing NullReferenceException (which previously disabled the whole track).
        if (chatResponse?.choices is not { Length: > 0 })
        {
            throw new TranslationException($"Empty or invalid response from {serviceType}")
            {
                Kind = TranslationFailureKind.EmptyResponse,
                Data = { ["status_code"] = statusCode.ToString(), ["response"] = jsonResultString }
            };
        }

        OpenAIChoice choice = chatResponse.choices[0];
        string? rawContent = choice.message?.content;
        if (rawContent == null)
        {
            throw new TranslationException($"No content in response from {serviceType}")
            {
                Kind = TranslationFailureKind.NullContent,
                Data = { ["status_code"] = statusCode.ToString(), ["response"] = jsonResultString }
            };
        }

        string reply = reasonStripRequired
            ? ChatReplyParser.StripReasoning(rawContent).Trim().ToString()
            : rawContent.Trim();

        // The model was cut off at the token cap: the visible text is truncated (lost text) and, in
        // KeepContext mode, a half-finished reply would poison subsequent subtitles. Surface it as a
        // recoverable failure rather than silently accepting/caching the partial output.
        if (string.Equals(choice.finish_reason, "length", StringComparison.OrdinalIgnoreCase))
        {
            throw new TranslationException(
                $"Response from {serviceType} was truncated (finish_reason=length); increase max tokens")
            {
                Kind = TranslationFailureKind.Truncated,
                Data = { ["status_code"] = statusCode.ToString(), ["response"] = jsonResultString }
            };
        }

        // An empty reply (e.g. a reasoning-only response fully consumed by StripReasoning) is not a
        // usable translation; fail so the caller can retry / fall back to the source text.
        if (reply.Length == 0)
        {
            throw new TranslationException($"Empty translation from {serviceType}")
            {
                Kind = TranslationFailureKind.EmptyResponse,
                Data = { ["status_code"] = statusCode.ToString(), ["response"] = jsonResultString }
            };
        }

        return reply;
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
    /// consecutively many times, or a whole sentence/paragraph block repeated many times. Used to avoid
    /// feeding a looping reply back into the chat context (which would prime the model to keep repeating on
    /// subsequent subtitles) and, in batch, to fall back to the source text for that line. This source-blind
    /// overload uses the original conservative thresholds; prefer <see cref="IsDegenerate(string, string?)"/>
    /// when the source subtitle text is available so a faithful translation of a repetitive source is not
    /// misclassified as a loop.
    /// </summary>
    public static bool IsDegenerate(string text) => IsDegenerate(text, null);

    /// <summary>
    /// Source-aware degeneration check. A faithful translation of a legitimately repetitive source line
    /// (e.g. a shouted "quick, quick, quick, quick, quick, quick") repeats just as much as the source, so it
    /// must NOT be flagged as a loop. The reply's allowed repetition therefore scales with the source's own
    /// repetition: a calm source keeps the original conservative thresholds, while a repetitive source widens
    /// the budget, bounded by <see cref="MaxSourceBudget"/> so a pathological/looping source cannot disable
    /// detection. A null/empty <paramref name="source"/> reproduces the original source-blind thresholds exactly.
    /// </summary>
    public static bool IsDegenerate(string text, string? source)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 40)
        {
            return false;
        }

        // How much consecutive repetition the source itself contains. The reply is allowed at least
        // DefaultMaxRepeat consecutive n-grams, plus headroom over whatever the source legitimately repeats,
        // capped at MaxSourceBudget. Source null/empty -> srcNgramRepeat 0 -> allowedRepeat == DefaultMaxRepeat
        // (byte-for-byte the original threshold).
        int srcNgramRepeat = MaxConsecutiveNgramRepeat(source);
        int allowedRepeat = Math.Min(MaxSourceBudget, Math.Max(DefaultMaxRepeat, srcNgramRepeat + RepeatSourceHeadroom));

        // Short consecutive n-gram loop (n = 1..3). Only runs when a whitespace split yields enough tokens;
        // spaceless scripts (CJK/Thai) collapse to a few tokens, so for those the block scan below is the
        // detector. Do NOT early-return on a low word count — that would skip the block scan and let a
        // spaceless "X---X---X" loop through (the very bug class this guards against).
        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 8)
        {
            // More than `allowedRepeat` consecutive identical n-grams (n = 1..3) is treated as a loop.
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
                        if (repeat > allowedRepeat)
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
        }

        // Long repeated-block loop: a multi-word sentence/paragraph repeated many times, often joined by
        // separator lines like "---". The short n-gram scan above misses these (the repeat period in words
        // far exceeds 3) and is skipped entirely for spaceless scripts, so this runs regardless of word
        // count. Split into content blocks (dropping blank / separator-only / trivially short lines) and
        // flag when one block dominates the reply by repetition — again scaled by the source's own blocks so
        // a faithfully-translated repeated cue (e.g. repeated SDH lines) is not mistaken for a loop.
        List<string> blocks = SplitContentBlocks(text);
        if (blocks.Count >= 3)
        {
            List<string> srcBlocks = source is null ? [] : SplitContentBlocks(source);
            int allowedBlockRun = Math.Min(MaxSourceBudget, Math.Max(3, MaxConsecutiveBlockRun(srcBlocks) + RepeatSourceHeadroom));
            int srcTopCount = MaxBlockMultiplicity(srcBlocks);

            // `allowedBlockRun`+ consecutive identical blocks, e.g. "X\n---\nX\n---\nX" (>= 3 by default).
            int run = 1;
            for (int i = 1; i < blocks.Count; i++)
            {
                if (string.Equals(blocks[i], blocks[i - 1], StringComparison.OrdinalIgnoreCase))
                {
                    if (++run >= allowedBlockRun)
                    {
                        return true;
                    }
                }
                else
                {
                    run = 1;
                }
            }

            // A single block repeated and dominating the reply when separators break adjacency. Require the
            // dominant block to be >= 2/3 of all content blocks (and >= 3 occurrences) so an interleaved
            // refrain like "A B A B A" (3-of-5) is not mistaken for a loop, AND require it to repeat strictly
            // more than the source's own dominant block so a faithful repeated-cue translation is not flagged.
            int topCount = blocks
                .GroupBy(b => b, StringComparer.OrdinalIgnoreCase)
                .Max(g => g.Count());
            if (topCount >= 3 && topCount * 3 >= blocks.Count * 2 && topCount > srcTopCount + 1)
            {
                return true;
            }
        }

        return false;
    }

    // Conservative thresholds, deliberately set to avoid flagging legitimately repetitive subtitles.
    private const int DefaultMaxRepeat = 4;      // max consecutive identical n-grams tolerated for a calm source
    private const int RepeatSourceHeadroom = 2;  // extra repeats allowed over what the source itself repeats
    private const int MaxSourceBudget = 16;      // hard cap so a pathological/looping source cannot disable detection

    // Max number of consecutive identical word n-grams (n = 1..3) in the text, using the SAME whitespace
    // tokenization (punctuation stays attached) and case-insensitive comparison as the reply scan above, so a
    // source budget is measured on the same footing as the reply. Returns 0 for null/empty/too-short input.
    private static int MaxConsecutiveNgramRepeat(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int best = 0;
        for (int n = 1; n <= 3; n++)
        {
            if (words.Length < 2 * n)
            {
                continue;
            }

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
                    if (repeat > best)
                    {
                        best = repeat;
                    }
                }
                else
                {
                    repeat = 1;
                }
            }
        }

        return best;
    }

    // Longest run of consecutive identical content blocks; 0 when there are no content blocks.
    private static int MaxConsecutiveBlockRun(List<string> blocks)
    {
        if (blocks.Count == 0)
        {
            return 0;
        }

        int best = 1;
        int run = 1;
        for (int i = 1; i < blocks.Count; i++)
        {
            if (string.Equals(blocks[i], blocks[i - 1], StringComparison.OrdinalIgnoreCase))
            {
                if (++run > best)
                {
                    best = run;
                }
            }
            else
            {
                run = 1;
            }
        }

        return best;
    }

    // Highest multiplicity of any single content block; 0 when there are no content blocks.
    private static int MaxBlockMultiplicity(List<string> blocks)
    {
        if (blocks.Count == 0)
        {
            return 0;
        }

        return blocks
            .GroupBy(b => b, StringComparer.OrdinalIgnoreCase)
            .Max(g => g.Count());
    }

    // Splits a reply into normalized content lines, dropping blank lines, separator-only lines (---, ***,
    // ===, bullets) and trivially short fragments so they don't dilute the repeated-block ratio above.
    private static List<string> SplitContentBlocks(string text)
    {
        List<string> blocks = [];
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().Trim('-', '—', '–', '*', '=', '_', '·', '•', '#', '>', '~', ' ', '\t', '\r');
            if (line.Length < 8)
            {
                continue;
            }
            blocks.Add(line);
        }
        return blocks;
    }
}
