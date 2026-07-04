using System;
using System.IO;
using AwesomeAssertions;

namespace FlyleafLib;

public class ArchivePathGuardTests
{
    // A stable, absolute base directory (need not exist on disk — the guard is pure path math).
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "llplayer-engines");

    [Theory]
    [InlineData("file.txt")]
    [InlineData("sub/file.txt")]
    [InlineData("sub\\deep\\file.txt")]
    [InlineData("sub/../ok.txt")]           // climbs then comes back inside → still contained
    [InlineData("")]                         // resolves to the base dir itself → contained
    public void IsWithinDirectory_ContainedEntries_ReturnTrue(string entry)
    {
        ArchivePathGuard.IsWithinDirectory(Base, entry).Should().BeTrue();
    }

    [Theory]
    [InlineData("../evil.dll")]              // one level up
    [InlineData("..\\..\\evil.dll")]         // two levels up
    [InlineData("sub/../../evil.dll")]       // net escape
    [InlineData("../llplayer-engines-evil/x.dll")] // sibling-prefix must NOT be treated as inside
    public void IsWithinDirectory_EscapingRelativeEntries_ReturnFalse(string entry)
    {
        ArchivePathGuard.IsWithinDirectory(Base, entry).Should().BeFalse();
    }

    [Fact]
    public void IsWithinDirectory_AbsoluteEntry_ReturnsFalse()
    {
        ArchivePathGuard.IsWithinDirectory(Base, @"C:\Windows\System32\evil.dll").Should().BeFalse();
    }

    [Fact]
    public void IsWithinDirectory_DriveRootedEntry_ReturnsFalse()
    {
        // A leading separator is rooted on Windows; Path.Combine returns it verbatim → escapes the base.
        ArchivePathGuard.IsWithinDirectory(Base, @"\evil.dll").Should().BeFalse();
    }

    [Fact]
    public void IsWithinDirectory_EmptyBase_Throws()
    {
        Action act = () => ArchivePathGuard.IsWithinDirectory("", "file.txt");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateEntries_AllContained_DoesNotThrow()
    {
        string[] entries = ["license.txt", "Faster-Whisper-XXL/whisper.exe", "Faster-Whisper-XXL/_models/model.bin"];

        Action act = () => ArchivePathGuard.ValidateEntries(entries, Base);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateEntries_OneEscapingEntry_ThrowsIOException()
    {
        // RED-without-fix: SevenZipSharp does not sanitize "..", so an engine 7z (compromised upstream/MITM) with a
        // single "../" entry would previously write outside the engines dir. The guard must refuse the whole archive.
        string[] entries = ["license.txt", "../../Startup/evil.dll", "whisper.exe"];

        Action act = () => ArchivePathGuard.ValidateEntries(entries, Base);

        act.Should().Throw<IOException>().WithMessage("*zip-slip*");
    }

    [Fact]
    public void ValidateEntries_NullEntries_Throws()
    {
        Action act = () => ArchivePathGuard.ValidateEntries(null!, Base);
        act.Should().Throw<ArgumentNullException>();
    }
}
