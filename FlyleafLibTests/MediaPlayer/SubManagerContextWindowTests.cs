namespace FlyleafLib.MediaPlayer;

public class SubManagerContextWindowTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Nonnegative_windows_include_available_cues_without_forward_index_overflow(int before, int after)
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        config.Subtitles.LanguageAutoDetect = false;
        var manager = new SubManager(config, 0);
        SubtitleData first = new() { Text = "before", StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(1) };
        SubtitleData focal = new() { Text = "focal", StartTime = TimeSpan.FromSeconds(1), EndTime = TimeSpan.FromSeconds(2) };
        SubtitleData next = new() { Text = "after", StartTime = TimeSpan.FromSeconds(2), EndTime = TimeSpan.FromSeconds(3) };
        manager.Load([first, focal, next]);
        Assert.Equal(1, focal.Index);
        var context = manager.GetContextWindow(focal, before, after);
        Assert.Equal(before == 0 ? [] : new[] { "before" }, context.before);
        Assert.Equal(after == 0 ? [] : new[] { "after" }, context.after);
    }
}
