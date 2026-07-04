using System;
using System.IO;
using AwesomeAssertions;

namespace FlyleafLib;

public class SafeDirectoryTests : IDisposable
{
    private readonly string _root;

    public SafeDirectoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "llplayer-safedir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort test cleanup */ }
    }

    [Fact]
    public void TryDelete_RemovesDirectoryAndContents()
    {
        string dir = Path.Combine(_root, "work");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "f.txt"), "x");

        bool ok = SafeDirectory.TryDelete(dir);

        ok.Should().BeTrue();
        Directory.Exists(dir).Should().BeFalse();
    }

    [Fact]
    public void TryDelete_MissingDirectory_ReturnsTrueAndDoesNotThrow()
    {
        string dir = Path.Combine(_root, "does-not-exist");

        bool ok = SafeDirectory.TryDelete(dir);

        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryDelete_NullOrBlank_ReturnsTrueAndDoesNotThrow(string? path)
    {
        SafeDirectory.TryDelete(path).Should().BeTrue();
    }

    [Fact]
    public void TryDelete_WhenAFileIsLocked_SwallowsAndReturnsFalse()
    {
        // RED-without-fix: the bug this closes. A locked file inside workingDir makes Directory.Delete(dir, true)
        // throw IOException; the old YoutubeDL.DisposeInternal called it unguarded from a background PlayThread's
        // finally, so the throw escaped and crashed the app. TryDelete must swallow and report false instead.
        string dir = Path.Combine(_root, "locked");
        Directory.CreateDirectory(dir);
        string locked = Path.Combine(dir, "held.bin");
        File.WriteAllText(locked, "data");

        using (var _ = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bool ok = SafeDirectory.TryDelete(dir);

            ok.Should().BeFalse("a locked file prevents deletion");
            Directory.Exists(dir).Should().BeTrue("nothing should be forcibly removed");
        }

        // Once the lock is released the directory can be cleaned up normally (no lingering handle from TryDelete).
        SafeDirectory.TryDelete(dir).Should().BeTrue();
    }
}
