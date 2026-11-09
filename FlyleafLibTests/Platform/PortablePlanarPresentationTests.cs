using System.Reflection;
using AwesomeAssertions;
using Flyleaf.FFmpeg;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using static Flyleaf.FFmpeg.Raw;

namespace FlyleafLib.Platform;

[Collection("PortableEngine")]
public unsafe class PortablePlanarPresentationTests
{
    static readonly MethodInfo Convert = typeof(Renderer).GetMethod("ConvertFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo Present = typeof(Renderer).GetMethod("PresentLocal", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static object RenderGate(Renderer renderer) => typeof(Renderer).GetField("lockRenderLoops", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;

    sealed class Surface : IVideoPlanarSurface
    {
        public bool Ready = true;
        public int PlanarCalls, BgraCalls;
        public byte FirstY, FirstB;
        public bool CanPresentPlanar(PlanarVideoPixelFormat format) => Ready;
        public bool TryPresentPlanar(in PlanarVideoFrame frame, in VideoPresentationParameters parameters)
        {
            if (!Ready) return false;
            PlanarCalls++;
            FirstY = frame.Plane0.GetRow(0)[0];
            return true;
        }
        public void PresentFrame(ReadOnlySpan<byte> bytes, int width, int height, int stride) { BgraCalls++; FirstB = bytes[0]; }
        public void ClearFrame() { }
    }

    static VideoFrame Picture(byte luma, int pictureWidth = 5, int pictureHeight = 3)
    {
        var frame = av_frame_alloc();
        frame->format = (int)AVPixelFormat.Yuv420p;
        frame->width = pictureWidth;
        frame->height = pictureHeight;
        av_frame_get_buffer(frame, 32).Should().BeGreaterThanOrEqualTo(0);
        for (int plane = 0; plane < 3; plane++)
        {
            int width = plane == 0 ? pictureWidth : (pictureWidth + 1) / 2, height = plane == 0 ? pictureHeight : (pictureHeight + 1) / 2;
            for (int row = 0; row < height; row++)
                new Span<byte>((byte*)frame->data[plane] + row * frame->linesize[plane], width).Fill(plane == 0 ? luma : (byte)128);
        }
        return new VideoFrame { AVFrame = frame };
    }

    [Fact]
    public void Early_prepare_keeps_one_owned_picture_until_timed_present_and_bypasses_BGRA()
    {
        PortableVideoTestMedia.RequireEngine();
        using Player player = new(new Config());
        var renderer = player.Renderer;
        var surface = new Surface();
        var picture = Picture(81);
        try
        {
            lock (RenderGate(renderer))
            {
                renderer.Surface = surface;
                Convert.Invoke(renderer, [picture, false]).Should().Be(true);
                surface.PlanarCalls.Should().Be(0, "preparation is earlier than the playback presentation deadline");
                surface.BgraCalls.Should().Be(0);
                renderer.HasPendingPlanarFrame.Should().BeTrue();
                renderer.SoftwareConversionCount.Should().Be(0);
                picture.Dispose();
                Present.Invoke(renderer, null);
                surface.PlanarCalls.Should().Be(1);
                surface.FirstY.Should().Be(81);
                surface.BgraCalls.Should().Be(0);
                renderer.ClearScreen(true);
                renderer.HasPendingPlanarFrame.Should().BeFalse();
            }
        }
        finally { picture.Dispose(); }
    }

    [Fact]
    public void Context_loss_between_prepare_and_present_converts_that_same_owned_picture()
    {
        PortableVideoTestMedia.RequireEngine();
        using Player player = new(new Config());
        var renderer = player.Renderer;
        var surface = new Surface();
        var picture = Picture(81);
        try
        {
            lock (RenderGate(renderer))
            {
                renderer.Surface = surface;
                Convert.Invoke(renderer, [picture, false]).Should().Be(true);
                picture.Dispose();
                surface.Ready = false;
                Present.Invoke(renderer, null);
                surface.PlanarCalls.Should().Be(0);
                surface.BgraCalls.Should().Be(1);
                surface.FirstB.Should().BeInRange((byte)70, (byte)80);
                renderer.SoftwareConversionCount.Should().Be(1);
            }
        }
        finally { picture.Dispose(); }
    }

    [Fact]
    public void Snapshot_reuse_of_deinterlacer_cannot_replace_the_prepared_planar_picture()
    {
        PortableVideoTestMedia.RequireEngine();
        Assert.SkipWhen(string.IsNullOrEmpty(SoftwareFramePreprocessor.DeinterlaceFilter) || !SoftwareFilterGraph.IsFilterAvailable("estdif"),
            "This native FFmpeg build has no lone-frame deinterlacer.");
        using Player player = new(new Config());
        var renderer = player.Renderer;
        var surface = new Surface();
        var picture = Picture(81, 32, 32);
        picture.Id = 1;
        var snapshotPicture = Picture(180, 32, 32);
        snapshotPicture.Id = 2;
        string snapshot = Path.Combine(Path.GetTempPath(), $"llplayer-planar-{Guid.NewGuid():N}.bmp");
        try
        {
            lock (RenderGate(renderer))
            {
                renderer.Surface = surface;
                typeof(Renderer).GetProperty(nameof(Renderer.FieldType))!.SetValue(renderer, VideoFrameFormat.InterlacedTopFieldFirst);
                Convert.Invoke(renderer, [picture, false]).Should().Be(true);
                renderer.Preprocessor.SpatialFallbacks.Should().BeGreaterThan(0);
                picture.Dispose();
                renderer.TakeSnapshotToFile(snapshot, frame: snapshotPicture).Should().BeTrue();
                new FileInfo(snapshot).Length.Should().BeGreaterThan(0);
                Present.Invoke(renderer, null);
                surface.PlanarCalls.Should().Be(1);
                surface.FirstY.Should().Be(81);
                renderer.SoftwareConversionCount.Should().Be(0);
                renderer.ClearScreen(true);
            }
        }
        finally
        {
            picture.Dispose();
            snapshotPicture.Dispose();
            File.Delete(snapshot);
        }
    }

    [Fact]
    public void Nondefault_nonlinear_color_controls_preserve_the_existing_CPU_filter()
    {
        PortableVideoTestMedia.RequireEngine();
        using Player player = new(new Config());
        var renderer = player.Renderer;
        var surface = new Surface();
        var picture = Picture(81);
        try
        {
            lock (RenderGate(renderer))
            {
                renderer.Surface = surface;
                var filter = SoftwareColorFilter.Create(0, 1.5f, 0, 1, ColorType.YUV, ColorSpace.Bt709, ColorRange.Limited);
                filter!.UsesLumaCurve.Should().BeTrue();
                typeof(Renderer).GetField("colorFilter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, filter);
                Convert.Invoke(renderer, [picture, false]).Should().Be(true);
                Present.Invoke(renderer, null);
                renderer.HasPendingPlanarFrame.Should().BeFalse();
                surface.PlanarCalls.Should().Be(0);
                surface.BgraCalls.Should().Be(1);
                surface.FirstB.Should().BeGreaterThan(80);
            }
        }
        finally { picture.Dispose(); }
    }
}
