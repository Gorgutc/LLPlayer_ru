using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaPlaylist;

namespace FlyleafLib;

/// <summary>
/// Unit tests for <see cref="PLSPlaylist"/> — the PLS (INI) playlist parser. It reads via the Win32
/// GetPrivateProfileString API, so each test writes a real temp .pls file (absolute path, written as
/// ASCII with CRLF line endings) and deletes it on dispose. Deterministic on the Windows-only target framework.
/// </summary>
public class PLSPlaylistTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string WritePls(params string[] lines)
    {
        // Path.GetTempFileName creates a unique zero-byte file; we overwrite it with the PLS content.
        string path = Path.GetTempFileName();
        _tempFiles.Add(path);
        // PLS is an INI file. Write ASCII (explicit, not the platform default) with CRLF — the form the
        // Win32 profile API parses most reliably; unique temp names avoid any INI read-cache collisions.
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", Encoding.ASCII);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void Parse_ReadsAllEntriesWithTitleAndLength()
    {
        string path = WritePls(
            "[playlist]",
            "NumberOfEntries=2",
            "File1=http://example.com/a.mp3",
            "Title1=Song A",
            "Length1=123",
            "File2=http://example.com/b.mp3",
            "Title2=Song B",
            "Length2=456");

        var items = PLSPlaylist.Parse(path);

        items.Should().HaveCount(2);
        items[0].Url.Should().Be("http://example.com/a.mp3");
        items[0].Title.Should().Be("Song A");
        items[0].Duration.Should().Be(123);
        items[1].Url.Should().Be("http://example.com/b.mp3");
        items[1].Title.Should().Be("Song B");
        items[1].Duration.Should().Be(456);
    }

    [Fact]
    public void Parse_StopsAtFirstMissingFileEntry_WhenNumberOfEntriesAbsent()
    {
        // No NumberOfEntries → loop default is 1000, but it breaks at the first absent File{i}.
        string path = WritePls(
            "[playlist]",
            "File1=http://only-one");

        var items = PLSPlaylist.Parse(path);

        items.Should().HaveCount(1);
        items[0].Url.Should().Be("http://only-one");
    }

    [Fact]
    public void Parse_LeavesTitleNullAndDurationZero_WhenOptionalFieldsAbsent()
    {
        string path = WritePls(
            "[playlist]",
            "NumberOfEntries=1",
            "File1=http://no-meta");

        var items = PLSPlaylist.Parse(path);

        items.Should().HaveCount(1);
        items[0].Url.Should().Be("http://no-meta");
        items[0].Title.Should().BeNull();
        items[0].Duration.Should().Be(0);
    }

    [Fact]
    public void Parse_ReturnsEmpty_WhenNoPlaylistSection()
    {
        string path = WritePls(
            "[other]",
            "foo=bar");

        PLSPlaylist.Parse(path).Should().BeEmpty();
    }

    [Fact]
    public void GetINIAttribute_ReturnsValue_WhenKeyPresent()
    {
        string path = WritePls(
            "[playlist]",
            "NumberOfEntries=3",
            "File1=http://x");

        PLSPlaylist.GetINIAttribute("playlist", "NumberOfEntries", path).Should().Be("3");
        PLSPlaylist.GetINIAttribute("playlist", "File1", path).Should().Be("http://x");
    }

    [Fact]
    public void GetINIAttribute_ReturnsNull_WhenKeyMissing()
    {
        string path = WritePls(
            "[playlist]",
            "File1=http://x");

        PLSPlaylist.GetINIAttribute("playlist", "DoesNotExist", path).Should().BeNull();
    }

    [Fact]
    public void Parse_StopsAtMissingFile_WhenNumberOfEntriesExceedsActualFiles()
    {
        // NumberOfEntries larger than the real file count → the loop still breaks at the first absent File{i}.
        string path = WritePls(
            "[playlist]",
            "NumberOfEntries=100",
            "File1=http://a",
            "File2=http://b");

        var items = PLSPlaylist.Parse(path);

        items.Should().HaveCount(2);
        items[0].Url.Should().Be("http://a");
        items[1].Url.Should().Be("http://b");
    }

    [Fact]
    public void Parse_IgnoresExtraFiles_WhenNumberOfEntriesIsSmaller()
    {
        // NumberOfEntries caps the loop; files beyond it are silently ignored.
        string path = WritePls(
            "[playlist]",
            "NumberOfEntries=2",
            "File1=http://a",
            "File2=http://b",
            "File3=http://c");

        var items = PLSPlaylist.Parse(path);

        items.Should().HaveCount(2);
        items[1].Url.Should().Be("http://b");
    }

    [Fact]
    public void Parse_PreservesTitleWithSpaces()
    {
        string path = WritePls(
            "[playlist]",
            "NumberOfEntries=1",
            "File1=http://a",
            "Title1=My Channel Name With Spaces");

        var items = PLSPlaylist.Parse(path);

        items.Should().HaveCount(1);
        items[0].Title.Should().Be("My Channel Name With Spaces");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Parse_ReturnsEmpty_WhenNumberOfEntriesIsZeroOrNegative(string count)
    {
        // entries <= 0 → the 1-based loop body never executes.
        string path = WritePls(
            "[playlist]",
            $"NumberOfEntries={count}",
            "File1=http://a");

        PLSPlaylist.Parse(path).Should().BeEmpty();
    }
}
