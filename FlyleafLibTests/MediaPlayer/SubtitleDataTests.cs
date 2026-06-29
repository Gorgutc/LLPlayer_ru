using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer;

// SubtitleData.Clone must carry the new per-cue source language (T-10) along with the existing scalar fields.
public class SubtitleDataTests
{
    [Fact]
    public void Clone_CopiesLanguage()
    {
        var sub = new SubtitleData
        {
            Index = 3,
            Text = "Hello",
            StartTime = TimeSpan.FromSeconds(1),
            EndTime = TimeSpan.FromSeconds(2),
            Language = Language.English,
        };

        SubtitleData clone = sub.Clone();

        clone.Language.Should().Be(Language.English);
        // Sanity: the clone is a distinct instance still carrying the other scalar fields (non-vacuous).
        clone.Should().NotBeSameAs(sub);
        clone.Index.Should().Be(3);
        clone.Text.Should().Be("Hello");
        clone.StartTime.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Clone_NullLanguage_StaysNull()
    {
        // Loaded / translated cues never set a language; the clone must keep it null (byte-identical default).
        var sub = new SubtitleData { Text = "x", Language = null };

        sub.Clone().Language.Should().BeNull();
    }
}
