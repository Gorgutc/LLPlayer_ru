using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public class SubtitleWordSegmentationTests
{
    [Theory]
    [InlineData("中国，hello 世界", 0, "中")]
    [InlineData("中国，hello 世界", 1, "国")]
    [InlineData("中国，hello 世界", 5, "hello")]
    [InlineData("𠀀中国", 0, "𠀀")]
    [InlineData("𠀀中国", 1, "𠀀")]
    [InlineData("中don't国", 3, "don't")]
    [InlineData("中state-of-the-art国", 9, "state-of-the-art")]
    [InlineData("𠀀mother-in-law's中", 11, "mother-in-law's")]
    public void Chinese_uses_individual_Han_with_exact_UTF16_offsets(string text, int index, string expected)
    {
        Assert.True(SubtitleWordSegmenter.TryGetRange(text, index, "zh-Hant", out int start, out int end));
        Assert.Equal(expected, text[start..end]);
    }

    [Fact]
    public void Japanese_uses_the_existing_MeCab_dictionary_and_preserves_whitespace_offsets()
    {
        const string text = "  私は日本語を勉強します。";
        Assert.True(SubtitleWordSegmenter.TryGetRange(text, text.IndexOf('日'), "ja", out int start, out int end));
        Assert.Equal("日本語", text[start..end]);
        Assert.Equal(4, start);
        Assert.False(SubtitleWordSegmenter.TryGetRange(text, 0, "ja", out _, out _));
        Assert.False(SubtitleWordSegmenter.TryGetRange(text, text.Length - 1, "ja", out _, out _));
    }
}
