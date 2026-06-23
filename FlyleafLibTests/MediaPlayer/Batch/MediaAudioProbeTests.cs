using System.Collections.Generic;
using AwesomeAssertions;
using FlyleafLib;
using FlyleafLib.MediaPlayer.Batch;

namespace FlyleafLib.MediaPlayer;

public class MediaAudioProbeTests
{
    private static MediaAudioTrack Track(int index, string? lang, long bitRate = 0, int channels = 2) =>
        new(index, lang == null ? Language.Unknown : Language.Get(lang), null, "aac", channels, bitRate);

    private static readonly IReadOnlyList<Language> PreferEnglish = [Language.English];
    private static readonly IReadOnlyList<Language> PreferNone = [];

    [Fact]
    public void PreferRussian_SelectsRussianTrack_EvenWhenNotFirst()
    {
        var tracks = new List<MediaAudioTrack> { Track(0, "en"), Track(1, "ru"), Track(2, "ja") };
        MediaAudioProbe.SelectTrackIndex(tracks, null, preferRussian: true, PreferEnglish).Should().Be(1);
    }

    [Fact]
    public void SingleRussianTrack_Selected()
    {
        var tracks = new List<MediaAudioTrack> { Track(0, "ru") };
        MediaAudioProbe.SelectTrackIndex(tracks, null, preferRussian: true, PreferNone).Should().Be(0);
    }

    [Fact]
    public void NoRussian_FallsBackToPreferredLanguage()
    {
        var tracks = new List<MediaAudioTrack> { Track(0, "ja"), Track(1, "en") };
        MediaAudioProbe.SelectTrackIndex(tracks, null, preferRussian: true, PreferEnglish).Should().Be(1);
    }

    [Fact]
    public void ForcedIndex_OverridesEverything()
    {
        var tracks = new List<MediaAudioTrack> { Track(0, "en"), Track(1, "ru") };
        MediaAudioProbe.SelectTrackIndex(tracks, forcedStreamIndex: 0, preferRussian: true, PreferNone).Should().Be(0);
    }

    [Fact]
    public void StaleForcedIndex_FallsThroughToAutoSelection()
    {
        var tracks = new List<MediaAudioTrack> { Track(0, "en"), Track(1, "ru") };
        MediaAudioProbe.SelectTrackIndex(tracks, forcedStreamIndex: 99, preferRussian: true, PreferNone).Should().Be(1);
    }

    [Fact]
    public void PreferRussianOff_IgnoresRussian_UsesPreferredLanguage()
    {
        var tracks = new List<MediaAudioTrack> { Track(0, "en"), Track(1, "ru") };
        MediaAudioProbe.SelectTrackIndex(tracks, null, preferRussian: false, PreferEnglish).Should().Be(0);
    }

    [Fact]
    public void NoLanguageMatch_FallsBackToBitrateThenChannels()
    {
        var tracks = new List<MediaAudioTrack>
        {
            Track(0, null, bitRate: 100, channels: 2),
            Track(1, null, bitRate: 500, channels: 6),
        };
        MediaAudioProbe.SelectTrackIndex(tracks, null, preferRussian: true, PreferNone).Should().Be(1);
    }

    [Fact]
    public void EmptyTrackList_ReturnsNull()
    {
        MediaAudioProbe.SelectTrackIndex([], null, preferRussian: true, PreferNone).Should().BeNull();
    }

    [Fact]
    public void RussianTrack_IsRussianFlag_True()
    {
        Track(0, "ru").IsRussian.Should().BeTrue();
        Track(0, "en").IsRussian.Should().BeFalse();
        Track(0, null).IsRussian.Should().BeFalse();
    }
}
