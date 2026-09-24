using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Controls;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

/// <summary>
/// End-to-end over the real engine (env-gated like FlyleafLibTests' PortableEngineSmokeTests):
///   LLPLAYER_FFMPEG_DIR = folder with the FFmpeg 8 shared libraries
///   LLPLAYER_TEST_MEDIA = test-720p.mp4 (12 s testsrc2 + sine) with test-720p.srt next to it
/// Opens the clip in the real main window, plays, and checks that decoded frames reach the VideoView and that the
/// first cue of the .srt ("Hello world, this is a test.", 0.5-3.0 s) is shown by the overlay at ~1 s.
/// </summary>
[Collection(FlyleafGlobalsCollection.Name)]
public class EndToEndTests
{
    static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout)
                return false;
            await Task.Delay(25);
        }
        return true;
    }

    /// <summary>
    /// Samples a grid inside the renderer's viewport and checks that the captured window shows the VideoView's current
    /// frame at the same relative positions (scaled only: no extra rotation or mirroring).
    /// </summary>
    static void AssertScreenShowsPresentedFrame(MainWindow window)
    {
        VideoView view = window.VideoSurface;
        WriteableBitmap presented = view.CurrentFrame!;
        presented.Should().NotBeNull();
        Viewport vp = view.Renderer!.Viewport;
        Point origin = view.TranslatePoint(new Point(0, 0), window)!.Value;
        WriteableBitmap screen = window.CaptureRenderedFrame()!;

        int matches = 0, total = 0;
        using ILockedFramebuffer src = presented.Lock();
        using ILockedFramebuffer dst = screen.Lock();
        for (double v = 0.12; v < 0.72; v += 0.06)          // stay above the subtitle overlay
            for (double u = 0.12; u < 0.90; u += 0.06)
            {
                int fx = (int)(u * src.Size.Width), fy = (int)(v * src.Size.Height);
                int sx = (int)(origin.X + vp.X + u * vp.Width), sy = (int)(origin.Y + vp.Y + v * vp.Height);
                unsafe
                {
                    byte* a = (byte*)src.Address + fy * src.RowBytes + fx * 4;
                    byte* b = (byte*)dst.Address + sy * dst.RowBytes + sx * 4;
                    int diff = Math.Max(Math.Abs(a[0] - b[0]), Math.Max(Math.Abs(a[1] - b[1]), Math.Abs(a[2] - b[2])));
                    if (diff <= 48)
                        matches++;
                }
                total++;
            }

        matches.Should().BeGreaterThanOrEqualTo(total * 8 / 10,
            $"the screen must show the presented frame untransformed ({matches}/{total} grid samples matched)");
    }

    static string LogTail(AppPaths paths)
    {
        try
        {
            return string.Join("\n", File.ReadLines(paths.EngineLogFile).Where(l => !l.Contains("Frame Dropped") && !l.Contains("| Trace")).TakeLast(60));
        }
        catch (IOException)
        {
            return "(no engine log)";
        }
    }

    [AvaloniaFact]
    public async Task RealMedia_Plays_FramesReachVideoView_AndSubtitleAppears()
    {
        string? ffmpegDir = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpegDir), "LLPLAYER_FFMPEG_DIR is not set (folder with the FFmpeg 8 shared libraries).");
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrEmpty(media), "LLPLAYER_TEST_MEDIA is not set (path of test-720p.mp4).");
        string srt = Path.ChangeExtension(media!, ".srt");
        Assert.SkipUnless(File.Exists(media) && File.Exists(srt), $"{media} or {srt} does not exist.");
        FFmpegLocator.Locate(ffmpegDir).IsValid.Should().BeTrue();

        using TempDir state = new();
        AppPaths paths = new(Path.Combine(state.Path, "config"), Path.Combine(state.Path, "state"));
        KeyMapper keys = new();
        AvaloniaUIDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        MainWindow? window = null;
        AvaloniaHostServices host = new(() => window);
        EngineBootstrap.InstallSeams(dispatcher, host, bridge, keys);
        if (!Engine.IsLoaded)
            EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpegDir!, paths));

        AppPrefs prefs = new AppPrefs { TranslateTargetLanguage = "Russian" }.Normalize();
        Config config = EngineBootstrap.CreatePlayerConfig(prefs);
        // the app's player config mirrors the WPF app's defaults (needs the engine: Config() reads Engine.Plugins)
        config.Subtitles.SearchLocal.Should().BeTrue();
        config.Demuxer.FormatOptToUnderlying.Should().BeTrue();
        config.Player.UICurTime.Should().Be(UIRefreshType.PerUIRefreshInterval);
        config.Subtitles.TranslateTargetLanguage.ToString().Should().Be("Russian");

        // Deterministic and fast: load the .srt explicitly instead of the local search + Lingua language detection.
        config.Subtitles.SearchLocal = false;
        config.Subtitles.LanguageAutoDetect = false;

        Player player = new(config);
        FlyleafPlaybackController controller = new(player, dispatcher, bridge);
        window = new MainWindow(keys, host) { Width = 960, Height = 600 };
        MainWindowViewModel vm = new(controller, prefs, _ => { }, new FakeTranslator(), window);
        try
        {
            window.Attach(vm, player.Renderer);
            player.Host = window;
            window.Show();

            vm.Open(media!, [srt]);

            (await WaitUntil(() => vm.IsOpened && vm.IsPlaying, TimeSpan.FromSeconds(30))).Should().BeTrue("the clip opens and auto-plays");
            (await WaitUntil(() => vm.PrimaryText == "Hello world, this is a test.", TimeSpan.FromSeconds(30)))
                .Should().BeTrue($"the first cue is displayed (text now '{vm.PrimaryText}', time {vm.CurrentTimeText}; toasts: "
                                 + string.Join(" | ", vm.Toasts.Select(t => $"{t.Title}: {t.Message}")) + "; engine log tail:\n" + LogTail(paths));

            TimeSpan shownAt = TimeSpan.FromTicks(player.CurTime);
            shownAt.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.4)).And.BeLessThan(TimeSpan.FromSeconds(3.0));
            window.PrimarySubtitle.IsVisible.Should().BeTrue();

            (await WaitUntil(() => window.VideoSurface.FramesPresented >= 15, TimeSpan.FromSeconds(20))).Should().BeTrue("decoded frames reach the VideoView");
            // Portable renderer contract: frames arrive at min(native, viewport) per axis (the 960x600 window's
            // viewport is smaller than the 1280x720 clip) and the VideoView shows exactly what the renderer presented.
            Viewport viewport = player.Renderer.Viewport;
            window.VideoSurface.FrameSize.Should().Be(new PixelSize(player.Renderer.PresentedWidth, player.Renderer.PresentedHeight));
            window.VideoSurface.FrameSize.Width.Should().BeLessThanOrEqualTo(Math.Min(1280, (int)Math.Ceiling(viewport.Width)));
            window.VideoSurface.FrameSize.Height.Should().BeLessThanOrEqualTo(Math.Min(720, (int)Math.Ceiling(viewport.Height)));

            // the video area is not black any more (testsrc2 colour bars)
            WriteableBitmap frame = window.CaptureRenderedFrame()!;
            using (ILockedFramebuffer fb = frame.Lock())
            {
                int bright = 0;
                unsafe
                {
                    for (int x = 100; x < 860; x += 20)
                    {
                        byte* p = (byte*)fb.Address + 200 * fb.RowBytes + x * 4;
                        if (p[0] + p[1] + p[2] > 150)
                            bright++;
                    }
                }
                bright.Should().BeGreaterThan(10);
            }

            // the sidebar lists the three cues of the .srt
            vm.ToggleSidebar();
            (await WaitUntil(() => vm.Sidebar.Cues.Count == 3, TimeSpan.FromSeconds(10))).Should().BeTrue();
            vm.Sidebar.Cues[2].Text.Should().Be("Language learning with subtitles.");

            player.Pause();
            (await WaitUntil(() => !vm.IsPlaying, TimeSpan.FromSeconds(10))).Should().BeTrue();

            // Rotation / mirroring are applied by the renderer; the VideoView must draw the presented (already
            // upright) frame as is. Drawing it rotated / flipped again would put other pixels on screen.
            player.Config.Video.Rotation = 90;
            player.Config.Video.HFlip = true;
            (await WaitUntil(() => window.VideoSurface.FrameSize.Height > window.VideoSurface.FrameSize.Width
                                   && player.Renderer.Viewport.Height > player.Renderer.Viewport.Width, TimeSpan.FromSeconds(10)))
                .Should().BeTrue($"the rotated frame is presented while paused (frame {window.VideoSurface.FrameSize}, viewport {player.Renderer.Viewport.Width}x{player.Renderer.Viewport.Height})");
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();
            AssertScreenShowsPresentedFrame(window);
        }
        finally
        {
            window.Close();
            vm.Dispose();
            controller.Dispose();
            player.Dispose();
        }
    }
}
