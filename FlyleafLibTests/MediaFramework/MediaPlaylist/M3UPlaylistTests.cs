using System.Collections.Generic;
using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaPlaylist;

namespace FlyleafLib;

/// <summary>
/// Unit tests for <see cref="M3UPlaylist.ParseFromString"/> — the pure (no-HTTP) M3U/M3U8 parser path.
/// Expectations are derived by tracing the parser source (#EXTINF handling, tag regex, [Geo-blocked]/
/// [Not 24/7] stripping, "(NNNp)" height extraction, #EXTVLCOPT user-agent/referrer, URL capture).
/// </summary>
public class M3UPlaylistTests
{
    private static string M3U(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void ParseFromString_Empty_ReturnsNoItems()
    {
        M3UPlaylist.ParseFromString("").Should().BeEmpty();
    }

    [Fact]
    public void ParseFromString_IgnoresNonExtinfLines()
    {
        // #EXTM3U header and stray lines are not #EXTINF → no items produced.
        string text = M3U("#EXTM3U", "not-a-tag", "http://orphan");
        M3UPlaylist.ParseFromString(text).Should().BeEmpty();
    }

    [Fact]
    public void ParseFromString_SingleItem_ParsesTitleAndUrl()
    {
        string text = M3U("#EXTINF:-1,Channel One", "http://example.com/1");

        var items = M3UPlaylist.ParseFromString(text);

        items.Should().HaveCount(1);
        var item = items[0];
        item.Title.Should().Be("Channel One");
        item.OriginalTitle.Should().Be("Channel One");
        item.Url.Should().Be("http://example.com/1");
        item.Height.Should().Be(0);
        item.GeoBlocked.Should().BeFalse();
        item.Not_24_7.Should().BeFalse();
        item.Tags.Should().BeEmpty();
    }

    [Fact]
    public void ParseFromString_ParsesQuotedTags()
    {
        string text = M3U("#EXTINF:-1 tvg-id=\"ch1\" tvg-logo=\"logo.png\",Channel One", "http://example.com/1");

        var item = M3UPlaylist.ParseFromString(text)[0];

        item.Tags.Should().HaveCount(2);
        item.Tags["tvg-id"].Should().Be("ch1");
        item.Tags["tvg-logo"].Should().Be("logo.png");
        item.Title.Should().Be("Channel One");
    }

    [Fact]
    public void ParseFromString_StripsGeoBlockedMarker()
    {
        string text = M3U("#EXTINF:-1,Channel Two [Geo-blocked]", "http://example.com/2");

        var item = M3UPlaylist.ParseFromString(text)[0];

        item.GeoBlocked.Should().BeTrue();
        item.Not_24_7.Should().BeFalse();
        item.Title.Should().Be("Channel Two");
    }

    [Fact]
    public void ParseFromString_StripsBothGeoBlockedAndNot247Markers()
    {
        string text = M3U("#EXTINF:-1,Channel Two [Geo-blocked] [Not 24/7]", "http://example.com/2");

        var item = M3UPlaylist.ParseFromString(text)[0];

        item.GeoBlocked.Should().BeTrue();
        item.Not_24_7.Should().BeTrue();
        item.Title.Should().Be("Channel Two");
    }

    [Fact]
    public void ParseFromString_ExtractsHeightFromResolutionSuffix()
    {
        string text = M3U("#EXTINF:-1,Channel Three (720p)", "http://example.com/3");

        var item = M3UPlaylist.ParseFromString(text)[0];

        item.Height.Should().Be(720);
        item.Title.Should().Be("Channel Three");
    }

    [Fact]
    public void ParseFromString_ParsesExtVlcOptUserAgentAndReferrer()
    {
        string text = M3U(
            "#EXTINF:-1,Channel Four",
            "#EXTVLCOPT:http-user-agent=MyAgent/1.0",
            "#EXTVLCOPT:http-referrer=http://ref.example",
            "http://example.com/4");

        var item = M3UPlaylist.ParseFromString(text)[0];

        item.UserAgent.Should().Be("MyAgent/1.0");
        item.Referrer.Should().Be("http://ref.example");
        item.Url.Should().Be("http://example.com/4");
    }

    [Fact]
    public void ParseFromString_ParsesMultipleItems()
    {
        string text = M3U(
            "#EXTM3U",
            "#EXTINF:-1,A",
            "http://a",
            "#EXTINF:-1,B",
            "http://b");

        var items = M3UPlaylist.ParseFromString(text);

        items.Should().HaveCount(2);
        items[0].Title.Should().Be("A");
        items[0].Url.Should().Be("http://a");
        items[1].Title.Should().Be("B");
        items[1].Url.Should().Be("http://b");
    }

    [Fact]
    public void ParseFromString_ExtinfWithoutUrlAtEof_AddsItemWithNullUrl()
    {
        // Characterization: an #EXTINF that runs into EOF still yields an item; its Url is left null.
        string text = M3U("#EXTINF:-1,Last");

        var items = M3UPlaylist.ParseFromString(text);

        items.Should().HaveCount(1);
        items[0].Title.Should().Be("Last");
        items[0].Url.Should().BeNull();
    }

    [Fact]
    public void ParseFromString_StripsNot247Marker_Alone()
    {
        // The [Not 24/7] marker is stripped independently of [Geo-blocked].
        string text = M3U("#EXTINF:-1,Channel Five [Not 24/7]", "http://example.com/5");

        var item = M3UPlaylist.ParseFromString(text)[0];

        item.Not_24_7.Should().BeTrue();
        item.GeoBlocked.Should().BeFalse();
        item.Title.Should().Be("Channel Five");
    }

    [Fact]
    public void ParseFromString_EmptyTitleAfterComma_YieldsEmptyStringNotNull()
    {
        // GetMatch captures the (possibly empty) text after the comma; here that capture is "".
        string text = M3U("#EXTINF:-1,", "http://example.com/6");

        var item = M3UPlaylist.ParseFromString(text)[0];

        item.Title.Should().Be("");
        item.Url.Should().Be("http://example.com/6");
    }

    [Fact]
    public void ParseFromString_TagWithWhitespaceValue_IsNotParsed()
    {
        // The tag regex value class is [^\s"]+, so a value containing a space does not match and is dropped.
        string text = M3U("#EXTINF:-1 tvg-id=\"ch 1\",Channel Seven", "http://example.com/7");

        var item = M3UPlaylist.ParseFromString(text)[0];

        item.Tags.Should().BeEmpty();
        item.Title.Should().Be("Channel Seven");
    }

    [Fact]
    public void ParseFromString_LeadingWhitespaceBeforeExtinf_IsIgnored()
    {
        // The parser uses a strict StartsWith("#EXTINF"); an indented directive is not recognized.
        string text = M3U("  #EXTINF:-1,Indented", "http://example.com/8");

        M3UPlaylist.ParseFromString(text).Should().BeEmpty();
    }
}
