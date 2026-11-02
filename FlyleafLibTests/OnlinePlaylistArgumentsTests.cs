#if !WINDOWS
using System.Reflection;
using FlyleafLib.Plugins;

namespace FlyleafLib.Tests;

public sealed class OnlinePlaylistArgumentsTests
{
    static IReadOnlyList<string> Parse(string text)
    {
        var type = typeof(OnlineVideoRequest).Assembly.GetType("FlyleafLib.Plugins.OnlineVideoArguments");
        Assert.NotNull(type);
        var method = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        try { return (IReadOnlyList<string>)method.Invoke(null, [text])!; }
        catch (TargetInvocationException e) when (e.InnerException is ArgumentException argument) { throw argument; }
    }

    [Fact]
    public void UsefulExtractionAndAuthenticationOptionsRemainIndividualArguments()
    {
        Assert.Equal(["--cookies-from-browser", "firefox", "--proxy", "http://127.0.0.1:3128", "--extractor-args", "youtube:player_client=web,android", "--user-agent", "lesson browser", "--playlist-items", "1,3:5"],
            Parse("--cookies-from-browser firefox --proxy http://127.0.0.1:3128 --extractor-args youtube:player_client=web,android --user-agent 'lesson browser' --playlist-items=1,3:5"));
    }

    [Theory]
    [InlineData("--exec 'touch /tmp/pwn'")]
    [InlineData("--config-locations config")]
    [InlineData("--plugin-dirs private")]
    [InlineData("--print-to-file title output")]
    [InlineData("--download-sections *0-5")]
    [InlineData("--no-skip-download")]
    [InlineData("--remote-components ejs:github")]
    [InlineData("--flat-playlist")]
    [InlineData("--unknown-option")]
    [InlineData("https://second.example/video")]
    [InlineData("--proxy")]
    [InlineData("--user-agent 'unclosed")]
    [InlineData("--user-agent line\nnext")]
    [InlineData("--playlist-items 1:")]
    [InlineData("--playlist-items 1:1001")]
    [InlineData("--playlist-items 3:1")]
    [InlineData("--playlist-items 1:3:0")]
    [InlineData("--playlist-start 1 --playlist-end 2000")]
    [InlineData("--playlist-start 1 --playlist-items 1:3")]
    public void UnsafeUnsupportedAndMalformedArgumentsAreRejected(string input)
        => Assert.Throws<ArgumentException>(() => Parse(input));

    [Fact]
    public void ArgumentsHaveABoundedSize()
        => Assert.Throws<ArgumentException>(() => Parse("--user-agent " + new string('x', 16385)));

    [Fact]
    public void StartAndEndBecomeABoundedRangeAndFarAwayClosedSelectionsRemainUseful()
    {
        Assert.Equal(["--playlist-items", "2001:2003"], Parse("--playlist-start 2001 --playlist-end 2003"));
        Assert.Equal(["--playlist-items", "2001:3000"], Parse("--playlist-start 2001"));
        Assert.Equal(["--playlist-items", "2001:2003,5000"], Parse("--playlist-items 2001:2003,5000"));
    }
}
#endif
