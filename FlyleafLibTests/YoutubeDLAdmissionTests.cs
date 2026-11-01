using System.Runtime.CompilerServices;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.Plugins;

namespace FlyleafLib.Tests;

public class YoutubeDLAdmissionTests
{
#if WINDOWS
    [Fact]
    public void LegacyPluginRetainsItsOptions()
    {
        var options = new YoutubeDL().GetDefaultOptions();
        Assert.Equal("", options["ExtraArguments"]);
        Assert.Equal("720", options["MaxVideoHeight"]);
        Assert.Equal("False", options["PreferVideoWithAudio"]);
        Assert.Equal(3, options.Count);
    }
#else
    [Theory]
    [InlineData("https://media.example/direct/1234", false)]
    [InlineData("ytdlp:https://media.example/watch/1234#", false)]
    [InlineData("https://media.example/radio.mp3", false)]
    public void ClaimsOnlyTargetSpecificInput(string url, bool expected)
    {
        // Admission reads only the input; no engine or native decoder is needed.
        var handler = (PluginHandler)RuntimeHelpers.GetUninitializedObject(typeof(PluginHandler));
        handler.Playlist = new Playlist(-1) { Url = url };
        var plugin = new YoutubeDL { Handler = handler };
        using var ordinary = OnlineVideoRequest.ObserveCancellation(TestContext.Current.CancellationToken);
        Assert.Equal(expected, plugin.CanOpen());
    }

    [Fact]
    public void ExplicitAdmissionOnlyMatchesItsOwnRequestAndDoesNotFlowToNativeWorker()
    {
        Assert.True(OnlineVideoRequest.TryCreate("https://example.org/video#chapter", out string request));
        using var scope = OnlineVideoRequest.ObserveCancellation(TestContext.Current.CancellationToken, request);
        Assert.True(OnlineVideoRequest.TryGetAdmittedUrl(request, out _));
        Assert.False(OnlineVideoRequest.TryGetAdmittedUrl(request + "#", out _));
        bool workerAdmitted = true;
        var native = new Thread(() => workerAdmitted = OnlineVideoRequest.TryGetAdmittedUrl(request, out _));
        native.Start();
        Assert.True(native.Join(TimeSpan.FromSeconds(5)));
        Assert.False(workerAdmitted);
    }

    [Fact]
    public void ExplicitPageEndingInSubtitleExtensionUsesMediaOpenAndRoundTrips()
    {
        const string url = "https://example.org/watch#some.vtt";
        Assert.True(OnlineVideoRequest.TryCreate(url, out var request));
        Assert.DoesNotContain(Utils.GetUrlExtention(request), Utils.ExtensionsSubtitles);
        Assert.True(OnlineVideoRequest.TryGetUrl(request, out var roundTrip));
        Assert.Equal(url, roundTrip);
    }
#endif
}
