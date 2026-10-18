using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AwesomeAssertions;
using LLPlayer.Avalonia.Controls;

namespace LLPlayer.Avalonia.Tests;

/// <summary>VideoView: fake BGRA frames from a background thread end up on screen, letterboxed on black.</summary>
public class VideoViewTests
{
    static byte[] SolidFrame(int width, int height, int stride, byte b, byte g, byte r)
    {
        byte[] data = new byte[stride * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                data[i] = b;
                data[i + 1] = g;
                data[i + 2] = r;
                data[i + 3] = 255;
            }
        return data;
    }

    static (byte B, byte G, byte R) PixelAt(WriteableBitmap frame, int x, int y)
    {
        using ILockedFramebuffer fb = frame.Lock();
        fb.Format.Should().Be(PixelFormat.Bgra8888);
        unsafe
        {
            byte* p = (byte*)fb.Address + y * fb.RowBytes + x * 4;
            return (p[0], p[1], p[2]);
        }
    }

    static (Window Window, VideoView View) Show(int width, int height)
    {
        VideoView view = new();
        Window window = new() { Width = width, Height = height, Content = view };
        window.Show();
        return (window, view);
    }

    [AvaloniaFact]
    public void PresentFrame_FromBackgroundThread_IsDrawnLetterboxed()
    {
        var (window, view) = Show(400, 400);

        // 16:9 frame (with row padding) into a square control: bars above and below.
        const int w = 64, h = 36, stride = w * 4 + 32;
        byte[] red = SolidFrame(w, h, stride, 0, 0, 255);
        Task.Run(() => view.PresentFrame(red, w, h, stride)).Wait();

        Dispatcher.UIThread.RunJobs();
        WriteableBitmap frame = window.CaptureRenderedFrame()!;

        view.FramesPresented.Should().Be(1);
        view.FrameSize.Should().Be(new PixelSize(w, h));

        var center = PixelAt(frame, 200, 200);
        center.R.Should().BeGreaterThan(200);
        center.G.Should().BeLessThan(40);
        center.B.Should().BeLessThan(40);

        var top = PixelAt(frame, 200, 10);
        (top.R + top.G + top.B).Should().BeLessThan(30, "the letterbox area is black");

        window.Close();
    }

    [AvaloniaFact]
    public void NewFrames_ReplaceOldOnes_AndClearFrame_BlanksTheSurface()
    {
        var (window, view) = Show(320, 180);

        view.PresentFrame(SolidFrame(32, 18, 128, 0, 0, 255), 32, 18, 128);
        view.PresentFrame(SolidFrame(32, 18, 128, 255, 0, 0), 32, 18, 128); // blue
        Dispatcher.UIThread.RunJobs();
        var blue = PixelAt(window.CaptureRenderedFrame()!, 160, 90);
        blue.B.Should().BeGreaterThan(200);
        blue.R.Should().BeLessThan(40);

        // size change reallocates the buffers
        view.PresentFrame(SolidFrame(16, 9, 64, 0, 255, 0), 16, 9, 64);
        Dispatcher.UIThread.RunJobs();
        PixelAt(window.CaptureRenderedFrame()!, 160, 90).G.Should().BeGreaterThan(200);
        view.FramesPresented.Should().Be(3);

        view.ClearFrame();
        Dispatcher.UIThread.RunJobs();
        var cleared = PixelAt(window.CaptureRenderedFrame()!, 160, 90);
        (cleared.R + cleared.G + cleared.B).Should().BeLessThan(30);

        window.Close();
    }

    [AvaloniaFact]
    public void InvalidFrames_AreIgnored()
    {
        var (window, view) = Show(100, 100);
        view.PresentFrame(new byte[10], 64, 36, 256);   // too small for the declared size
        view.PresentFrame(new byte[1024], 0, 10, 0);
        view.FramesPresented.Should().Be(0);
        window.Close();
    }

    [AvaloniaFact]
    public void DestinationRect_WithoutRenderer_IsAspectFit()
    {
        VideoView view = new();
        Rect r = view.GetDestinationRect(new Rect(0, 0, 400, 400), new PixelSize(1280, 720));
        r.Width.Should().BeApproximately(400, 0.01);
        r.Height.Should().BeApproximately(225, 0.01);
        r.Y.Should().BeApproximately(87.5, 0.01);
    }
}
