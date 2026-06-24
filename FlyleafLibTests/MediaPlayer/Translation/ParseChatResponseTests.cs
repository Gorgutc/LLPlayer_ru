using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FlyleafLib;

public class ParseChatResponseTests
{
    private const TranslateServiceType Svc = TranslateServiceType.OpenAI;

    [Fact]
    public void ParseChatResponse_ReturnsTrimmedContent()
    {
        string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"  Привет  \"},\"finish_reason\":\"stop\"}]}";
        OpenAIBaseTranslateService.ParseChatResponse(json, Svc, reasonStripRequired: false)
            .Should().Be("Привет");
    }

    [Fact]
    public void ParseChatResponse_StripsReasoning_WhenRequired()
    {
        string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"<think>thinking</think>Привет\"},\"finish_reason\":\"stop\"}]}";
        OpenAIBaseTranslateService.ParseChatResponse(json, Svc, reasonStripRequired: true)
            .Should().Be("Привет");
    }

    [Theory]
    [InlineData("{\"choices\":[]}")]                                                                              // empty choices
    [InlineData("{}")]                                                                                            // no choices
    [InlineData("{\"choices\":[{}]}")]                                                                            // choice has no message -> null content
    [InlineData("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"x\"},\"finish_reason\":\"length\"}]}")] // truncated
    [InlineData("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"<think>no closing tag\"}}]}")]   // reasoning-only -> empty
    public void ParseChatResponse_Throws_OnBadResponse(string json)
    {
        Action act = () => OpenAIBaseTranslateService.ParseChatResponse(json, Svc, reasonStripRequired: true);
        act.Should().Throw<TranslationException>();
    }

    [Fact]
    public void ParseChatResponse_ClassifiesTruncatedReply_AsTruncatedKind()
    {
        // A reply cut off by the token cap (finish_reason=length) must be classified Truncated so the
        // anti-loop recovery treats it as a probable loop and retries, instead of surfacing a hard error.
        string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"partial\"},\"finish_reason\":\"length\"}]}";
        Action act = () => OpenAIBaseTranslateService.ParseChatResponse(json, Svc, reasonStripRequired: false);
        act.Should().Throw<TranslationException>()
            .Which.Kind.Should().Be(TranslationFailureKind.Truncated);
    }

    [Fact]
    public async Task Hello_PreservesRecoverableKind_FromHttpResponseParser()
    {
        const string body = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"partial\"},\"finish_reason\":\"length\"}]}";
        using OneShotHttpServer server = await OneShotHttpServer.StartAsync(body);

        var settings = new OpenAILikeTranslateSettings
        {
            Endpoint = server.Endpoint,
            Model = "test-model"
        };

        Func<Task> act = () => OpenAIBaseTranslateService
            .Hello(settings)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        (await act.Should().ThrowAsync<TranslationException>())
            .Which.Kind.Should().Be(TranslationFailureKind.Truncated);
    }

    private sealed class OneShotHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serverTask;

        private OneShotHttpServer(TcpListener listener, Task serverTask, string endpoint)
        {
            _listener = listener;
            _serverTask = serverTask;
            Endpoint = endpoint;
        }

        public string Endpoint { get; }

        public static Task<OneShotHttpServer> StartAsync(string responseBody)
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task serverTask = Task.Run(async () =>
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                await using NetworkStream stream = client.GetStream();

                byte[] buffer = new byte[4096];
                _ = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);

                byte[] bodyBytes = Encoding.UTF8.GetBytes(responseBody);
                byte[] headerBytes = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: application/json\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(headerBytes, TestContext.Current.CancellationToken);
                await stream.WriteAsync(bodyBytes, TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken);

            return Task.FromResult(new OneShotHttpServer(listener, serverTask, $"http://127.0.0.1:{port}"));
        }

        public void Dispose()
        {
            _listener.Stop();
            try { _serverTask.Wait(TimeSpan.FromSeconds(1)); }
            catch { /* test helper cleanup */ }
        }
    }
}
