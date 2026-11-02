using System.Reflection;
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

        Assert.Equal(middle, Sibling(first, next: true));
        Assert.Equal(last, Sibling(middle, next: true));
        Assert.Equal(first, Sibling(middle, next: false));
        Assert.Equal(middle, Sibling(last, next: false));
    }

    [Fact]
    public void Sibling_navigation_does_not_wrap_at_either_end()
    {
        using TempDir directory = new();
        string first = AddFile(directory, "Part 1.mp4");
        string last = AddFile(directory, "Part 2.mp4");

        Assert.Null(Sibling(first, next: false));
        Assert.Null(Sibling(last, next: true));
    }

    [Fact]
    public void File_uri_is_decoded_once_and_uses_the_current_filesystem_snapshot()
    {
        using TempDir directory = new();
        string current = AddFile(directory, "Lesson 1 #50%.mkv");
        string next = AddFile(directory, "Lesson 2 #50%.mkv");
        string uri = new Uri(current).AbsoluteUri;

        Assert.Equal(next, Sibling(uri, next: true));
        File.Delete(next);
        Assert.Null(Sibling(uri, next: true));
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
        Assert.Null(LocalPath(source));
    }

    [Fact]
    public void Local_path_admission_accepts_absolute_path_or_file_uri_without_probing_existence()
    {
        using TempDir directory = new();
        string missing = Path.Combine(directory.Path, "not created #1.mkv");

        Assert.Equal(missing, LocalPath(missing));
        Assert.Equal(missing, LocalPath(new Uri(missing).AbsoluteUri));
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void Equal_natural_sort_keys_use_a_deterministic_filename_tiebreaker()
    {
        using TempDir directory = new();
        string padded = AddFile(directory, "Part 01.mkv");
        string plain = AddFile(directory, "Part 1.mkv");
        string last = AddFile(directory, "Part 2.mkv");

        Assert.Equal(plain, Sibling(padded, next: true));
        Assert.Equal(last, Sibling(plain, next: true));
    }

    [Fact]
    public void Missing_file_and_extensionless_media_are_contextual_navigation_errors()
    {
        using TempDir directory = new();
        string missing = Path.Combine(directory.Path, "missing.mkv");
        string extensionless = AddFile(directory, "media");

        Assert.IsType<FileNotFoundException>(Assert.Throws<TargetInvocationException>(() => Sibling(missing, true)).InnerException);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Sibling(extensionless, true)).InnerException);
    }

    static string AddFile(TempDir directory, string name)
    {
        string path = Path.Combine(directory.Path, name);
        File.WriteAllText(path, "fixture");
        return path;
    }

    // Compiles on the unchanged baseline so RED is an absent capability assertion, not a compiler error.
    static Type NavigationType => Assert.IsAssignableFrom<Type>(typeof(AppPrefs).Assembly.GetType("LLPlayer.Avalonia.Services.LocalMediaNavigation"));

    static string? Sibling(string source, bool next)
    {
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(NavigationType.GetMethod("GetSibling", BindingFlags.Public | BindingFlags.Static));
        return (string?)method.Invoke(null, [source, next]);
    }

    static string? LocalPath(string? source)
    {
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(NavigationType.GetMethod("TryGetLocalPath", BindingFlags.Public | BindingFlags.Static));
        object?[] arguments = [source, null];
        return method.Invoke(null, arguments) is true ? (string?)arguments[1] : null;
    }
}
