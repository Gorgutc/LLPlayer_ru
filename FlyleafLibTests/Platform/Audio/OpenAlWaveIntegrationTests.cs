using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform.Audio;

// F-13 (Linux port) integration test: real samples through Player -> OpenAlAudioSink -> OpenAL Soft, captured with
// OpenAL Soft's "wave" backend (a file writer that mixes in real time), then checked for a ~440 Hz tone, and A/V sync.
//
// Why a child process: OpenAL Soft reads its configuration (ALSOFT_DRIVERS / ALSOFT_CONF) once, when the library is
// first used, and the engine is process-global. So the [Fact] below (the parent) writes an alsoft.conf, then re-runs
// THIS test assembly (`dotnet FlyleafLibTests.dll -method <child> -explicit only`, the xunit v3 in-process runner) with
// the wave backend forced through the environment. The child is an Explicit test: it never runs in a normal
// `dotnet test` and skips unless the parent's result-path variable is set.
//
// Needs: LLPLAYER_FFMPEG_DIR (FFmpeg 8 shared libs), LLPLAYER_TEST_MEDIA (test-720p.mp4: 440 Hz AAC sine) and
// libopenal (OpenAL Soft); otherwise skipped with the reason. Optional LLPLAYER_EVIDENCE_DIR receives the wav + logs.
//
// Manual smoke with a real sound card (not automatable here): run the Avalonia host (or this child with
// ALSOFT_DRIVERS unset) and listen for a clean 440 Hz tone in sync with the moving test pattern; change the volume.
[Collection("PortableEngine")]
public class OpenAlWaveIntegrationTests(ITestOutputHelper output)
{
    const string FFmpegDirVar   = "LLPLAYER_FFMPEG_DIR";
    const string TestMediaVar   = "LLPLAYER_TEST_MEDIA";
    const string ResultVar      = "LLPLAYER_OPENAL_WAVE_RESULT";
    const string EvidenceVar    = "LLPLAYER_EVIDENCE_DIR";

    const int    Rate           = 48000;
    const double MeasureSeconds = 3.0;
    const double MaxDesyncMs    = 100;

    sealed record ChildResult(double MaxDesyncMs, double MeanDesyncMs, int Samples, double PlayedSeconds,
        int UnderrunRestarts, int LatencySamples, string Backend, string SinkType);

    static (string ffmpegDir, string media) RequireEnvironment()
    {
        string? ffmpegDir = Environment.GetEnvironmentVariable(FFmpegDirVar);
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpegDir), $"{FFmpegDirVar} is not set (folder with the FFmpeg 8 shared libraries).");
        Assert.SkipUnless(Directory.Exists(ffmpegDir), $"{FFmpegDirVar} '{ffmpegDir}' does not exist.");

        string? media = Environment.GetEnvironmentVariable(TestMediaVar);
        Assert.SkipWhen(string.IsNullOrEmpty(media), $"{TestMediaVar} is not set (path of the 440 Hz test clip).");
        Assert.SkipUnless(File.Exists(media), $"{TestMediaVar} '{media}' does not exist.");

        return (ffmpegDir!, media!);
    }

    // === Parent ==================================================================================================

    [Fact]
    public void OpenAlWaveBackend_PlaysTestMediaAsA440HzTone_InSyncWithVideo()
    {
        RequireEnvironment();
        Assert.SkipUnless(OpenAlNative.TryGet(out var native, out string? error), $"OpenAL is not installed: {error}");

        string dir = Path.Combine(Path.GetTempPath(), $"llplayer-openal-wave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string wav      = Path.Combine(dir, "out.wav");
        string conf     = Path.Combine(dir, "alsoft.conf");
        string result   = Path.Combine(dir, "result.json");

        try
        {
            File.WriteAllText(conf, $"""
                [general]
                channels = stereo
                sample-type = int16
                frequency = {Rate}

                [wave]
                file = {wav}
                """);

            var (exitCode, log) = RunChild(conf, result);
            output.WriteLine(log);
            SaveEvidence(wav, log, result);

            exitCode.Should().Be(0, $"the child test run must pass (OpenAL library: {native.LibraryName}):\n{log}");
            File.Exists(result).Should().BeTrue("the child must really have run (not been filtered out or skipped)");
            var child = JsonSerializer.Deserialize<ChildResult>(File.ReadAllText(result))!;
            output.WriteLine($"child: {child}");

            child.Backend.Should().Be("OpenAL");
            child.SinkType.Should().Be(nameof(OpenAlAudioSink));
            child.Samples.Should().BeGreaterThan(20);
            child.MaxDesyncMs.Should().BeLessThan(MaxDesyncMs);

            File.Exists(wav).Should().BeTrue("OpenAL Soft's wave backend writes the mix to the configured file");
            var (channels, rate, bits, pcm) = ReadWav(wav);
            channels.Should().Be(2);
            rate.Should().Be(Rate);
            bits.Should().Be(16);
            double seconds = pcm.Length / 2.0 / rate;
            output.WriteLine($"wav: {seconds:F2} s of {rate} Hz stereo s16");
            seconds.Should().BeGreaterThanOrEqualTo(2.0);

            foreach (int ch in new[] { 0, 1 })
            {
                var (toneSeconds, freq, ratio) = AnalyzeTone(pcm, ch, rate);
                output.WriteLine($"channel {ch}: tone {toneSeconds:F2} s, zero-crossing {freq:F1} Hz, Goertzel 440 Hz dominance x{ratio:F0}");

                toneSeconds.Should().BeGreaterThanOrEqualTo(2.0, "~2 s or more of the clip's audio must have reached the device");
                freq.Should().BeApproximately(440, 10);
                ratio.Should().BeGreaterThan(20, "440 Hz must dominate the neighbouring frequencies");
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    (int exitCode, string log) RunChild(string conf, string result)
    {
        string testDll = typeof(OpenAlWaveIntegrationTests).Assembly.Location;
        string method  = $"{typeof(OpenAlWaveIntegrationTests).FullName}.{nameof(WaveChild_PlaysTestMediaThroughOpenAl)}";

        ProcessStartInfo psi = new(DotnetHost())
        {
            RedirectStandardOutput  = true,
            RedirectStandardError   = true,
            UseShellExecute         = false,
            WorkingDirectory        = Path.GetDirectoryName(testDll)!
        };
        foreach (string arg in new[] { testDll, "-method", method, "-explicit", "only", "-noLogo", "-noColor", "-parallel", "none" })
            psi.ArgumentList.Add(arg);

        psi.Environment["ALSOFT_DRIVERS"]   = "wave";
        psi.Environment["ALSOFT_CONF"]      = conf;
        psi.Environment[ResultVar]          = result;
        psi.Environment.Remove(AudioBackendFactory.BackendVariable); // exercise the default (auto) selection

        using Process p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();

        if (!p.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            p.Kill(true);
            p.WaitForExit();
            return (-1, $"child timed out\n{stdout.Result}\n{stderr.Result}");
        }

        return (p.ExitCode, $"$ {psi.FileName} {string.Join(' ', psi.ArgumentList)}\n{stdout.Result}\n{stderr.Result}");
    }

    static string DotnetHost()
    {
        // .../dotnet/shared/Microsoft.NETCore.App/<version>/ -> .../dotnet/dotnet (the running runtime's own muxer)
        string root = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        string muxer = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(muxer) ? muxer : Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    }

    void SaveEvidence(string wav, string log, string result)
    {
        string? evidence = Environment.GetEnvironmentVariable(EvidenceVar);
        if (string.IsNullOrEmpty(evidence))
            return;

        Directory.CreateDirectory(evidence);
        if (File.Exists(wav))
            File.Copy(wav, Path.Combine(evidence, "openal-wave-out.wav"), true);
        if (File.Exists(result))
            File.Copy(result, Path.Combine(evidence, "openal-wave-result.json"), true);
        File.WriteAllText(Path.Combine(evidence, "openal-wave-child.log"), log);
        output.WriteLine($"evidence saved to {evidence}");
    }

    // === Child (runs only when launched by the parent) ============================================================

    sealed class NullSurface : IVideoSurface
    {
        public void PresentFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride) { }
        public void ClearFrame() { }
    }

    [Fact(Explicit = true)]
    public void WaveChild_PlaysTestMediaThroughOpenAl()
    {
        string? resultPath = Environment.GetEnvironmentVariable(ResultVar);
        Assert.SkipWhen(string.IsNullOrEmpty(resultPath), $"Child half of {nameof(OpenAlWaveBackend_PlaysTestMediaAsA440HzTone_InSyncWithVideo)}; runs only when launched by it.");
        var (ffmpegDir, media) = RequireEnvironment();

        List<string> backendLog = [];
        AudioEngine.Backend = AudioBackendFactory.CreateDefault(backendLog.Add);
        foreach (string line in backendLog)
            output.WriteLine(line);
        AudioEngine.Backend.Should().BeOfType<OpenAlAudioBackend>("ALSOFT_DRIVERS=wave provides a default device");

        Utils.IsTesting = false; // let the engine's UI actions run inline (see PortableEngineSmokeTests)
        Engine.Start(new EngineConfig
        {
            FFmpegPath      = ffmpegDir,
            PluginsPath     = null,
            UIRefresh       = false,
            LogLevel        = LogLevel.Quiet,
            FFmpegLogLevel  = Flyleaf.FFmpeg.LogLevel.Quiet
        });

        Config config = new();
        config.Player.AutoPlay = false;
        Player player = new(config);
        try
        {
            player.Open(media).Success.Should().BeTrue();
            player.Audio.IsOpened.Should().BeTrue();

            // Audio clock: PTS of the sample being heard = end of the last submitted frame, minus what is still queued,
            // minus the device delay the player compensates for (both in the sink's sample numbering).
            object clockLock = new();
            long lastEndTicks = 0;
            ulong submittedAfter = 0;
            bool hasAudio = false;
            player.Audio.SamplesAdded += (_, frame) =>
            {
                lock (clockLock)
                {
                    lastEndTicks    = frame.Timestamp + (long)(frame.dataLen / 4 * player.Audio.Timebase);
                    submittedAfter  = player.Audio.submittedSamples; // already includes this frame
                    hasAudio        = true;
                }
            };

            player.Renderer.SetControlSize(640, 360);
            player.Renderer.Surface = new NullSurface();
            player.Play();

            // The output is created once the audio codec is running (Audio.Refresh(fromCodec) -> Initialize).
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(20) && (player.CurTime < TimeSpan.FromMilliseconds(500).Ticks || !hasAudio
                || player.Audio.sink is not OpenAlAudioSink warm || warm.SamplesPlayed == 0))
                Thread.Sleep(20);
            player.Status.Should().Be(Status.Playing);
            player.Audio.sink.Should().BeOfType<OpenAlAudioSink>();
            var sink = (OpenAlAudioSink)player.Audio.sink;
            sink.SamplesPlayed.Should().BeGreaterThan(0UL, "OpenAL must consume the submitted audio");

            List<double> diffs = [];
            Stopwatch measure = Stopwatch.StartNew();
            while (measure.Elapsed < TimeSpan.FromSeconds(MeasureSeconds))
            {
                long end; ulong after;
                lock (clockLock) { end = lastEndTicks; after = submittedAfter; }

                ulong played    = sink.SamplesPlayed;
                long  queued    = played >= after ? 0 : (long)((after - played) * player.Audio.Timebase);
                long  audioClock= end - queued - player.Audio.GetDeviceDelay();
                long  curTime   = player.CurTime;

                diffs.Add(Math.Abs(curTime - audioClock) / 10_000.0);
                Thread.Sleep(50);
            }

            player.Status.Should().Be(Status.Playing);
            double max = diffs.Max();
            output.WriteLine($"A/V desync over {MeasureSeconds} s: max {max:F1} ms, mean {diffs.Average():F1} ms ({diffs.Count} samples)");

            ChildResult result = new(max, diffs.Average(), diffs.Count, sink.SamplesPlayed / (double)Rate,
                sink.UnderrunRestarts, sink.LatencySamples, AudioEngine.Backend.Name, sink.GetType().Name);

            player.Pause();
            player.Dispose(); // closes the OpenAL device: the wave backend finalizes the file

            File.WriteAllText(resultPath!, JsonSerializer.Serialize(result));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{result}"));
            max.Should().BeLessThan(MaxDesyncMs);
        }
        finally
        {
            if (!player.IsDisposed)
                player.Dispose();
        }
    }

    // === WAV analysis ============================================================================================

    /// <summary>Reads a PCM (or WAVE_FORMAT_EXTENSIBLE PCM) RIFF file: format and the interleaved s16 samples.</summary>
    static (int channels, int rate, int bits, short[] samples) ReadWav(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        using BinaryReader r = new(new MemoryStream(data));

        new string(r.ReadChars(4)).Should().Be("RIFF");
        r.ReadUInt32();
        new string(r.ReadChars(4)).Should().Be("WAVE");

        int channels = 0, rate = 0, bits = 0;
        while (r.BaseStream.Position + 8 <= data.Length)
        {
            string id = new(r.ReadChars(4));
            long size = r.ReadUInt32();
            long start = r.BaseStream.Position;

            if (id == "fmt ")
            {
                ushort tag = r.ReadUInt16();
                channels = r.ReadUInt16();
                rate = r.ReadInt32();
                r.ReadInt32();  // byte rate
                r.ReadUInt16(); // block align
                bits = r.ReadUInt16();
                tag.Should().BeOneOf((ushort)1, (ushort)0xFFFE); // PCM / extensible (PCM sub-format)
            }
            else if (id == "data")
            {
                // The writer patches the sizes when the device closes; tolerate an unpatched (0 / oversized) size.
                long available = data.Length - start;
                if (size == 0 || size > available)
                    size = available;

                short[] samples = new short[size / 2];
                Buffer.BlockCopy(data, (int)start, samples, 0, samples.Length * 2);
                return (channels, rate, bits, samples);
            }

            r.BaseStream.Position = start + size + (size & 1);
        }

        throw new InvalidDataException("no data chunk");
    }

    /// <summary>
    /// For one channel: seconds with signal (10 ms windows above a silence threshold), the zero-crossing frequency and
    /// the Goertzel power ratio of 440 Hz against neighbouring frequencies, over the loud part of the capture.
    /// </summary>
    static (double toneSeconds, double zeroCrossingHz, double dominance) AnalyzeTone(short[] pcm, int channel, int rate)
    {
        int frames = pcm.Length / 2;
        int window = rate / 100;
        List<int> loudWindows = [];
        for (int w = 0; w + window <= frames; w += window)
        {
            double sum = 0;
            for (int i = w; i < w + window; i++)
            {
                double v = pcm[i * 2 + channel];
                sum += v * v;
            }

            if (Math.Sqrt(sum / window) > 300) // the clip's sine is ~ -18 dBFS (RMS ~2900); silence is 0
                loudWindows.Add(w);
        }

        double toneSeconds = loudWindows.Count * window / (double)rate;
        if (loudWindows.Count == 0)
            return (0, 0, 0);

        // Analyse up to 1 s from the middle of the loud span.
        int first = loudWindows[0], last = loudWindows[^1] + window;
        int length = Math.Min(rate, last - first);
        int from = first + (last - first - length) / 2;
        double[] x = new double[length];
        for (int i = 0; i < length; i++)
            x[i] = pcm[(from + i) * 2 + channel];

        int crossings = 0;
        for (int i = 1; i < length; i++)
            if ((x[i - 1] < 0) != (x[i] < 0))
                crossings++;
        double zeroCrossingHz = crossings / 2.0 / (length / (double)rate);

        double target = Goertzel(x, 440, rate);
        double others = new[] { 220.0, 330, 550, 660, 880, 1000 }.Max(f => Goertzel(x, f, rate));
        return (toneSeconds, zeroCrossingHz, target / Math.Max(others, 1e-9));
    }

    static double Goertzel(double[] x, double freq, int rate)
    {
        double coeff = 2 * Math.Cos(2 * Math.PI * freq / rate);
        double s1 = 0, s2 = 0;
        foreach (double v in x)
        {
            double s = v + coeff * s1 - s2;
            s2 = s1;
            s1 = s;
        }

        return s1 * s1 + s2 * s2 - coeff * s1 * s2;
    }
}
