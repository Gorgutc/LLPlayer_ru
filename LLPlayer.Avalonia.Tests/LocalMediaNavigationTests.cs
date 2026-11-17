using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public sealed class LocalMediaNavigationTests
{
    [Fact]
    public void Siblings_use_natural_filename_order_and_same_extension_ignoring_case()
    {
        using TempDir directory = new();
        string first = AddFile(directory, "Episode 1.mkv");
        string middle = AddFile(directory, "Episode 2.MKV");
        string last = AddFile(directory, "Episode 10.mkv");
        AddFile(directory, "Episode 3.mp4");
        AddFile(directory, "Episode 4.mkv.bak");
        Directory.CreateDirectory(Path.Combine(directory.Path, "Episode 5.mkv"));

        Assert.Equal(middle, LocalMediaNavigation.GetSibling(first, next: true));
        Assert.Equal(last, LocalMediaNavigation.GetSibling(middle, next: true));
        Assert.Equal(first, LocalMediaNavigation.GetSibling(middle, next: false));
        Assert.Equal(middle, LocalMediaNavigation.GetSibling(last, next: false));
    }

    [Fact]
    public void Sibling_navigation_does_not_wrap_at_either_end()
    {
        using TempDir directory = new();
        string first = AddFile(directory, "Part 1.mp4");
        string last = AddFile(directory, "Part 2.mp4");

        Assert.Null(LocalMediaNavigation.GetSibling(first, next: false));
        Assert.Null(LocalMediaNavigation.GetSibling(last, next: true));
    }

    [Fact]
    public void File_uri_is_decoded_once_and_uses_the_current_filesystem_snapshot()
    {
        using TempDir directory = new();
        string current = AddFile(directory, "Lesson 1 #50%.mkv");
        string next = AddFile(directory, "Lesson 2 #50%.mkv");
        string uri = new Uri(current).AbsoluteUri;

        Assert.Equal(next, LocalMediaNavigation.GetSibling(uri, next: true));
        File.Delete(next);
        Assert.Null(LocalMediaNavigation.GetSibling(uri, next: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative/movie.mkv")]
    [InlineData("https://example.test/movie.mkv")]
    [InlineData("rtsp://example.test/movie.mkv")]
    [InlineData("ytdlp:https://example.test/watch?v=1#")]
    [InlineData("file:///tmp/movie.mkv?query=1")]
    [InlineData("file:///tmp/movie.mkv#fragment")]
    [InlineData("bad\0name.mkv")]
    public void Local_path_admission_rejects_nonlocal_or_invalid_inputs(string? source)
    {
        Assert.False(LocalMediaNavigation.TryGetLocalPath(source, out _));
    }

    [Fact]
    public void Local_path_admission_accepts_absolute_path_or_file_uri_without_probing_existence()
    {
        using TempDir directory = new();
        string missing = Path.Combine(directory.Path, "not created #1.mkv");

        Assert.True(LocalMediaNavigation.TryGetLocalPath(missing, out string absolute));
        Assert.Equal(missing, absolute);
        Assert.True(LocalMediaNavigation.TryGetLocalPath(new Uri(missing).AbsoluteUri, out string fromUri));
        Assert.Equal(missing, fromUri);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void Equal_natural_sort_keys_use_a_deterministic_filename_tiebreaker()
    {
        using TempDir directory = new();
        string padded = AddFile(directory, "Part 01.mkv");
        string plain = AddFile(directory, "Part 1.mkv");
        string last = AddFile(directory, "Part 2.mkv");

        Assert.Equal(plain, LocalMediaNavigation.GetSibling(padded, next: true));
        Assert.Equal(last, LocalMediaNavigation.GetSibling(plain, next: true));
    }

    [Fact]
    public void Missing_file_and_extensionless_media_are_contextual_navigation_errors()
    {
        using TempDir directory = new();
        string missing = Path.Combine(directory.Path, "missing.mkv");
        string extensionless = AddFile(directory, "media");

        Assert.Throws<FileNotFoundException>(() => LocalMediaNavigation.GetSibling(missing, next: true));
        Assert.Throws<InvalidOperationException>(() => LocalMediaNavigation.GetSibling(extensionless, next: true));
    }

    static string AddFile(TempDir directory, string name)
    {
        string path = Path.Combine(directory.Path, name);
        File.WriteAllText(path, "fixture");
        return path;
    }
}
