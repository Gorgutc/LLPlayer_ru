using AwesomeAssertions;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public class AppIdentityTests
{
    [Fact]
    public void Version_IdentifiesTheSharedAvaloniaApp_WithoutStartingTheDesktop()
    {
        TextWriter previous = Console.Out;
        using StringWriter output = new();
        try
        {
            Console.SetOut(output);
            Program.Main(["--version"]).Should().Be(0);
        }
        finally
        {
            Console.SetOut(previous);
        }

        output.ToString().Should().StartWith("LLPlayer (Avalonia) ")
            .And.NotContain("Linux").And.NotContain("Windows");
        output.ToString().Should().Contain(typeof(App).Assembly.GetName().Version!.ToString(3));
    }
}

public class NativePayloadTests
{
    static readonly string[] WindowsWhisperLibraries =
        ["whisper.dll", "ggml-whisper.dll", "ggml-base-whisper.dll", "ggml-cpu-whisper.dll"];
    static readonly string[] LinuxWhisperLibraries =
        ["libwhisper.so", "libggml-whisper.so", "libggml-base-whisper.so", "libggml-cpu-whisper.so"];

    [Fact]
    public void HostBuild_CopiesOnlyItsOwnWhisperCpuPayload()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "The Avalonia host currently supports Windows x64 and Linux x64.");
        string expectedRid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        string foreignRid = OperatingSystem.IsWindows() ? "linux-x64" : "win-x64";
        string[] expected = OperatingSystem.IsWindows() ? WindowsWhisperLibraries : LinuxWhisperLibraries;
        string[] foreign = OperatingSystem.IsWindows() ? LinuxWhisperLibraries : WindowsWhisperLibraries;

        foreach (string name in expected)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "runtimes", expectedRid, name);
            File.Exists(path).Should().BeTrue($"the {expectedRid} app must supply its selected Whisper CPU library {name}");
            new FileInfo(path).Length.Should().BeGreaterThan(0);
        }
        foreach (string name in foreign)
            File.Exists(Path.Combine(AppContext.BaseDirectory, "runtimes", foreignRid, name)).Should().BeFalse("foreign Whisper CPU libraries must not leak into the host output");
    }

    [Fact]
    public void WindowsBuild_SuppliesAppLocalOpenAlSoft()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows OpenAL is app-local; Linux uses the system OpenAL Soft library.");
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "soft_oal.dll"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "soft_oal.dll"),
        ];
        candidates.Any(path => File.Exists(path) && new FileInfo(path).Length > 0).Should().BeTrue("the native Windows Avalonia build must carry the pinned OpenAL Soft DLL");
    }
}
