using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.Services;
using LLPlayer.Core.Services;
using System.Collections.Concurrent;
using System.Diagnostics;
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
        try
        {
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
        catch (Exception failure)
        {
            server.WriteFailureSnapshot(kind, failure);
            throw;
        }
    }

    [Theory]
    [InlineData(TranslationDiagnosticKind.GetModels)]
    [InlineData(TranslationDiagnosticKind.Hello)]
    public async Task Default_transport_cancellation_reaches_pending_http_request(TranslationDiagnosticKind kind)
    {
        await using var server = new LoopbackServer(null);
        try
        {
            var settings = new TranslationSettings { Providers = { [TranslateServiceType.OpenAILike] = new OpenAILikeTranslateSettings { Endpoint = server.Endpoint } } };
            using var cancellation = new CancellationTokenSource();
            Task request = new TranslationDiagnostics().RunAsync(new(TranslateServiceType.OpenAILike, settings, kind), cancellation.Token);
            await server.Request.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        catch (Exception failure)
        {
            server.WriteFailureSnapshot(kind, failure);
            throw;
        }
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
        readonly long startedAt = Stopwatch.GetTimestamp();
        readonly ConcurrentQueue<string> milestones = new();
        readonly Task server;
        public string Endpoint { get; }
        public TaskCompletionSource<string> Request { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LoopbackServer(string? response)
        {
            listener.Start(); Endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Mark("listening");
            server = ServeAsync(response);
        }
        void Mark(string stage)
            => milestones.Enqueue($"{stage}@{Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}ms"
                + $"(thread={Environment.CurrentManagedThreadId},context={SynchronizationContext.Current?.GetType().Name ?? "none"})");
        public void WriteFailureSnapshot(TranslationDiagnosticKind kind, Exception failure)
        {
            // Diagnostics must neither expose HTTP contents nor replace the original test failure.
            try
            {
                TestContext.Current.TestOutputHelper?.WriteLine($"Loopback diagnostic kind={kind}; failureType={failure.GetType().Name}; innerFailureType={failure.InnerException?.GetType().Name ?? "none"}; "
                    + $"elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1}; "
                    + $"serverStatus={server.Status}; serverFaultType={server.Exception?.GetBaseException().GetType().Name ?? "none"}; requestStatus={Request.Task.Status}; "
                    + $"threadPoolThreads={ThreadPool.ThreadCount}; threadPoolPending={ThreadPool.PendingWorkItemCount}; "
                    + $"milestones=[{string.Join("; ", milestones)}]");
            }
            catch { }
        }
        async Task ServeAsync(string? response)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(shutdown.Token);
                Mark("accepted");
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var request = new StringBuilder(); int length = 0;
                while (await reader.ReadLineAsync(shutdown.Token) is { Length: > 0 } line)
                {
                    request.AppendLine(line);
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }
                Mark("headers-read");
                if (length > 0) { var body = new char[length]; await reader.ReadBlockAsync(body, shutdown.Token); request.Append(body); }
                Mark("body-read");
                Request.SetResult(request.ToString());
                Mark("request-completed");
                if (response == null) { Mark("awaiting-cancellation"); await Task.Delay(Timeout.Infinite, shutdown.Token); return; }
                byte[] bytes = Encoding.UTF8.GetBytes(response);
                byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, shutdown.Token);
                Mark("response-header-written");
                await stream.WriteAsync(bytes, shutdown.Token);
                Mark("response-body-written");
            }
            catch (Exception ex)
            {
                Mark($"server-exit-{ex.GetType().Name}");
                throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            shutdown.Cancel(); listener.Stop();
            try { await server; } catch (OperationCanceledException) { }
            shutdown.Dispose();
        }
    }
}
