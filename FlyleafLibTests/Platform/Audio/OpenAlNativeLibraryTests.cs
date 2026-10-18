using AwesomeAssertions;

namespace FlyleafLib.Platform.Audio;

// Loader path assertions never load a native library or change the process working directory.
public class OpenAlNativeLibraryTests
{
    [Fact]
    public void CurrentPlatform_LoadCandidates_AreIndependentOfTheWorkingDirectoryOnWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            OpenAlNative.LibraryNames.Should().StartWith(Path.Combine(AppContext.BaseDirectory, "soft_oal.dll"));
            OpenAlNative.LibraryNames.Should().OnlyContain(path => Path.IsPathFullyQualified(path),
                "Windows must load only from the trusted application directory or its native runtime directory");
        }
        else
        {
            OpenAlNative.LibraryNames.Should().Equal("libopenal.so.1", "libopenal.so",
                "libopenal.1.dylib", "libopenal.dylib", "soft_oal.dll", "OpenAL32.dll");
        }
    }

    [Fact]
    public void WindowsCandidates_PreferApplicationPayload_ThenItsRuntimeSubdirectory()
    {
        string appDirectory = Path.Combine(Path.GetTempPath(), "LLPlayer App");

        string[] candidates = OpenAlNative.GetLibraryCandidates(appDirectory, isWindows: true);

        candidates.Should().Equal(
            Path.Combine(appDirectory, "soft_oal.dll"),
            Path.Combine(appDirectory, "OpenAL32.dll"),
            Path.Combine(appDirectory, "runtimes", "win-x64", "native", "soft_oal.dll"),
            Path.Combine(appDirectory, "runtimes", "win-x64", "native", "OpenAL32.dll"));
        candidates.Should().OnlyContain(path => Path.IsPathFullyQualified(path));
    }

    [Fact]
    public void WindowsCandidates_NormalizeAnAbsoluteApplicationDirectory()
    {
        string parent = Path.GetPathRoot(AppContext.BaseDirectory)!;
        string appDirectory = Path.Combine(parent, "OldApp", "..", "LLPlayer");

        OpenAlNative.GetLibraryCandidates(appDirectory, isWindows: true).Should().StartWith(
            Path.Combine(parent, "LLPlayer", "soft_oal.dll"));
    }

    [Theory]
    [InlineData("LLPlayer")]
    [InlineData(".")]
    [InlineData("../LLPlayer")]
    public void WindowsCandidates_RejectRelativeApplicationDirectories(string appDirectory)
    {
        Action locate = () => OpenAlNative.GetLibraryCandidates(appDirectory, isWindows: true);

        locate.Should().Throw<ArgumentException>().WithParameterName("baseDirectory");
    }

    [Fact]
    public void UnixCandidates_PreserveExistingSystemLibraryOrder()
    {
        OpenAlNative.GetLibraryCandidates(AppContext.BaseDirectory, isWindows: false).Should().Equal(
            "libopenal.so.1", "libopenal.so", "libopenal.1.dylib", "libopenal.dylib", "soft_oal.dll", "OpenAL32.dll");
    }
}
