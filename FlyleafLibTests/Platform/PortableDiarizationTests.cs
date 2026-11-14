using System.Buffers.Binary;
using System.Diagnostics;
using FlyleafLib.MediaPlayer.Diarization;
using FlyleafLib.Plugins;

namespace FlyleafLib.Platform;

[Collection("PortableEngine")]
public sealed class PortableDiarizationTests
{
    [Fact]
    public void Selected_audio_uses_media_origin_and_preserves_timestamp_gap()
    {
        PortableVideoTestMedia.RequireEngine();
        var source = new DiarizationAudioSource(PortableVideoTestMedia.DiarizationAudio, 1);
        using var wav = new MemoryStream();

        TimeSpan duration = DiarizationAudioExtractor.WriteWave(source, wav, TestContext.Current.CancellationToken);

        byte[] bytes = wav.ToArray();
        Assert.InRange(duration.TotalSeconds, 1.499, 1.501);
        Assert.Equal(bytes.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22)));
        Assert.Equal(16_000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34)));
        Assert.Equal(bytes.Length - 44, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)));
        Assert.Equal(0, AmplitudeAt(0.125));
        Assert.InRange(AmplitudeAt(0.375), 22_800, 23_100);
        Assert.Equal(0, AmplitudeAt(0.875));
        Assert.InRange(AmplitudeAt(1.125), 22_800, 23_100);

        short AmplitudeAt(double seconds) => BinaryPrimitives.ReadInt16LittleEndian(
            bytes.AsSpan(44 + (int)(seconds * 16_000) * sizeof(short)));
    }

    [Fact]
    public void Wave_writer_trims_negative_and_overlapping_audio_without_moving_the_clock()
    {
        using var output = new MemoryStream();
        var writer = new DiarizationWaveWriter(output);
        writer.WriteAt(-1, [1, 0, 2, 0, 3, 0], TestContext.Current.CancellationToken);
        writer.WriteAt(1, [4, 0, 5, 0, 6, 0], TestContext.Current.CancellationToken);
        writer.WriteAt(6, [7, 0], TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromTicks(4_375), writer.Finish());
        Assert.Equal(new byte[] { 2, 0, 3, 0, 5, 0, 6, 0, 0, 0, 0, 0, 7, 0 }, output.ToArray()[44..]);
    }

    [Fact]
    public void Valid_response_preserves_overlap_and_speaker_identity()
    {
        const string response = """
            {"version":1,"turns":[{"start":0.2,"end":0.8,"speaker":"SPEAKER_00"},{"start":0.3,"end":1.0,"speaker":"SPEAKER_01"}]}
            """;

        var turns = LocalDiarizationEngine.ParseResponse(response, TimeSpan.FromSeconds(1));

        Assert.Collection(turns,
            turn => Assert.Equal(new DiarizationTurn(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800), "SPEAKER_00"), turn),
            turn => Assert.Equal(new DiarizationTurn(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1), "SPEAKER_01"), turn));
    }

    [Theory]
    [InlineData("""{"version":2,"turns":[]}""")]
    [InlineData("""{"version":1,"turns":[{"start":-0.1,"end":0.5,"speaker":"A"}]}""")]
    [InlineData("""{"version":1,"turns":[{"start":0.5,"end":1.2,"speaker":"A"}]}""")]
    [InlineData("""{"version":1,"turns":[{"start":0,"end":1e400,"speaker":"A"}]}""")]
    [InlineData("""{"version":1,"turns":[{"start":0.5,"end":0.5,"speaker":"A"}]}""")]
    [InlineData("""{"version":1,"turns":[{"start":0,"end":1,"speaker":" "}]}""")]
    [InlineData("""{"version":1,"turns":[{"start":0,"end":1}]}""")]
    [InlineData("""{"version":1,"turns":null}""")]
    [InlineData("{}\n{}")]
    public void Invalid_response_rejects_entire_diarization(string response)
    {
        Assert.Throws<InvalidDataException>(() => LocalDiarizationEngine.ParseResponse(response, TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sidecar_exit_is_validated_even_when_stdout_contains_a_reply(bool fail)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        const string response = """{"version":1,"turns":[]}""";
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        int exitCode = fail ? 4 : 0;
        if (OperatingSystem.IsWindows())
        {
            info.FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add($"[Console]::WriteLine('{response}'); [Console]::Error.WriteLine('fixture detail'); exit {exitCode}");
        }
        else
        {
            info.FileName = "/bin/sh";
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add($"printf '%s\\n' '{response}'; echo 'fixture detail' >&2; exit {exitCode}");
        }

        var process = OnlineVideoProcess.StartEngine(info);
        try
        {
            if (fail)
            {
                var failure = await Assert.ThrowsAsync<IOException>(
                    () => LocalDiarizationEngine.ReadResponseAsync(process, deadline.Token));
                Assert.Contains("exit code 4", failure.Message);
                Assert.Contains("fixture detail", failure.Message);
            }
            else
            {
                Assert.Equal(response, await LocalDiarizationEngine.ReadResponseAsync(process, deadline.Token));
            }
        }
        finally
        {
            await process.DisposeEngineAsync();
        }
    }
}
