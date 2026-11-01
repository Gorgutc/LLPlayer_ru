using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.Services;
using LLPlayer.Core.Services;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LLPlayer.Avalonia.Tests;

public class TranslationDiagnosticsTests
{
    [Theory]
    [InlineData(TranslationDiagnosticKind.Check)]
    [InlineData(TranslationDiagnosticKind.GetModels)]
    [InlineData(TranslationDiagnosticKind.Hello)]
    public async Task Default_transport_uses_explicit_loopback_protocol_and_discards_hello_response(TranslationDiagnosticKind kind)
    {
        string body = kind == TranslationDiagnosticKind.Hello
            ? "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"synthetic-response-secret\"},\"finish_reason\":\"stop\"}]}"
            : "{\"data\":[{\"id\":\"model-b\"},{\"id\":\"model-a\"}]}";
        await using var server = new LoopbackServer(body);
        var settings = new TranslationSettings { WordProvider = "OpenAILike", Providers = { [TranslateServiceType.OpenAILike] = new OpenAILikeTranslateSettings
            { Endpoint = server.Endpoint, Model = "custom-model", ApiKey = "synthetic-key", ChatPath = "/custom/chat" } } };
        var service = new TranslationDiagnostics();
        var result = await service.RunAsync(new(TranslateServiceType.OpenAILike, settings, kind), TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(TranslationDiagnosticOutcome.Success, result.Outcome);
        string request = await server.Request.Task;
        Assert.StartsWith(kind == TranslationDiagnosticKind.Hello ? "POST /custom/chat " : "GET /v1/models ", request);
        Assert.Contains("Bearer synthetic-key", request);
        if (kind == TranslationDiagnosticKind.GetModels) Assert.Equal(new[] { "model-a", "model-b" }, result.Models);
        else Assert.Null(result.Models);
        if (kind == TranslationDiagnosticKind.Hello) { Assert.Contains("Hello", request); Assert.Contains("custom-model", request); }
        Assert.DoesNotContain("synthetic-response-secret", result.ToString());
    }

    [Theory]
    [InlineData(TranslationDiagnosticKind.GetModels)]
    [InlineData(TranslationDiagnosticKind.Hello)]
    public async Task Default_transport_cancellation_reaches_pending_http_request(TranslationDiagnosticKind kind)
    {
        await using var server = new LoopbackServer(null);
        var settings = new TranslationSettings { Providers = { [TranslateServiceType.OpenAILike] = new OpenAILikeTranslateSettings { Endpoint = server.Endpoint } } };
        using var cancellation = new CancellationTokenSource();
        Task request = new TranslationDiagnostics().RunAsync(new(TranslateServiceType.OpenAILike, settings, kind), cancellation.Token);
        await server.Request.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unsupported_provider_returns_explicit_outcome_without_network()
    {
        var settings = new TranslationSettings { Providers = { [TranslateServiceType.GoogleV1] = new GoogleV1TranslateSettings { Endpoint = "https://must-not-contact.invalid" } } };
        var result = await new TranslationDiagnostics().RunAsync(new(TranslateServiceType.GoogleV1, settings, TranslationDiagnosticKind.Check), TestContext.Current.CancellationToken);
        Assert.Equal(TranslationDiagnosticOutcome.Unsupported, result.Outcome);
    }

    sealed class LoopbackServer : IAsyncDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource shutdown = new();
        readonly Task server;
        public string Endpoint { get; }
        public TaskCompletionSource<string> Request { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LoopbackServer(string? response)
        {
            listener.Start(); Endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            server = ServeAsync(response);
        }
        async Task ServeAsync(string? response)
        {
            using var client = await listener.AcceptTcpClientAsync(shutdown.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var request = new StringBuilder(); int length = 0;
            while (await reader.ReadLineAsync(shutdown.Token) is { Length: > 0 } line)
            {
                request.AppendLine(line);
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
            if (length > 0) { var body = new char[length]; await reader.ReadBlockAsync(body, shutdown.Token); request.Append(body); }
            Request.SetResult(request.ToString());
            if (response == null) { await Task.Delay(Timeout.Infinite, shutdown.Token); return; }
            byte[] bytes = Encoding.UTF8.GetBytes(response);
            byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, shutdown.Token); await stream.WriteAsync(bytes, shutdown.Token);
        }
        public async ValueTask DisposeAsync()
        {
            shutdown.Cancel(); listener.Stop();
            try { await server; } catch (OperationCanceledException) { }
            shutdown.Dispose();
        }
    }
}
