using System.Diagnostics;
using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform;

// F-13 (Linux port) smoke test of the portable engine: FFmpeg loading, open, software decode -> BGRA surface, and
// NullAudioSink-driven A/V playback. Needs real FFmpeg shared libraries + a media file, so it runs only when
//   LLPLAYER_FFMPEG_DIR  = folder with the FFmpeg 8 shared libs (libavcodec.so.62, ...)
//   LLPLAYER_TEST_MEDIA  = a ~12 s H.264 + AAC clip (e.g. test-720p.mp4)
// are set; otherwise it is reported as skipped with the reason. The engine is process-global (static), so this runs
// in its own non-parallel collection.
[CollectionDefinition("PortableEngine", DisableParallelization = true)]
public sealed class PortableEngineCollection
{
}

[Collection("PortableEngine")]
public class PortableEngineSmokeTests
{
    const string FFmpegDirVar   = "LLPLAYER_FFMPEG_DIR";
    const string TestMediaVar   = "LLPLAYER_TEST_MEDIA";

    static readonly object engineLock = new();

    static (string ffmpegDir, string media) RequireEnvironment()
    {
        string? ffmpegDir = Environment.GetEnvironmentVariable(FFmpegDirVar);
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpegDir), $"{FFmpegDirVar} is not set (folder with the FFmpeg 8 shared libraries).");
        Assert.SkipUnless(Directory.Exists(ffmpegDir), $"{FFmpegDirVar} '{ffmpegDir}' does not exist.");

        string? media = Environment.GetEnvironmentVariable(TestMediaVar);
        Assert.SkipWhen(string.IsNullOrEmpty(media), $"{TestMediaVar} is not set (path of a ~12 s H.264/AAC test clip).");
        Assert.SkipUnless(File.Exists(media), $"{TestMediaVar} '{media}' does not exist.");

        return (ffmpegDir!, media!);
    }

    static void EnsureEngine(string ffmpegDir)
    {
        lock (engineLock)
        {
            if (Engine.IsLoaded)
                return;

            Engine.Start(new EngineConfig
            {
                FFmpegPath      = ffmpegDir,
                PluginsPath     = null,
                UIRefresh       = false,
                LogLevel        = LogLevel.Quiet,
                FFmpegLogLevel  = Flyleaf.FFmpeg.LogLevel.Quiet
            });
        }
    }

    sealed class CountingSurface : IVideoSurface
    {
        public int Frames;
        public int Clears;
        public int Width, Height, Stride;
        public byte CenterB, CenterG, CenterR, CenterA;

        public void PresentFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride)
        {
            Width   = width;
            Height  = height;
            Stride  = stride;
            int center = (height / 2) * stride + (width / 2) * 4;
            CenterB = bgra[center];
            CenterG = bgra[center + 1];
            CenterR = bgra[center + 2];
            CenterA = bgra[center + 3];
            Interlocked.Increment(ref Frames);
        }

        public void ClearFrame() => Interlocked.Increment(ref Clears);
    }

    [Fact]
    public void Engine_OpensAndPlaysTestMedia_WithSoftwareRendererAndNullAudio()
    {
        var (ffmpegDir, media) = RequireEnvironment();

        // UIInvokeIfRequired is a no-op while Utils.IsTesting is set (other test classes set it process-wide);
        // the engine/player need their UI actions to run (inline: no dispatcher on the portable build).
        Utils.IsTesting = false;
        Player? player = null;
        try
        {
            EnsureEngine(ffmpegDir);

            Engine.IsLoaded.Should().BeTrue();
            Engine.FFmpeg.Version.Should().StartWith("62.", "the FFmpeg 8.x libavformat major version is 62");
            Engine.Video.GPUAdapters.Should().ContainSingle().Which.Value.Description.Should().Contain("Software");
            Engine.Audio.Devices.Should().Contain(Engine.Audio.DefaultDevice);

            Config config = new();
            config.Player.AutoPlay = false;
            player = new(config);

            var args = player.Open(media);

            args.Success.Should().BeTrue(args.Error);
            TimeSpan.FromTicks(player.Duration).TotalSeconds.Should().BeApproximately(12, 0.5);
            player.Video.IsOpened.Should().BeTrue();
            player.Audio.IsOpened.Should().BeTrue();
            player.VideoDemuxer.VideoStream.Width.Should().Be(1280u);
            player.VideoDemuxer.VideoStream.Height.Should().Be(720u);
            player.AudioDecoder.AudioStream.SampleRate.Should().Be(48000);

            // Playback: software BGRA frames must reach the host surface while the NullAudioSink paces audio.
            CountingSurface surface = new();
            player.Renderer.SetControlSize(640, 360);
            player.Renderer.Surface = surface;

            player.Play();

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(10) && (surface.Frames < 30 || player.CurTime < TimeSpan.FromSeconds(1).Ticks))
                Thread.Sleep(50);

            player.Status.Should().Be(Status.Playing);
            surface.Frames.Should().BeGreaterThanOrEqualTo(30, "~1 s of 30 fps video must have been presented");
            TimeSpan.FromTicks(player.CurTime).TotalSeconds.Should().BeGreaterThanOrEqualTo(1);
            player.Renderer.VideoProcessor.Should().Be(VideoProcessors.SwsScale);
            player.Video.Width.Should().Be(1280, "set from the renderer's visible size once the first frame is decoded");
            player.Video.Height.Should().Be(720);
            surface.Width.Should().Be(1280);
            surface.Height.Should().Be(720);
            surface.Stride.Should().BeGreaterThanOrEqualTo(1280 * 4);
            surface.CenterA.Should().Be(255, "video frames are converted to opaque BGRA");
            player.Renderer.Viewport.Width.Should().Be(640);
            player.Renderer.Viewport.Height.Should().Be(360);

            // Real-time pacing: after ~sw.Elapsed of wall clock, playback time must not run ahead of it.
            TimeSpan.FromTicks(player.CurTime).Should().BeLessThanOrEqualTo(sw.Elapsed + TimeSpan.FromMilliseconds(500));

            player.Pause();
            player.Status.Should().Be(Status.Paused);
        }
        finally
        {
            player?.Dispose();
            Utils.IsTesting = true;
        }
    }
}
