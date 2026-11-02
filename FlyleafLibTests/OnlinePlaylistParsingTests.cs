#if !WINDOWS
using System.Collections;
using System.Reflection;
using FlyleafLib.Plugins;

namespace FlyleafLib.Tests;

public sealed class OnlinePlaylistParsingTests
{
    static object Parse(string json)
    {
        var method = typeof(YoutubeDL).GetMethod("ParsePortablePlaylist", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(null, [json, "https://example.com/playlist?list=lesson"])!;
    }

    static T Property<T>(object result, string name)
        => (T)result.GetType().GetProperty(name)!.GetValue(result)!;

    [Fact]
    public void PlaylistMetadata_PreservesOrderTitleAndSafePlayableEntries()
    {
        var result = Parse("""
            {"_type":"playlist","title":"Lessons","playlist_count":5,"entries":[
              {"title":"first","webpage_url":"https://example.com/one","formats":[{"url":"https://cdn.example/one","vcodec":"h264","acodec":"none","height":720}]},
              null,
              {"title":"unsafe","webpage_url":"file:///private/lesson","formats":[{"url":"file:///private/lesson","vcodec":"h264"}]},
              {"_type":"multi_video","entries":[{"title":"second","webpage_url":"https://example.com/two","url":"https://cdn.example/two","vcodec":"none","acodec":"opus"}]}]}
            """);
        Assert.Equal("Lessons", Property<string>(result, "Title"));
        Assert.Equal(5, Property<int>(result, "ExpectedCount"));
        var items = Property<IEnumerable>(result, "Items").Cast<YoutubeDLJson>().ToArray();
        Assert.Equal(["first", "second"], items.Select(item => item.title));
        Assert.All(items, item => Assert.All(item.formats, format => Assert.StartsWith("https://", format.url)));
    }

    [Fact]
    public void PlaylistMetadata_DoesNotInventEntryPageUrlsOrTrustCredentials()
    {
        var result = Parse("""
            {"_type":"playlist","entries":[
              {"title":"no page","url":"https://cdn.example/one","vcodec":"h264"},
              {"title":"private","webpage_url":"https://user:password@example.com/two","url":"https://user:password@cdn.example/two","vcodec":"h264"},
              {"title":"valid","webpage_url":"https://example.com/three","url":"https://cdn.example/three","vcodec":"none","acodec":"opus"}]}
            """);
        var item = Assert.Single(Property<IEnumerable>(result, "Items").Cast<YoutubeDLJson>());
        Assert.Equal("valid", item.title);
        Assert.Equal("https://example.com/three", item.webpage_url);
    }

    [Fact]
    public void SingleMetadata_UsesRequestedPageAndDropsInvalidChaptersAndCaptionUrls()
    {
        var result = Parse("""
            {"title":"single","url":"https://cdn.example/one","vcodec":"h264",
             "chapters":[{"start_time":-1,"end_time":2,"title":"bad"},{"start_time":1,"end_time":2,"title":"good"}],
             "automatic_captions":{"en":[{"ext":"vtt","url":"file:///private/captions"},{"ext":"vtt","url":"https://cdn.example/captions"}]}}
            """);
        var item = Assert.Single(Property<IEnumerable>(result, "Items").Cast<YoutubeDLJson>());
        Assert.Equal("https://example.com/playlist?list=lesson", item.webpage_url);
        Assert.Equal("good", Assert.Single(item.chapters).title);
        Assert.Equal("https://cdn.example/captions", Assert.Single(item.automatic_captions["en"]).url);
    }
}
#endif
