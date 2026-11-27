using System.Text;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaPlayer;

namespace FlyleafLibTests.MediaPlayer;

public sealed class SrtTimingDocumentTests
{
    [Fact]
    public void Revision_changes_when_a_local_media_file_is_replaced_and_rejects_web_inputs()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "Before");
            Assert.True(Demuxer.TryGetLocalMediaFileRevision(path, out var original));
            Assert.Equal(path, original.Path);
            Assert.True(Demuxer.TryGetLocalMediaFileRevision(new Uri(path).AbsoluteUri, out var fromUri));
            Assert.Equal(original, fromUri);
            File.WriteAllText(path, "After changing the selected media.");
            Assert.True(Demuxer.TryGetLocalMediaFileRevision(path, out var replaced));
            Assert.NotEqual(original, replaced);
            Assert.False(Demuxer.TryGetLocalMediaFileRevision("https://example.invalid/video.mp4", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    public void Rewrite_changes_only_the_timestamp_tokens(string newline, bool bom)
    {
        string text = (bom ? "\ufeff" : "") + string.Join(newline,
            "7", "  00:00:01,000  -->   00:00:02,000 X1:10 X2:20 Y1:30 Y2:40 ",
            "<i>Actual café</i>", "héllo  ", "", "12", "00:00:04,000 --> 00:00:05,000",
            "<font color=\"red\">That is author text.</font>");
        byte[] original = Encoding.UTF8.GetBytes(text);

        SrtTimingDocument document = SrtTimingDocument.Parse(original);
        original.AsSpan().Fill(0);
        Assert.Collection(document.Cues,
            cue =>
            {
                Assert.Equal(0, cue.Id);
                Assert.Equal(TimeSpan.FromSeconds(1), cue.Start);
                Assert.Equal("<i>Actual café</i>\nhéllo  ", cue.Text);
            },
            cue => Assert.Equal(1, cue.Id));

        byte[] result = document.Rewrite(new Dictionary<int, (TimeSpan, TimeSpan)>
        {
            [0] = (TimeSpan.FromMilliseconds(2300), TimeSpan.FromMilliseconds(3210)),
        });
        Assert.Equal(Encoding.UTF8.GetBytes(text.Replace("00:00:01,000", "00:00:02,300")
            .Replace("00:00:02,000", "00:00:03,210")), result);
    }

    [Theory]
    [InlineData("4\n00:00:01,000 --> 00:00:01,000\nZero duration")]
    [InlineData("4\n00:99:01,000 --> 00:00:02,000\nBad minute")]
    [InlineData("4\n00:00:01,000 --> 00:00:02,000\n\n")]
    [InlineData("not an SRT")]
    public void Rejects_unsupported_or_malformed_cues(string text)
    {
        Assert.Throws<InvalidDataException>(() => SrtTimingDocument.Parse(Encoding.UTF8.GetBytes(text)));
        Assert.Throws<InvalidDataException>(() => SrtTimingDocument.Parse([0xff, 0xfe, 0x30, 0]));
    }

    [Fact]
    public void Refuses_a_rewrite_that_would_collapse_after_millisecond_quantization()
    {
        var document = SrtTimingDocument.Parse("1\n00:00:01,000 --> 00:00:02,000\nOne cue"u8);
        Assert.Throws<ArgumentException>(() => document.Rewrite(new Dictionary<int, (TimeSpan, TimeSpan)>
        {
            [0] = (TimeSpan.FromTicks(1), TimeSpan.FromTicks(2)),
        }));
    }

    [Fact]
    public void Native_and_sidebar_decoders_must_attest_the_same_authored_bytes()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] original = "1\n00:00:01,000 --> 00:00:02,000\nThe first text"u8.ToArray();
            File.WriteAllBytes(path, original);
            var native = Assert.IsType<SrtTimingInput>(SrtTimingInput.TryRead(path, 1024));
            var sidebar = Assert.IsType<SrtTimingInput>(SrtTimingInput.TryRead(path, 1024));
            Assert.NotNull(native.ParseIfSameInput(sidebar));

            byte[] changed = "1\n00:00:01,000 --> 00:00:02,000\nThe other text"u8.ToArray();
            Assert.Equal(original.Length, changed.Length);
            File.WriteAllBytes(path, changed);
            sidebar = Assert.IsType<SrtTimingInput>(SrtTimingInput.TryRead(path, 1024));
            Assert.Null(native.ParseIfSameInput(sidebar));
            Assert.Equal(original, native.DecoderBytes.ToArray());
            Assert.Equal(changed, sidebar.DecoderBytes.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
