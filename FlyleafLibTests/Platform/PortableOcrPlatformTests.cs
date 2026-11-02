using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform;

public class PortableOcrPlatformTests
{
    [Fact]
    public void WindowsLoader_LoadsOnlyAbsoluteApprovedPaths_InDependencyOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "llp-native-root");
        List<string> loaded = [];
        var handles = TesseractNative.LoadWindowsLibraries(root, path => { loaded.Add(path); return loaded.Count; },
            _ => throw new InvalidOperationException("Successful load must remain resident"));
        loaded.Should().Equal(Path.Combine(root, "runtimes", "win-x64", "leptonica-1.85.0.dll"),
            Path.Combine(root, "runtimes", "win-x64", "tesseract55.dll"));
        handles.Dependency.Should().Be((nint)1);
        handles.Library.Should().Be((nint)2);
    }

    [Fact]
    public void WindowsLoader_ReleasesDependency_WhenEngineLoadFails()
    {
        List<nint> freed = [];
        int calls = 0;
        Assert.Throws<DllNotFoundException>(() => TesseractNative.LoadWindowsLibraries(Path.GetTempPath(),
            _ => ++calls == 1 ? (nint)7 : throw new DllNotFoundException("fixture"), freed.Add));
        freed.Should().Equal((nint)7);
    }

    [Fact]
    public void Construction_DoesNotLoadNativeLibraries()
    {
        bool loaded = false;
        using TesseractOCRService service = new(new Config.SubtitlesConfig(),
            () => { loaded = true; throw new InvalidOperationException("No eager native activation"); }, () => null);
        loaded.Should().BeFalse();
    }

    [Fact]
    public void NativeInitializationFailure_KeepsCauseAndGivesCurrentPlatformAdvice()
    {
        using TesseractOCRService service = new(new Config.SubtitlesConfig(),
            () => throw new DllNotFoundException("fixture native failure"), () => null);

        service.TryInitialize(Language.English, out string error).Should().BeFalse();

        error.Should().Contain("fixture native failure");
        if (OperatingSystem.IsWindows())
            error.Should().Contain("Windows x64 Tesseract").And.Contain("Redistributable").And.NotContain("libtesseract.so");
        else
            error.Should().Contain("Check/install system Tesseract 5 (libtesseract.so.5)");
    }
}
