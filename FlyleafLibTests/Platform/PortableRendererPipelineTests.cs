using System.Diagnostics;
using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform;

// F-13 portable software renderer, end to end through Player -> Renderer -> IVideoSurface with real FFmpeg decoding:
// rotation / mirroring, colour filters, deinterlacing, HDR to SDR, resolution changes, seek while paused, clear on
// stop, snapshots, the per-frame allocation budget (+ measured ms/frame) and native-resource release over repeated
// open / dispose. Needs LLPLAYER_FFMPEG_DIR (FFmpeg 8 shared libs) and the FFmpeg CLI next to them (bin/ffmpeg) to
// generate the synthetic clips (see PortableVideoTestMedia); skipped otherwise. Waits are poll-until with generous
// deadlines (no timing asserts).
[Collection("PortableEngine")]
public class PortableRendererPipelineTests
{
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    static readonly (int R, int G, int B) Blue  = (0, 0, 255);
    static readonly (int R, int G, int B) Red   = (255, 0, 0);
    static readonly (int R, int G, int B) White = (255, 255, 255);
    static readonly (int R, int G, int B) Lime  = (0, 255, 0);

    static bool WaitFor(Func<bool> condition, TimeSpan? timeout = null)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < (timeout ?? Deadline))
        {
            if (condition())
                return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    static bool Near((int R, int G, int B) a, (int R, int G, int B) e, int tolerance)
        => Math.Abs(a.R - e.R) <= tolerance && Math.Abs(a.G - e.G) <= tolerance && Math.Abs(a.B - e.B) <= tolerance;

    sealed class Session : IDisposable
    {
        public readonly Player              Player;
        public readonly CapturingSurface    Surface = new();
        public Renderer Renderer => Player.Renderer;
        public Config   Config   => Player.Config;

        public Session(string media, int controlWidth, int controlHeight, Action<Config>? configure = null)
        {
            PortableVideoTestMedia.RequireEngine();
            Utils.IsTesting = false; // UI actions must run inline (see PortableEngineSmokeTests)

            Config config = new();
            config.Player.AutoPlay = false;
            configure?.Invoke(config);

            Player = new(config);
            Renderer.SetControlSize(controlWidth, controlHeight);
            Renderer.Surface = Surface;

            var args = Player.Open(media);
            args.Success.Should().BeTrue(args.Error);
            Player.Seek(0); // opening paused does not show a frame; a paused seek presents the first one
            WaitFor(() => Surface.Frames > 0).Should().BeTrue("the first frame is presented after a paused seek");
        }

        /// <summary>Waits for a presented frame (newer than <paramref name="after"/>) that satisfies <paramref name="predicate"/>.</summary>
        public (byte[] Px, int W, int H) WaitFrame(Func<byte[], int, int, bool> predicate, int after = 0, string because = "")
        {
            (byte[] Px, int W, int H) last = ([], 0, 0);
            bool ok = WaitFor(() =>
            {
                var s = Surface.Snapshot();
                if (s.Frames <= after || s.Width == 0)
                    return false;
                last = (s.Pixels, s.Width, s.Height);
                return predicate(s.Pixels, s.Width, s.Height);
            });

            ok.Should().BeTrue($"a matching frame must be presented {because} (last: {last.W}x{last.H})");
            return last;
        }

        public void Dispose()
        {
            Player.Dispose();
            Utils.IsTesting = true;
        }
    }

    static (int R, int G, int B)[,] Quadrants(byte[] px, int w, int h) => new[,]
    {
        { CapturingSurface.Average(px, w, h, w / 4, h / 4), CapturingSurface.Average(px, w, h, 3 * w / 4, h / 4) },
        { CapturingSurface.Average(px, w, h, w / 4, 3 * h / 4), CapturingSurface.Average(px, w, h, 3 * w / 4, 3 * h / 4) },
    };

    /// <summary>Expected quadrant colours of the quad clip after flip-then-clockwise-rotation (independent reference).</summary>
    static (int R, int G, int B)[,] ExpectedQuadrants(uint rotation, bool hflip, bool vflip)
    {
        (int, int, int)[] colours = [Blue, Red, White, Lime];
        var ids = PortableVideoPipelineUnitTests.Reference(new[,] { { 0, 1 }, { 2, 3 } }, rotation, hflip, vflip);
        return new[,]
        {
            { colours[ids[0, 0]], colours[ids[0, 1]] },
            { colours[ids[1, 0]], colours[ids[1, 1]] },
        };
    }

    static bool QuadrantsMatch(byte[] px, int w, int h, (int, int, int)[,] expected)
    {
        var q = Quadrants(px, w, h);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                if (!Near(q[y, x], expected[y, x], 60))
                    return false;
        return true;
    }

    [Fact]
    public void StreamDisplayMatrix_IsAppliedByTheRenderer_AndViewportUsesTheRotatedSize()
    {
        using Session s = new(PortableVideoTestMedia.QuadRotated, 400, 400);

        // -display_rotation 90 is counter-clockwise, i.e. 270 degrees clockwise (Flyleaf's convention)
        s.Renderer.Rotation.Should().Be(270u);
        var expected = ExpectedQuadrants(270, false, false);
        var f = s.WaitFrame((px, w, h) => w == 180 && h == 320 && QuadrantsMatch(px, w, h, expected), because: "upright (portrait)");

        s.Renderer.VisibleWidth.Should().Be(320u, "the visible (decoded) size is not rotated");
        s.Renderer.Viewport.Width.Should().Be(225, "400 * 180 / 320: the viewport follows the rotated aspect ratio");
        s.Renderer.Viewport.Height.Should().Be(400);
        f.W.Should().Be(180);

        // Snapshot: native upright size, same orientation as presented
        string file = Path.Combine(Path.GetTempPath(), $"llplayer-snap-{Guid.NewGuid():N}.bmp");
        try
        {
            s.Renderer.TakeSnapshotToFile(file).Should().BeTrue();
            var (bmp, bw, bh) = ReadBmp(file);
            (bw, bh).Should().Be((180, 320));
            QuadrantsMatch(bmp, bw, bh, expected).Should().BeTrue("the snapshot is rotated like the presented frame");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void UserRotationAndFlips_AllCombinations_PresentTheExpectedOrientation()
    {
        using Session s = new(PortableVideoTestMedia.Quad, 1000, 1000);

        foreach (uint rotation in new uint[] { 0, 90, 180, 270 })
            foreach (bool h in new[] { false, true })
                foreach (bool v in new[] { false, true })
                {
                    s.Config.Video.Rotation = rotation;
                    s.Config.Video.HFlip    = h;
                    s.Config.Video.VFlip    = v;

                    var expected = ExpectedQuadrants(rotation, h, v);
                    int ew = rotation % 180 == 0 ? 320 : 180, eh = rotation % 180 == 0 ? 180 : 320;
                    s.WaitFrame((px, w, hh) => w == ew && hh == eh && QuadrantsMatch(px, w, hh, expected),
                        because: $"for rotation {rotation}, hflip {h}, vflip {v}");

                    s.Renderer.Rotation.Should().Be(rotation);
                    s.Renderer.HFlip.Should().Be(h);
                    s.Renderer.VFlip.Should().Be(v);
                }
    }

    [Fact]
    public void ColorFilters_ChangeTheConvertedPixels_LikeTheWindowsPixelShader()
    {
        using Session grey = new(PortableVideoTestMedia.Solid("808080"), 320, 180);
        var filters = grey.Config.Video.FLFilters;
        (int, int, int) Center(byte[] px, int w, int h) => CapturingSurface.Average(px, w, h, w / 2, h / 2);

        grey.WaitFrame((px, w, h) => Near(Center(px, w, h), (128, 128, 128), 3), because: "unfiltered grey");

        // Brightness +50 -> +0.25 (shader units): 128 + 63.75
        filters[FLFilters.Brightness].Value = 50;
        grey.WaitFrame((px, w, h) => Near(Center(px, w, h), (192, 192, 192), 3), because: "brightness +50");
        filters[FLFilters.Brightness].Value = 0;

        // Contrast +50 -> 1.5: YUV luma curve Y' = lerp(Y, Y^0.5, smoothstep(Y)) on Y = 126/255, then BT.709 limited
        filters[FLFilters.Contrast].Value = 50;
        float y = 126 / 255f, ss = y * y * (3 - 2 * y), yc = y + (MathF.Pow(y, 0.5f) - y) * ss;
        int expectedContrast = (int)MathF.Round(1.16438356f * (yc - 0.0625f) * 255);
        grey.WaitFrame((px, w, h) => Near(Center(px, w, h), (expectedContrast, expectedContrast, expectedContrast), 4), because: $"contrast +50 (~{expectedContrast})");
        filters[FLFilters.Contrast].Value = 0;
        grey.WaitFrame((px, w, h) => Near(Center(px, w, h), (128, 128, 128), 3), because: "back to defaults");
        grey.Dispose();

        using Session red = new(PortableVideoTestMedia.Solid("C04040"), 320, 180);
        var rf = red.Config.Video.FLFilters;
        var plain = red.WaitFrame((px, w, h) => Center(px, w, h).Item1 > 150, because: "unfiltered red");
        var (pr, pg, pb) = Center(plain.Px, plain.W, plain.H);

        // Saturation -100 -> 0: BT.709 luma grey
        rf[FLFilters.Saturation].Value = -100;
        int lum = (int)MathF.Round(0.2126f * pr + 0.7152f * pg + 0.0722f * pb);
        red.WaitFrame((px, w, h) => Near(Center(px, w, h), (lum, lum, lum), 3), because: $"saturation -100 (grey {lum})");
        rf[FLFilters.Saturation].Value = 0;

        // Hue +90 degrees -> 1.57 rad through the shader's hue matrix
        rf[FLFilters.Hue].Value = 90;
        var e = PortableVideoPipelineUnitTests.ShaderTail([pr / 255f, pg / 255f, pb / 255f], 0, Utils.Scale(90, -180, 180, -3.14f, 3.14f), 1);
        var expectedHue = ((int)MathF.Round(e[0] * 255), (int)MathF.Round(e[1] * 255), (int)MathF.Round(e[2] * 255));
        red.WaitFrame((px, w, h) => Near(Center(px, w, h), expectedHue, 3), because: $"hue +90 {expectedHue}");
        rf[FLFilters.Hue].Value = 0;

        red.WaitFrame((px, w, h) => Near(Center(px, w, h), (pr, pg, pb), 1), because: "defaults again: unfiltered");
    }

    static double Combing(byte[] px, int w, int h)
    {   // Mean |row(y) - avg(row(y-1), row(y+1))| of the green channel: high for woven fields of moving content
        double sum = 0;
        for (int yy = 1; yy < h - 1; yy++)
            for (int x = 0; x < w; x++)
            {
                int c = px[(yy * w + x) * 4 + 1], a = px[((yy - 1) * w + x) * 4 + 1], b = px[((yy + 1) * w + x) * 4 + 1];
                sum += Math.Abs(c - (a + b) / 2.0);
            }
        return sum / ((h - 2) * w);
    }

    [Fact]
    public void Deinterlace_RemovesCombing_ReusesTheGraphWhilePlaying_AndDoubleRateShowsBothFields()
    {
        using Session s = new(PortableVideoTestMedia.Interlaced, 320, 240);

        s.Renderer.FieldType.Should().Be(VideoFrameFormat.InterlacedTopFieldFirst, "DeInterlace Auto follows the stream's field order");
        var deinterlaced = s.WaitFrame((px, w, h) => w == 320 && h == 240);
        double combed0 = Combing(deinterlaced.Px, 320, 240);

        int n = s.Surface.Frames;
        s.Config.Video.DeInterlace = DeInterlace.Progressive;
        WaitFor(() => s.Renderer.FieldType == VideoFrameFormat.Progressive).Should().BeTrue("the render loop applies the request");
        var woven = s.WaitFrame((px, w, h) => true, after: n, because: "re-rendered without deinterlacing");
        double combedRaw = Combing(woven.Px, 320, 240);

        combed0.Should().BeLessThan(combedRaw * 0.5, $"bwdif must remove most of the combing (deinterlaced {combed0:F2}, woven {combedRaw:F2})");

        // Double rate: both fields of the same frame are presented and differ (moving content)
        s.Config.Video.DeInterlace = DeInterlace.Auto;
        s.Config.Video.DoubleRate  = true;
        n = s.Surface.Frames;
        s.WaitFrame((px, w, h) => true, after: n, because: "re-rendered with deinterlacing");

        var frame = s.Renderer.Frames.RendererFrame;
        frame.Should().NotBeNull();
        s.Renderer.RenderPlay(frame, false).Should().BeTrue();
        s.Renderer.PresentPlay();
        var first = s.Surface.Snapshot().Pixels;
        s.Renderer.RenderPlay(frame, true).Should().BeTrue();
        s.Renderer.PresentPlay();
        var second = s.Surface.Snapshot().Pixels;

        double diff = first.Zip(second, (a, b) => Math.Abs(a - b)).Average();
        diff.Should().BeGreaterThan(1, "the second field is a different point in time");
        Combing(second, 320, 240).Should().BeLessThan(combedRaw * 0.5);

        // Sequential playback keeps one running graph (one push per frame) instead of rebuilding it
        s.Config.Video.DoubleRate = false;
        int graphsBefore = s.Renderer.Preprocessor.DeinterlaceGraphsCreated;
        n = s.Surface.Frames;
        s.Player.Play();
        WaitFor(() => s.Surface.Frames >= n + 20).Should().BeTrue("playback presents frames");
        s.Player.Pause();
        (s.Renderer.Preprocessor.DeinterlaceGraphsCreated - graphsBefore).Should().BeLessThanOrEqualTo(3, "sequential frames reuse the running deinterlacer");
    }

    [Fact]
    public void HdrPq_IsToneMappedToSdr()
    {
        using Session s = new(PortableVideoTestMedia.HdrGrey, 320, 180);
        Assert.SkipUnless(SoftwareFramePreprocessor.CanToneMap, "zscale/tonemap filters not available in this FFmpeg build");

        s.Player.VideoDemuxer.VideoStream.HDRFormat.Should().Be(HDRFormat.HDR);
        // Without tone mapping the PQ code value (~1000 nits) reads as ~193 grey; hable over a 200-nit SDR peak is brighter
        var f = s.WaitFrame((px, w, h) => CapturingSurface.Average(px, w, h, w / 2, h / 2).G > 215, because: "tone mapped (hable)");
        var c = CapturingSurface.Average(f.Px, f.W, f.H, f.W / 2, f.H / 2);
        Math.Abs(c.R - c.B).Should().BeLessThan(20, "a neutral grey stays neutral");
        s.Renderer.Preprocessor.HDRGraphsCreated.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ResolutionChange_MidStream_IsFollowed()
    {
        using Session s = new(PortableVideoTestMedia.ResolutionSwitch, 1280, 720);

        s.Player.Play();
        WaitFor(() => s.Surface.SawSize(640, 360) && s.Surface.SawSize(1280, 720)).Should().BeTrue("both segments are presented at their native sizes");
        WaitFor(() => s.Renderer.VisibleWidth == 1280).Should().BeTrue();
    }

    [Fact]
    public void SeekWhilePaused_PresentsTheNewFrame()
    {
        using Session s = new(PortableVideoTestMedia.RedThenBlue, 320, 180);
        (int, int, int) Center(byte[] px, int w, int h) => CapturingSurface.Average(px, w, h, w / 2, h / 2);

        s.WaitFrame((px, w, h) => Near(Center(px, w, h), Red, 40), because: "first frame (red)");
        s.Player.Status.Should().NotBe(Status.Playing);

        int n = s.Surface.Frames;
        s.Player.Seek(1600);
        s.WaitFrame((px, w, h) => Near(Center(px, w, h), Blue, 40), after: n, because: "after seeking to 1.6 s (blue)");

        n = s.Surface.Frames;
        s.Player.Seek(200);
        s.WaitFrame((px, w, h) => Near(Center(px, w, h), Red, 40), after: n, because: "after seeking back to 0.2 s (red)");
        s.Player.Status.Should().NotBe(Status.Playing);
    }

    [Fact]
    public void StopAndDispose_ClearTheSurface_AndReleaseNativeResources()
    {
        Session s = new(PortableVideoTestMedia.Interlaced, 1000, 1000, c => c.Video.Rotation = 90);
        try
        {
            s.Config.Video.FLFilters[FLFilters.Brightness].Value = 10;
            int n = s.Surface.Frames;
            s.WaitFrame((px, w, h) => w == 240 && h == 320, after: n - 1);

            int clears = s.Surface.Clears;
            s.Player.Stop();
            WaitFor(() => s.Surface.Clears > clears).Should().BeTrue("Stop clears the surface");

            n = s.Surface.Frames;
            var args = s.Player.Open(PortableVideoTestMedia.Quad);
            args.Success.Should().BeTrue(args.Error);
            s.Player.Seek(0);
            s.WaitFrame((px, w, h) => w == 180 && h == 320, after: n, because: "reopened");

            var renderer = s.Renderer;
            clears = s.Surface.Clears;
            s.Player.Dispose();
            WaitFor(() => s.Surface.Clears > clears).Should().BeTrue("Dispose clears the surface");
            renderer.HasNativeVideoResources.Should().BeFalse("Dispose frees the SwsContext, filter graphs and buffers");
        }
        finally
        {
            Utils.IsTesting = true;
        }
    }

    [Fact]
    public void Snapshot_KeepsNativeResolution_WhilePresentationIsDownscaled()
    {
        using Session s = new(PortableVideoTestMedia.Hd, 640, 360);
        s.WaitFrame((px, w, h) => w == 640 && h == 360, because: "presentation at the viewport size");

        string png = Path.Combine(Path.GetTempPath(), $"llplayer-snap-{Guid.NewGuid():N}.png");
        string small = Path.Combine(Path.GetTempPath(), $"llplayer-snap-{Guid.NewGuid():N}.png");
        try
        {
            s.Renderer.TakeSnapshotToFile(png).Should().BeTrue();
            ReadPngSize(png).Should().Be((1280, 720));

            s.Renderer.TakeSnapshotToFile(small, 320).Should().BeTrue();
            ReadPngSize(small).Should().Be((320, 180));
        }
        finally
        {
            File.Delete(png);
            File.Delete(small);
        }
    }

    sealed class NullSurface : IVideoSurface
    {
        public int Frames;
        public void PresentFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride) => Frames++;
        public void ClearFrame() { }
    }

    [Fact]
    public void Conversion_StaysWithinTheAllocationBudget_AndReportsMsPerFrame()
    {
        var output = TestContext.Current.TestOutputHelper;
        foreach (var (media, label, nw, nh) in new[] { (PortableVideoTestMedia.Hd, "720p", 1280, 720), (PortableVideoTestMedia.FullHd, "1080p", 1920, 1080) })
        {
            using Session s = new(media, nw, nh);
            NullSurface surface = new();
            s.Renderer.Surface = surface;

            List<VideoFrame> frames = [];
            try
            {
                var vd = s.Player.VideoDecoder;
                var f0 = vd.GetFrame(0);
                f0.Should().NotBeNull();
                frames.Add(f0);
                while (frames.Count < 40 && vd.GetFrameNext() is { } f)
                    frames.Add(f);
                frames.Count.Should().BeGreaterThanOrEqualTo(30);

                foreach (var (name, cw, ch, configure) in new (string, int, int, Action<Config>)[]
                {
                    ("native",              nw,     nh,     _ => { }),
                    ("half (downscale)",    nw / 2, nh / 2, _ => { }),
                    ("native + filters",    nw,     nh,     c => c.Video.FLFilters[FLFilters.Saturation].Value = 30),
                    ("native + rotate 90",  nh,     nw,     c => { c.Video.FLFilters[FLFilters.Saturation].Value = 0; c.Video.Rotation = 90; }),
                })
                {
                    configure(s.Config);
                    s.Renderer.SetControlSize(cw, ch);

                    for (int i = 0; i < 30; i++) // warm-up: JIT tiering, context and buffers
                        { s.Renderer.RenderPlay(frames[i % frames.Count], false); s.Renderer.PresentPlay(); }

                    int presented = surface.Frames, failed = 0;
                    long bytes = GC.GetAllocatedBytesForCurrentThread();
                    var sw = Stopwatch.StartNew();
                    foreach (var f in frames) // (no assertions inside the measured loop: they allocate)
                    {
                        if (!s.Renderer.RenderPlay(f, false))
                            failed++;
                        s.Renderer.PresentPlay();
                    }
                    sw.Stop();
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    failed.Should().Be(0);
                    (surface.Frames - presented).Should().BeGreaterThanOrEqualTo(frames.Count);

                    double ms = sw.Elapsed.TotalMilliseconds / frames.Count;
                    output?.WriteLine($"{label} {name}: {ms:F2} ms/frame -> {s.Renderer.PresentedWidth}x{s.Renderer.PresentedHeight}, {allocated} B managed over {frames.Count} frames");
                    (allocated / frames.Count).Should().BeLessThan(1024, $"{label} {name}: no per-frame managed allocations above 1 KB");
                }
            }
            finally
            {
                s.Renderer.ClearScreen(force: true); // drop the renderer's reference before freeing our frames
                foreach (var f in frames)
                    f.Dispose();
            }
        }
    }

    [Fact]
    public void OpenDispose20Times_DoesNotGrowNativeHandlesOrMemory()
    {
        PortableVideoTestMedia.RequireEngine();
        string[] media = [PortableVideoTestMedia.Interlaced, PortableVideoTestMedia.Hd];
        var output = TestContext.Current.TestOutputHelper;

        int fds5 = 0; long managed5 = 0, rss5 = 0;
        for (int i = 1; i <= 20; i++)
        {
            Session s = new(media[i % 2], 640, 360, c =>
            {
                c.Video.Rotation = 90;
                c.Video.HFlip    = i % 3 == 0;
            });
            try
            {
                s.Config.Video.FLFilters[FLFilters.Contrast].Value = 20;
                int n = s.Surface.Frames;
                s.Player.Play();
                WaitFor(() => s.Surface.Frames >= n + 5).Should().BeTrue("a few frames are played");
                var renderer = s.Renderer;
                s.Player.Dispose();
                renderer.HasNativeVideoResources.Should().BeFalse($"iteration {i}: Dispose frees conversion contexts, graphs and buffers");
            }
            finally
            {
                Utils.IsTesting = true;
            }

            if (i == 5 || i == 20)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                int fds = OperatingSystem.IsLinux() ? Directory.GetFiles("/proc/self/fd").Length : 0;
                long managed = GC.GetTotalMemory(true);
                long rss = Process.GetCurrentProcess().WorkingSet64;
                output?.WriteLine($"after {i}: fds {fds}, managed {managed / 1024} KiB, rss {rss / 1024 / 1024} MiB");

                if (i == 5)
                    (fds5, managed5, rss5) = (fds, managed, rss);
                else
                {
                    (fds - fds5).Should().BeLessThanOrEqualTo(4, "no file-descriptor growth over 15 open/dispose cycles");
                    (managed - managed5).Should().BeLessThan(16L << 20, "no managed memory growth trend");
                    (rss - rss5).Should().BeLessThan(96L << 20, "no native memory growth trend (a leaked 720p BGRA buffer per cycle would be ~55 MB)");
                }
            }
        }
    }

    #region Image file helpers
    static (int W, int H) ReadPngSize(string file)
    {
        var b = File.ReadAllBytes(file);
        b.AsSpan(1, 3).SequenceEqual("PNG"u8).Should().BeTrue();
        return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
    }

    static (byte[] Bgra, int W, int H) ReadBmp(string file)
    {   // 24-bit BI_RGB, bottom-up (FFmpeg's bmp encoder)
        var b = File.ReadAllBytes(file);
        int offset = BitConverter.ToInt32(b, 10), w = BitConverter.ToInt32(b, 18), h = BitConverter.ToInt32(b, 22);
        BitConverter.ToInt16(b, 28).Should().Be(24);
        bool bottomUp = h > 0;
        h = Math.Abs(h);
        int rowSize = (w * 3 + 3) & ~3;
        byte[] px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int row = offset + (bottomUp ? h - 1 - y : y) * rowSize;
            for (int x = 0; x < w; x++)
            {
                px[(y * w + x) * 4]     = b[row + x * 3];
                px[(y * w + x) * 4 + 1] = b[row + x * 3 + 1];
                px[(y * w + x) * 4 + 2] = b[row + x * 3 + 2];
                px[(y * w + x) * 4 + 3] = 255;
            }
        }
        return (px, w, h);
    }
    #endregion
}
