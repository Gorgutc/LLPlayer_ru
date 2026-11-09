using Avalonia;
using Avalonia.Media;
using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;
using LLPlayer.Avalonia.Controls;

namespace LLPlayer.Avalonia.Tests;

public class PlanarGlFramePumpTests
{
    sealed class FakeGl : IPlanarGlApi
    {
        public string DriverDescription => "Test driver (no native acceptance)";
        public int Uploads, Disposals, Framebuffer;
        public bool Show;
        public void Upload(PlanarFrameMailbox.Frame frame) => Uploads++;
        public void Draw(int framebuffer, int width, int height, Rect destination, Color background, bool show)
        {
            Framebuffer = framebuffer;
            Show = show;
        }
        public void Dispose() => Disposals++;
    }

    static void Publish(PlanarFrameMailbox mailbox)
    {
        var frame = new PlanarVideoFrame(PlanarVideoPixelFormat.Nv12, 2, 2,
            new(new byte[4], 2, 2, 2, 2, false), new(new byte[2], 1, 1, 2, 2, false), default);
        var parameters = new VideoPresentationParameters(CropRect.Empty, false, false, false, ColorSpace.Bt709, ColorRange.Limited);
        mailbox.Publish(frame, parameters).Should().BeTrue();
    }

    [Fact]
    public void Uses_callback_framebuffer_and_cached_textures_until_clear_then_never_resurrects_picture()
    {
        PlanarFrameMailbox mailbox = new();
        PlanarGlFramePump pump = new(mailbox);
        FakeGl gl = new();
        pump.Initialize(gl);
        Publish(mailbox);
        pump.Render(37, 100, 100, _ => new Rect(0, 0, 100, 100), Colors.Black, true);
        gl.Framebuffer.Should().Be(37);
        gl.Show.Should().BeTrue();
        gl.Uploads.Should().Be(1);
        pump.Render(43, 100, 100, _ => new Rect(0, 0, 100, 100), Colors.Black, true);
        gl.Framebuffer.Should().Be(43);
        gl.Uploads.Should().Be(1);
        mailbox.Clear();
        pump.Render(43, 100, 100, _ => new Rect(0, 0, 100, 100), Colors.Black, true);
        gl.Show.Should().BeFalse();
        pump.Deinitialize();
        gl.Disposals.Should().Be(1);
        pump.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void Context_loss_drops_old_names_without_deleting_in_a_dead_context_or_replaying_old_frames()
    {
        PlanarFrameMailbox mailbox = new();
        PlanarGlFramePump pump = new(mailbox);
        FakeGl old = new(), replacement = new();
        pump.Initialize(old);
        Publish(mailbox);
        pump.LoseContext();
        old.Disposals.Should().Be(0);
        pump.Initialize(replacement);
        pump.Render(8, 100, 100, _ => default, Colors.Black, true);
        replacement.Uploads.Should().Be(0);
        replacement.Show.Should().BeFalse();
        pump.Deinitialize();
        replacement.Disposals.Should().Be(1);
    }
}
