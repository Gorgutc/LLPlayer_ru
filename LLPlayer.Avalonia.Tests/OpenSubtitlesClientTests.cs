using System.Net;
using System.Text;
using System.Text.Json;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public class OpenSubtitlesClientTests
{
    static readonly OpenSubtitlesCredentials credentials = new("application-key", "owner", "p\"&word");
    const string Login = """{"token":"session-jwt","base_url":"vip-api.opensubtitles.com"}""";
    const string Search = """{"page":1,"total_pages":3,"data":[{"attributes":{"language":"ja","release":"Example","download_count":12,"ratings":8.1,"feature_details":{"year":2024},"files":[{"file_id":42,"file_name":"example.ja.srt"}]}}]}""";

    [Fact]
    public async Task Search_is_explicit_uses_modern_login_headers_escaped_query_and_returned_VIP_host()
    {
        int calls = 0;
        using HttpClient api = new(new Handler(async (request, cancellation) =>
        {
            Assert.Equal("application-key", Assert.Single(request.Headers.GetValues("Api-Key")));
            if (++calls == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method); Assert.EndsWith("/login", request.RequestUri!.AbsolutePath);
                using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
                Assert.Equal("p\"&word", body.RootElement.GetProperty("password").GetString());
                Assert.Null(request.Headers.Authorization);
                return Json(Login);
            }
            Assert.Equal("vip-api.opensubtitles.com", request.RequestUri!.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("session-jwt", request.Headers.Authorization.Parameter);
            Assert.Equal("?languages=ja&query=a%20%26%20b", request.RequestUri.Query);
            return Json(Search);
        }));
        using HttpClient files = new(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Search must not download files.")));
        using OpenSubtitlesClient client = new(api, files);
        Assert.Equal(0, calls);
        var page = await client.SearchAsync(credentials, "A & B", "ja", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, calls); Assert.Equal(3, page.TotalPages);
        OpenSubtitlesResult row = Assert.Single(page.Results);
        Assert.Equal(42, row.FileId); Assert.Equal(2024, row.Year); Assert.Equal("SRT", row.Format);
        Assert.Equal("Unavailable", row.Duration);
    }

    [Fact]
    public async Task Download_uses_file_id_and_separate_file_transport_without_credentials_or_BOM()
    {
        int calls = 0, fileCalls = 0;
        using HttpClient api = new(new Handler(async (request, cancellation) =>
        {
            if (++calls == 1) return Json(Login);
            Assert.EndsWith("/download", request.RequestUri!.AbsolutePath);
            Assert.Equal("session-jwt", request.Headers.Authorization!.Parameter);
            Assert.True(request.Headers.Contains("Api-Key"));
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            Assert.Equal(42, body.RootElement.GetProperty("file_id").GetInt32());
            return Json("""{"link":"https://www.opensubtitles.com/download/signed","file_name":"../../safe.srt"}""");
        }));
        using HttpClient files = new(new Handler((request, _) =>
        {
            fileCalls++;
            Assert.False(request.Headers.Contains("Api-Key")); Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:01,000\nこんにちは\n")).ToArray()) });
        }));
        using OpenSubtitlesClient client = new(api, files);
        OpenSubtitlesDownload result = await client.DownloadAsync(credentials, new(42, "file.srt", "ja", "", 0, 0), TestContext.Current.CancellationToken);
        Assert.Equal("safe.srt", result.FileName); Assert.Equal(1, fileCalls);
        Assert.False(result.Utf8Bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Contains("こんにちは", Encoding.UTF8.GetString(result.Utf8Bytes));
    }

    [Fact]
    public async Task Failed_login_is_not_retried_automatically_with_the_same_credentials()
    {
        int calls = 0;
        using HttpClient api = new(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); }));
        using HttpClient files = new(); using OpenSubtitlesClient client = new(api, files);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync(credentials, "title", cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SearchAsync(credentials, "title", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("http://www.opensubtitles.com/download/file")]
    [InlineData("https://opensubtitles.com.evil.example/file")]
    [InlineData("https://localhost/file")]
    public async Task Untrusted_download_link_never_receives_a_file_request(string link)
    {
        int calls = 0;
        using HttpClient api = new(new Handler((_, _) => Task.FromResult(Json(++calls == 1 ? Login : JsonSerializer.Serialize(new { link })))));
        using HttpClient files = new(new Handler((_, _) => throw new Xunit.Sdk.XunitException("The untrusted link must not be requested.")));
        using OpenSubtitlesClient client = new(api, files);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(credentials, new(42, "file.srt", "en", "", 0, 0), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task API_redirect_does_not_forward_secrets_to_an_untrusted_host()
    {
        int calls = 0;
        using HttpClient api = new(new Handler((_, _) =>
        {
            calls++;
            HttpResponseMessage redirect = new(HttpStatusCode.TemporaryRedirect);
            redirect.Headers.Location = new Uri("https://elsewhere.example/api/v1/login");
            return Task.FromResult(redirect);
        }));
        using HttpClient files = new(); using OpenSubtitlesClient client = new(api, files);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.SearchAsync(credentials, "title", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Download_payload_size_is_bounded_before_reading_the_content()
    {
        int calls = 0;
        using HttpClient api = new(new Handler((_, _) => Task.FromResult(Json(++calls == 1 ? Login : """{"link":"https://www.opensubtitles.com/download/file"}"""))));
        using HttpClient files = new(new Handler((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = 17 * 1024 * 1024;
            return Task.FromResult(response);
        }));
        using OpenSubtitlesClient client = new(api, files);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(credentials, new(42, "file.srt", "en", "", 0, 0), TestContext.Current.CancellationToken));
    }

    static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
