using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;
using LLPlayer.Avalonia.Controls;

namespace LLPlayer.Avalonia.Tests;

public class PlanarVideoViewTests
{
    [AvaloniaFact]
    public void Unavailable_context_keeps_requested_OpenGL_and_visible_CPU_picture()
    {
        VideoView view = new();
        view.PresentationMode.Should().Be(PortableVideoPresentationMode.Software);
        view.CanPresentPlanar(PlanarVideoPixelFormat.Yuv420P).Should().BeFalse();
        view.PresentationMode = PortableVideoPresentationMode.OpenGL;
        Window window = new() { Width = 100, Height = 100, Content = view };
        window.Show();
        view.PresentFrame([0, 0, 255, 255], 1, 1, 4);
        Dispatcher.UIThread.RunJobs();
        view.PresentationStatus.RequestedMode.Should().Be(PortableVideoPresentationMode.OpenGL);
        view.PresentationStatus.EffectiveMode.Should().Be(PortableVideoPresentationMode.Software);
        view.PresentationStatus.UsesPlanarShader.Should().BeFalse();
        view.PresentationStatus.FallbackReason.Should().NotBeNullOrWhiteSpace();
        using var screenshot = window.CaptureRenderedFrame()!;
        using var framebuffer = screenshot.Lock();
        framebuffer.Format.Should().Be(PixelFormat.Bgra8888);
        unsafe { ((byte*)framebuffer.Address + 50 * framebuffer.RowBytes + 50 * 4)[2].Should().BeGreaterThan(200); }
        view.PresentationMode = PortableVideoPresentationMode.Software;
        view.PresentationStatus.RequestedMode.Should().Be(PortableVideoPresentationMode.Software);
        view.PresentationStatus.FallbackReason.Should().BeNull();
        window.Close();
    }

    [Theory]
    [InlineData(false, false, false, 0.2f, 0.6f)]
    [InlineData(false, true, false, 0.9f, 0.6f)]
    [InlineData(false, false, true, 0.2f, 0.1f)]
    [InlineData(true, false, false, 0.9f, 0.1f)]
    [InlineData(true, true, false, 0.9f, 0.6f)]
    [InlineData(true, false, true, 0.2f, 0.1f)]
    public void Quad_source_coordinates_apply_existing_output_flips_before_transpose(bool transpose, bool flipX, bool flipY, float expectedU, float expectedV)
    {
        var parameters = new VideoPresentationParameters(new CropRect(1, 2, 4, 1), transpose, flipX, flipY, ColorSpace.Bt709, ColorRange.Limited);
        Span<float> vertices = stackalloc float[16];
        PlanarTextureGeometry.WriteVertices(vertices, 10, 10, parameters);
        // First vertex is the output bottom-left, source crop spans x=.2..9 and y=.1..6.
        vertices[0].Should().Be(-1);
        vertices[1].Should().Be(-1);
        vertices[2].Should().BeApproximately(expectedU, 0.00001f);
        vertices[3].Should().BeApproximately(expectedV, 0.00001f);
        PlanarTextureGeometry.VisibleSize(10, 10, parameters).Should().Be(transpose ? new PixelSize(5, 7) : new PixelSize(7, 5));
    }

    [Theory]
    [InlineData(false, false, "#version 120", "texture2D", ".ra")]
    [InlineData(true, false, "#version 100", "texture2D", ".ra")]
    [InlineData(false, true, "#version 130", "texture", ".rg")]
    [InlineData(true, true, "#version 300 es", "texture", ".rg")]
    public void Shader_profile_matches_actual_single_and_dual_channel_texture_layout(bool es, bool modern, string version, string sample, string pair)
    {
        var sources = PlanarShaderSources.Create(es, modern);
        sources.Vertex.Should().StartWith(version);
        sources.Fragment.Should().StartWith(version);
        sources.Fragment.Should().Contain(sample + "(uU,p)" + pair);
        sources.Fragment.Should().Contain("128.0/255.0");
        sources.Fragment.Should().NotContain("pow(", "nondefault nonlinear color controls use the existing CPU color filter");
    }
}
