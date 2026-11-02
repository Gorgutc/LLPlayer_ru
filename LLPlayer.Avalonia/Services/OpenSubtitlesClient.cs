using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LLPlayer.Avalonia.Services;

/// <summary>Dialog-owned credentials, never persisted or included in diagnostic strings.</summary>
public sealed class OpenSubtitlesCredentials(string apiKey, string username, string password)
{
    public string ApiKey { get; } = apiKey;
    public string Username { get; } = username;
    public string Password { get; } = password;
    internal bool Matches(OpenSubtitlesCredentials? other) => other != null
        && ApiKey == other.ApiKey && Username == other.Username && Password == other.Password;
    public override string ToString() => "OpenSubtitles session credentials";
}

public sealed record OpenSubtitlesResult(int FileId, string FileName, string Language, string Release,
    int DownloadCount, double Rating, int? Year = null, string Duration = "Unavailable")
{
    public string Format => Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
    public string YearText => Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unavailable";
    public string LanguageName => FlyleafLib.Language.Get(Language) is { } language && language != FlyleafLib.Language.Unknown
        ? language.TopEnglishName : Language;
}
public sealed record OpenSubtitlesSearchPage(IReadOnlyList<OpenSubtitlesResult> Results, int Page, int TotalPages);
public sealed record OpenSubtitlesDownload(string FileName, byte[] Utf8Bytes);

public interface IOpenSubtitlesClient : IDisposable
{
    Task<OpenSubtitlesSearchPage> SearchAsync(OpenSubtitlesCredentials credentials, string query, string language = "",
        int page = 1, CancellationToken cancellationToken = default);
    Task<OpenSubtitlesDownload> DownloadAsync(OpenSubtitlesCredentials credentials, OpenSubtitlesResult result,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit OpenSubtitles.com REST operations. Construction makes no requests. Separate unauthenticated file
/// transport, trusted HTTPS redirects and bounded responses keep keys/tokens off temporary download URLs.
/// </summary>
public sealed class OpenSubtitlesClient : IOpenSubtitlesClient
{
    static readonly Uri defaultApi = new("https://api.opensubtitles.com/api/v1/");
    readonly HttpClient api, files;
    readonly bool ownsClients;
    readonly SemaphoreSlim loginGate = new(1);
    OpenSubtitlesCredentials? activeCredentials, rejectedCredentials;
    Uri apiBase = defaultApi;
    string? token;
    bool disposed;
    const int MaxJsonBytes = 2 * 1024 * 1024, MaxSubtitleBytes = 16 * 1024 * 1024;

    public OpenSubtitlesClient() : this(CreateTransport(), CreateTransport()) => ownsClients = true;
    /// <summary>Injected transports remain caller-owned; intended for deterministic HTTP fixtures.</summary>
    public OpenSubtitlesClient(HttpClient api, HttpClient files) { this.api = api; this.files = files; }
    static HttpClient CreateTransport() => new(new SocketsHttpHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<OpenSubtitlesSearchPage> SearchAsync(OpenSubtitlesCredentials credentials, string query,
        string language = "", int page = 1, CancellationToken cancellationToken = default)
    {
        query = query?.Trim() ?? "";
        language = language?.Trim().ToLowerInvariant() ?? "";
        if (query.Length is < 1 or > 512) throw new InvalidOperationException("Enter a title or release name (up to 512 characters).");
        if (language.Length != 0 && (language.Length > 16 || language.Any(c => !char.IsAsciiLetter(c) && c != '-')))
            throw new InvalidOperationException("Enter a language code such as en, ja, zh-cn or pt-br, or leave it empty.");
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        await EnsureSessionAsync(credentials, cancellationToken).ConfigureAwait(false);
        string parameters = (language.Length == 0 ? "" : "languages=" + Uri.EscapeDataString(language) + "&")
            + (page == 1 ? "" : "page=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&")
            + "query=" + Uri.EscapeDataString(query.ToLowerInvariant());
        using HttpResponseMessage response = await SendApiAsync(HttpMethod.Get, new Uri(apiBase, "subtitles?" + parameters),
            credentials, token, null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        JsonElement root = json.RootElement;
        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("OpenSubtitles returned an invalid search response.");
        List<OpenSubtitlesResult> results = [];
        foreach (JsonElement entry in data.EnumerateArray())
        {
            if (!entry.TryGetProperty("attributes", out JsonElement attrs)
                || !attrs.TryGetProperty("files", out JsonElement items) || items.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement file in items.EnumerateArray())
            {
                int id = Number(file, "file_id");
                if (id <= 0 || results.Count >= 200) continue;
                int year = attrs.TryGetProperty("feature_details", out JsonElement feature) && feature.ValueKind == JsonValueKind.Object
                    ? Number(feature, "year") : 0;
                results.Add(new(id, SafeFileName(String(file, "file_name")), String(attrs, "language"),
                    String(attrs, "release"), Number(attrs, "download_count"), Decimal(attrs, "ratings"), year > 0 ? year : null));
            }
        }
        int totalPages = Math.Max(1, Number(root, "total_pages"));
        return new(results, Math.Max(1, Number(root, "page", page)), totalPages);
    }

    public async Task<OpenSubtitlesDownload> DownloadAsync(OpenSubtitlesCredentials credentials,
        OpenSubtitlesResult result, CancellationToken cancellationToken = default)
    {
        if (result.FileId <= 0) throw new InvalidOperationException("Choose a subtitle file from the search results.");
        await EnsureSessionAsync(credentials, cancellationToken).ConfigureAwait(false);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { file_id = result.FileId });
        using HttpResponseMessage response = await SendApiAsync(HttpMethod.Post, new Uri(apiBase, "download"),
            credentials, token, body, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        if (!Uri.TryCreate(String(json.RootElement, "link"), UriKind.Absolute, out Uri? link) || !TrustedFile(link))
            throw new InvalidDataException("OpenSubtitles returned an unsupported download URL.");
        byte[] bytes = await DownloadFileAsync(link, cancellationToken).ConfigureAwait(false);
        // The current REST endpoint promises UTF-8 subtitles. Do not extract archives or guess executable/binary data.
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new InvalidDataException("The subtitle download was not valid UTF-8 text."); }
        string prefix = text.TrimStart();
        if (text.IndexOf('\0') >= 0 || prefix.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The service returned a web page or binary data instead of subtitle text.");
        string name = String(json.RootElement, "file_name");
        return new(SafeFileName(name.Length == 0 ? result.FileName : name), new UTF8Encoding(false).GetBytes(text));
    }

    async Task EnsureSessionAsync(OpenSubtitlesCredentials credentials, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrWhiteSpace(credentials.ApiKey) || credentials.ApiKey.Length > 4096
            || credentials.ApiKey.Any(c => c < 33 || c > 126) || string.IsNullOrWhiteSpace(credentials.Username)
            || credentials.Username.Length > 512 || string.IsNullOrEmpty(credentials.Password) || credentials.Password.Length > 4096)
            throw new InvalidOperationException("Enter your OpenSubtitles.com application API key, username and password. They are kept only in this dialog.");
        await loginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeCredentials?.Matches(credentials) == true && token != null) return;
            if (rejectedCredentials?.Matches(credentials) == true)
                throw new InvalidOperationException("OpenSubtitles rejected these credentials. Correct them before trying again.");
            token = null; activeCredentials = null; apiBase = defaultApi;
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { username = credentials.Username, password = credentials.Password });
            using HttpResponseMessage response = await SendApiAsync(HttpMethod.Post, new Uri(defaultApi, "login"),
                credentials, null, body, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized) rejectedCredentials = credentials;
            EnsureSuccess(response);
            using JsonDocument json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            string sessionToken = String(json.RootElement, "token");
            string host = String(json.RootElement, "base_url");
            if (sessionToken.Length == 0 || sessionToken.Any(char.IsWhiteSpace)
                || (host != "api.opensubtitles.com" && host != "vip-api.opensubtitles.com"))
                throw new InvalidDataException("OpenSubtitles returned an invalid login response.");
            apiBase = new Uri("https://" + host + "/api/v1/"); token = sessionToken; activeCredentials = credentials;
            rejectedCredentials = null;
        }
        finally { loginGate.Release(); }
    }

    async Task<HttpResponseMessage> SendApiAsync(HttpMethod method, Uri uri, OpenSubtitlesCredentials credentials,
        string? bearer, byte[]? body, CancellationToken cancellationToken)
    {
        for (int redirects = 0; ; redirects++)
        {
            using HttpRequestMessage request = new(method, uri);
            request.Headers.Add("Api-Key", credentials.ApiKey);
            request.Headers.UserAgent.ParseAdd("LLPlayer/0.3");
            request.Headers.Accept.Add(new("application/json"));
            if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (body != null) { request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new("application/json"); }
            HttpResponseMessage response = await api.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode)) return response;
            Uri? location = response.Headers.Location;
            Uri? next = location == null ? null : location.IsAbsoluteUri ? location : new Uri(uri, location);
            response.Dispose();
            if (redirects >= 3 || next == null || !TrustedApi(next))
                throw new InvalidDataException("OpenSubtitles returned an unsupported API redirect.");
            uri = next;
        }
    }

    async Task<byte[]> DownloadFileAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (int redirects = 0; ; redirects++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("LLPlayer/0.3");
            using HttpResponseMessage response = await files.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
            {
                EnsureSuccess(response);
                return await ReadBoundedAsync(response.Content, MaxSubtitleBytes, cancellationToken).ConfigureAwait(false);
            }
            Uri? location = response.Headers.Location;
            Uri? next = location == null ? null : location.IsAbsoluteUri ? location : new Uri(uri, location);
            if (redirects >= 3 || next == null || !TrustedFile(next))
                throw new InvalidDataException("OpenSubtitles returned an unsupported file redirect.");
            uri = next;
        }
    }
    static bool TrustedApi(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (uri.Host is "api.opensubtitles.com" or "vip-api.opensubtitles.com") && uri.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal);
    static bool TrustedFile(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (uri.Host == "opensubtitles.com" || uri.Host.EndsWith(".opensubtitles.com", StringComparison.OrdinalIgnoreCase));
    static bool IsRedirect(HttpStatusCode status) => (int)status is 301 or 302 or 303 or 307 or 308;
    static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string reason = (int)response.StatusCode switch
        {
            401 => "The OpenSubtitles session or login was rejected. Check the account credentials.",
            403 => "OpenSubtitles denied this request. Check the API key and account/application subscription.",
            406 => "OpenSubtitles reports that the download quota is exhausted. Try again after the quota resets.",
            429 => "OpenSubtitles rate limit reached. Wait before trying again.",
            _ => "OpenSubtitles could not complete the request (HTTP " + (int)response.StatusCode + ").",
        };
        throw new HttpRequestException(reason, null, response.StatusCode);
    }
    static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadBoundedAsync(response.Content, MaxJsonBytes, cancellationToken).ConfigureAwait(false);
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException) { throw new InvalidDataException("OpenSubtitles returned an invalid JSON response."); }
    }
    static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximum) throw new InvalidDataException("The OpenSubtitles response exceeded the download size limit.");
        using Stream input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream output = new(); byte[] buffer = new byte[16384];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > maximum) throw new InvalidDataException("The OpenSubtitles response exceeded the download size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
    static string String(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    static int Number(JsonElement element, string property, int fallback = 0) => element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : fallback;
    static double Decimal(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) ? number : 0;
    internal static string SafeFileName(string candidate)
    {
        string name = candidate.Split('/', '\\').LastOrDefault() ?? "";
        name = new string(name.Where(c => !char.IsControl(c) && !"<>:\"|?*".Contains(c)).ToArray()).Trim();
        if (name.Length is < 1 or > 180) return "subtitles.srt";
        string extension = Path.GetExtension(name).ToLowerInvariant();
        return extension is ".srt" or ".vtt" or ".ass" or ".ssa" or ".sub" or ".txt" or ".smi" or ".ttml" ? name : "subtitles.srt";
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; token = null; activeCredentials = rejectedCredentials = null;
        if (ownsClients) { api.Dispose(); files.Dispose(); }
        loginGate.Dispose();
    }
}
