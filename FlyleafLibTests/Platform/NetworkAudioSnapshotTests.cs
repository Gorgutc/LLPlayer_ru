using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaPlayer.Diarization;

namespace FlyleafLib.Platform;

[Collection("PortableEngine")]
public sealed class NetworkAudioSnapshotTests
{
    [Theory]
    [InlineData(MediaType.Video)]
    [InlineData(MediaType.Audio)]
    public async Task Authenticated_HTTP_snapshot_keeps_the_selected_track_and_sample_clock(MediaType demuxerType)
    {
        PortableVideoTestMedia.RequireEngine();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        CancellationToken token = deadline.Token;
        string media = PortableVideoTestMedia.DiarizationAudio;
        byte[] bytes = await File.ReadAllBytesAsync(media, token);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Task server = Task.Run(() => ServeAsync(listener, bytes, token), token);

        try
        {
            var config = new Config();
            new DemuxerInput
            {
                HTTPHeaders = new() { ["X-LLPlayer-Fixture"] = "selected-audio" }
            }.ApplyHttpFormatOptions(config.Demuxer.GetFormatOptPtr(demuxerType));
            using var remoteWave = new MemoryStream();
            using var localWave = new MemoryStream();
            string url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/fixture.mkv";

            TimeSpan remote = DiarizationAudioExtractor.WriteNetworkWave(config, demuxerType, url, 1, remoteWave, token);
            TimeSpan local = DiarizationAudioExtractor.WriteWave(new(media, 1), localWave, token);

            Assert.Equal(local, remote);
            Assert.Equal(localWave.ToArray(), remoteWave.ToArray());
            Assert.InRange(remote.TotalSeconds, 1.499, 1.501);
        }
        finally
        {
            await deadline.CancelAsync();
            await server;
        }
    }

    private static async Task ServeAsync(TcpListener listener, byte[] media, CancellationToken token)
    {
        try
        {
            while (true)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(token);
                using NetworkStream stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                string? request = await reader.ReadLineAsync(token);
                if (request == null)
                    continue;
                bool authorized = false;
                int? requestedOffset = null;
                while (await reader.ReadLineAsync(token) is { Length: > 0 } line)
                {
                    if (line.Equals("X-LLPlayer-Fixture: selected-audio", StringComparison.OrdinalIgnoreCase))
                        authorized = true;
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)
                        && line.EndsWith('-')
                        && int.TryParse(line.AsSpan(13, line.Length - 14), NumberStyles.None,
                            CultureInfo.InvariantCulture, out int offset))
                        requestedOffset = offset;
                }

                if (!authorized)
                {
                    await stream.WriteAsync("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8
                        .ToArray(), token);
                    continue;
                }
                int start = requestedOffset ?? 0;
                string status = requestedOffset.HasValue ? "206 Partial Content" : "200 OK";
                string range = requestedOffset.HasValue ? $"Content-Range: bytes {start}-{media.Length - 1}/{media.Length}\r\n" : "";
                string headers = $"HTTP/1.1 {status}\r\n{range}Accept-Ranges: bytes\r\n" +
                    $"Content-Type: video/x-matroska\r\nContent-Length: {media.Length - start}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), token);
                if (!request.StartsWith("HEAD ", StringComparison.Ordinal))
                    await stream.WriteAsync(media.AsMemory(start), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
