using AwesomeAssertions;

namespace FlyleafLib;

// Utils exposes one-line color converters between the four color types the player juggles: System.Drawing
// (WinForms), System.Windows.Media (WPF), Vortice.Mathematics (Direct3D resources) and Vortice.Direct3D11's
// VideoColor (video-processor float RGBA). The test project targets net10.0-windows so all four types load.
// These are channel-preserving conversions; the only arithmetic is VideoColor's byte/255 normalization.
public class UtilsColorConversionTests
{
    [Fact]
    public void WinFormsToWPFColor_PreservesArgbChannels()
    {
        System.Windows.Media.Color wpf = Utils.WinFormsToWPFColor(System.Drawing.Color.FromArgb(255, 200, 100, 50));

        wpf.A.Should().Be(255);
        wpf.R.Should().Be(200);
        wpf.G.Should().Be(100);
        wpf.B.Should().Be(50);
    }

    [Fact]
    public void WPFToWinFormsColor_PreservesArgbChannels()
    {
        System.Drawing.Color win = Utils.WPFToWinFormsColor(
            System.Windows.Media.Color.FromArgb(128, 10, 20, 30));

        win.A.Should().Be(128);
        win.R.Should().Be(10);
        win.G.Should().Be(20);
        win.B.Should().Be(30);
    }

    [Fact]
    public void WinFormsWpf_RoundTrips()
    {
        System.Drawing.Color original = System.Drawing.Color.FromArgb(64, 1, 2, 3);

        System.Drawing.Color roundTripped = Utils.WPFToWinFormsColor(Utils.WinFormsToWPFColor(original));

        roundTripped.A.Should().Be(original.A);
        roundTripped.R.Should().Be(original.R);
        roundTripped.G.Should().Be(original.G);
        roundTripped.B.Should().Be(original.B);
    }

    [Fact]
    public void VorticeToWPFColor_PreservesArgbChannels()
    {
        // Vortice.Mathematics.Color ctor order is (R, G, B, A).
        var vortice = new Vortice.Mathematics.Color((byte)200, (byte)100, (byte)50, (byte)255);

        System.Windows.Media.Color wpf = Utils.VorticeToWPFColor(vortice);

        wpf.A.Should().Be(255);
        wpf.R.Should().Be(200);
        wpf.G.Should().Be(100);
        wpf.B.Should().Be(50);
    }

    [Fact]
    public void WPFToVorticeColor_PreservesChannels()
    {
        Vortice.Mathematics.Color vortice = Utils.WPFToVorticeColor(
            System.Windows.Media.Color.FromArgb(255, 200, 100, 50));

        vortice.R.Should().Be(200);
        vortice.G.Should().Be(100);
        vortice.B.Should().Be(50);
        vortice.A.Should().Be(255);
    }

    [Fact]
    public void WPFToVideoColor_NormalizesChannelsToUnitFloats()
    {
        Vortice.Direct3D11.VideoColor video = Utils.WPFToVideoColor(
            System.Windows.Media.Color.FromArgb(255, 255, 0, 0));

        video.Rgba.R.Should().BeApproximately(1.0f, 0.0001f);
        video.Rgba.G.Should().BeApproximately(0.0f, 0.0001f);
        video.Rgba.B.Should().BeApproximately(0.0f, 0.0001f);
        video.Rgba.A.Should().BeApproximately(1.0f, 0.0001f);
    }

    [Fact]
    public void WPFToVideoColor_MidGray_IsAboutHalf()
    {
        Vortice.Direct3D11.VideoColor video = Utils.WPFToVideoColor(
            System.Windows.Media.Color.FromArgb(128, 128, 128, 128));

        // 128 / 255 ≈ 0.50196 — all four channels go through the same normalization.
        video.Rgba.R.Should().BeApproximately(128f / 255f, 0.0001f);
        video.Rgba.G.Should().BeApproximately(128f / 255f, 0.0001f);
        video.Rgba.B.Should().BeApproximately(128f / 255f, 0.0001f);
        video.Rgba.A.Should().BeApproximately(128f / 255f, 0.0001f);
    }
}
