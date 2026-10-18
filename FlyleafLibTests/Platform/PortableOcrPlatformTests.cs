using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform;

public class PortableOcrPlatformTests
{
    [Fact]
    public void WindowsRealNativeLoader_ReportsUnsupportedPlaybackBaselineWithWpfFallback()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows Avalonia playback-baseline diagnostic.");
        // Detached settings avoid process-global engine/UI initialization; the real loader still runs.
        using TesseractOCRService service = new(new Config.SubtitlesConfig(), () => TesseractNative.Shared, () => null);

        service.TryInitialize(Language.English, out string error).Should().BeFalse();

        error.Should().Contain("Windows Avalonia").And.Contain("not supported")
            .And.Contain("playback baseline").And.Contain("WPF");
        error.Should().NotContain("libtesseract.so").And.NotContain("Ubuntu");
    }

    [Fact]
    public void NativeInitializationFailure_KeepsCauseAndGivesCurrentPlatformAdvice()
    {
        using TesseractOCRService service = new(new Config.SubtitlesConfig(),
            () => throw new DllNotFoundException("fixture native failure"), () => null);

        service.TryInitialize(Language.English, out string error).Should().BeFalse();

        error.Should().Contain("fixture native failure");
        if (OperatingSystem.IsWindows())
            error.Should().Contain("WPF").And.NotContain("libtesseract.so").And.NotContain("Ubuntu");
        else
            error.Should().Contain("Check/install system Tesseract 5 (libtesseract.so.5)");
    }
}
