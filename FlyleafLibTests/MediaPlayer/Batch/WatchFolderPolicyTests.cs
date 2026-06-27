using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Batch;

namespace FlyleafLib.MediaPlayer;

public class WatchFolderPolicyTests
{
    private static readonly Func<string, bool> IsMkv = p => p.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void NewVideo_NoOutput_IsEnqueued()
    {
        WatchFolderPolicy.ShouldEnqueue(@"C:\v\a.mkv", [], IsMkv, outputExists: false, overwriteExisting: false)
            .Should().Be(WatchEnqueueDecision.Enqueue);
    }

    [Fact]
    public void NonVideo_IsSkipped()
    {
        WatchFolderPolicy.ShouldEnqueue(@"C:\v\a.srt", [], IsMkv, outputExists: false, overwriteExisting: false)
            .Should().Be(WatchEnqueueDecision.SkipNotVideo);

        // A partial download (still being written) is filtered out the same way — its extension is not a video one.
        WatchFolderPolicy.ShouldEnqueue(@"C:\v\a.mkv.part", [], IsMkv, false, false)
            .Should().Be(WatchEnqueueDecision.SkipNotVideo);
    }

    [Fact]
    public void AlreadyListed_IsSkipped_CaseInsensitive()
    {
        string[] known = [@"C:\v\A.MKV"];
        WatchFolderPolicy.ShouldEnqueue(@"C:\v\a.mkv", known, IsMkv, outputExists: false, overwriteExisting: false)
            .Should().Be(WatchEnqueueDecision.SkipDuplicate);
    }

    [Fact]
    public void ExistingOutput_WithoutOverwrite_IsSkipped()
    {
        WatchFolderPolicy.ShouldEnqueue(@"C:\v\a.mkv", [], IsMkv, outputExists: true, overwriteExisting: false)
            .Should().Be(WatchEnqueueDecision.SkipExistingOutput);
    }

    [Fact]
    public void ExistingOutput_WithOverwrite_IsEnqueued()
    {
        WatchFolderPolicy.ShouldEnqueue(@"C:\v\a.mkv", [], IsMkv, outputExists: true, overwriteExisting: true)
            .Should().Be(WatchEnqueueDecision.Enqueue);
    }

    [Fact]
    public void NotVideo_TakesPrecedence_OverDuplicateAndOutput()
    {
        // The video gate is checked first: a non-video path is SkipNotVideo even if it would also be a duplicate.
        WatchFolderPolicy.ShouldEnqueue(@"C:\v\a.srt", [@"C:\v\a.srt"], IsMkv, outputExists: true, overwriteExisting: false)
            .Should().Be(WatchEnqueueDecision.SkipNotVideo);
    }
}
