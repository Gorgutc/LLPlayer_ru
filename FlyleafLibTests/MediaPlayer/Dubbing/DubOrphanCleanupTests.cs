using System.Collections.Generic;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib;

public class DubOrphanCleanupTests
{
    [Fact]
    public void CleanAll_DeletesARecordedCanceledOutput()
    {
        // RED-without-fix (HC-43): a canceled render's dub can materialize AFTER the in-catch delete. The tracker must
        // remember it and re-delete once the sidecar is reaped, otherwise the orphan makes the next run skip and the
        // auto-loader pick it up.
        var deleted = new List<string>();
        var tracker = new DubOrphanCleanup();

        tracker.MarkCanceled(@"C:\media\video.ru.dub.flac");
        var cleaned = tracker.CleanAll(deleted.Add);

        deleted.Should().ContainSingle().Which.Should().Be(@"C:\media\video.ru.dub.flac");
        cleaned.Should().ContainSingle();
    }

    [Fact]
    public void CleanAll_IsIdempotent_SecondCallDeletesNothing()
    {
        var deleted = new List<string>();
        var tracker = new DubOrphanCleanup();
        tracker.MarkCanceled(@"C:\media\a.ru.dub.flac");

        tracker.CleanAll(deleted.Add);
        tracker.CleanAll(deleted.Add);

        deleted.Should().ContainSingle("the set is cleared after the first CleanAll");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MarkCanceled_NullOrEmpty_IsIgnored(string? path)
    {
        var deleted = new List<string>();
        var tracker = new DubOrphanCleanup();

        tracker.MarkCanceled(path);

        tracker.CleanAll(deleted.Add).Should().BeEmpty();
        deleted.Should().BeEmpty();
    }

    [Fact]
    public void ClearAndClean_ForgetsTarget_SoAFreshSuccessIsNotReCleaned()
    {
        // RED-without-fix: after a cancel, re-rendering the SAME target must clear the record BEFORE the new success,
        // else DisposeAsync's CleanAll would delete the good new dub.
        var deleted = new List<string>();
        var tracker = new DubOrphanCleanup();
        tracker.MarkCanceled(@"C:\media\v.ru.dub.flac");

        bool wasPending = tracker.ClearAndClean(@"C:\media\v.ru.dub.flac", deleted.Add);
        var cleanedOnDispose = tracker.CleanAll(deleted.Add);

        wasPending.Should().BeTrue();
        deleted.Should().ContainSingle().Which.Should().Be(@"C:\media\v.ru.dub.flac"); // only the stale orphan, once
        cleanedOnDispose.Should().BeEmpty("the target was forgotten, so the fresh render output is left intact");
    }

    [Fact]
    public void ClearAndClean_UnrecordedTarget_DoesNotTouchFilesystem()
    {
        // A normal first render of a brand-new target must not trigger a delete.
        var deleted = new List<string>();
        var tracker = new DubOrphanCleanup();

        bool wasPending = tracker.ClearAndClean(@"C:\media\new.ru.dub.flac", deleted.Add);

        wasPending.Should().BeFalse();
        deleted.Should().BeEmpty();
    }

    [Fact]
    public void Tracking_IsCaseInsensitive()
    {
        var deleted = new List<string>();
        var tracker = new DubOrphanCleanup();
        tracker.MarkCanceled(@"C:\Media\Clip.ru.dub.FLAC");

        tracker.ClearAndClean(@"c:\media\clip.ru.dub.flac", deleted.Add).Should().BeTrue();
    }
}
