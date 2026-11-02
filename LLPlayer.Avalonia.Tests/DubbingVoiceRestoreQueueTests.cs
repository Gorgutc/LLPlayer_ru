using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public class DubbingVoiceRestoreQueueTests
{
    [Fact]
    public async Task Explicit_clear_during_pending_read_cannot_be_resurrected_by_later_notifications()
    {
        var queue = new DubbingVoiceRestoreQueue();
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SubtitleData cue = Cue(null);
        var map = DubbingVoiceAssignmentMap.FromCurrentSubtitles("/movie.mkv", [Cue("saved-old-voice")]);
        int reads = 0;
        DubbingVoiceAssignmentMap Load()
        { reads++; entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return map; }
        Task<bool> Apply(DubbingVoiceAssignmentMap value, Func<bool> current)
        { if (current()) value.Apply("/movie.mkv", [cue]); return Task.FromResult(current()); }
        try
        {
            Task pending = queue.RestoreAsync(0, "source", Load, Apply);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            queue.Suppress(0, "source"); // Host calls this inside the identity-validated edit before assigning null.
            cue.AssignedVoiceId = null;
            release.Set();
            await pending;
            await queue.RestoreAsync(0, "source", Load, Apply);
            Assert.Null(cue.AssignedVoiceId);
            Assert.Equal(1, reads);
            await queue.RestoreAsync(0, "new-source", () => map, Apply);
            Assert.Equal("saved-old-voice", cue.AssignedVoiceId);
        }
        finally { release.Set(); await queue.DrainAsync(); }
    }

    [Fact]
    public async Task Same_source_reads_once_and_preserves_assignment_edited_while_read_pending()
    {
        var queue = new DubbingVoiceRestoreQueue();
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        SubtitleData cue = Cue(null);
        var map = DubbingVoiceAssignmentMap.FromCurrentSubtitles("/movie.mkv", [Cue("disk")]);
        DubbingVoiceAssignmentMap Load()
        { Interlocked.Increment(ref reads); entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return map; }
        Task<bool> Apply(DubbingVoiceAssignmentMap value, Func<bool> current)
        { if (current()) value.Apply("/movie.mkv", [cue]); return Task.FromResult(current()); }
        try
        {
            Task first = queue.RestoreAsync(0, "source", Load, Apply);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task duplicate = queue.RestoreAsync(0, "source", Load, Apply);
            cue.AssignedVoiceId = "new-live-edit";
            release.Set();
            await Task.WhenAll(first, duplicate);
            await queue.RestoreAsync(0, "source", Load, Apply);
            Assert.Equal(1, reads);
            Assert.Equal("new-live-edit", cue.AssignedVoiceId);
        }
        finally { release.Set(); await queue.DrainAsync(); }
    }

    [Fact]
    public async Task Source_replacements_coalesce_and_old_disk_result_cannot_apply()
    {
        var queue = new DubbingVoiceRestoreQueue();
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> applied = [];
        int obsoleteReads = 0, latestReads = 0;
        DubbingVoiceAssignmentMap BlockedLoad()
        { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return DubbingVoiceAssignmentMap.Empty; }
        Task<bool> ApplyOld(DubbingVoiceAssignmentMap map, Func<bool> current)
        { if (current()) applied.Add("old"); return Task.FromResult(current()); }
        try
        {
            Task first = queue.RestoreAsync(0, "old", BlockedLoad, ApplyOld);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            _ = queue.RestoreAsync(0, "obsolete", () => { obsoleteReads++; return DubbingVoiceAssignmentMap.Empty; }, ApplyOld);
            Task latest = queue.RestoreAsync(0, "latest", () => { latestReads++; return DubbingVoiceAssignmentMap.Empty; },
                (_, current) => { if (current()) applied.Add("latest"); return Task.FromResult(current()); });
            release.Set();
            await Task.WhenAll(first, latest);
            Assert.Equal(0, obsoleteReads);
            Assert.Equal(1, latestReads);
            Assert.Equal(new[] { "latest" }, applied);
        }
        finally { release.Set(); await queue.DrainAsync(); }
    }

    [Fact]
    public async Task Shutdown_rejects_deferred_apply_and_drains_both_slots()
    {
        var queue = new DubbingVoiceRestoreQueue();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int applies = 0;
        Task pending = queue.RestoreAsync(1, "source", () => DubbingVoiceAssignmentMap.Empty, async (_, current) =>
        { entered.TrySetResult(); await release.Task; if (current()) applies++; return current(); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Task drain = queue.DrainAsync();
        Assert.False(drain.IsCompleted);
        release.TrySetResult();
        await Task.WhenAll(pending, drain);
        await queue.RestoreAsync(0, "late", () => throw new Exception("Must not read after drain"), (_, _) => Task.FromResult(true));
        Assert.Equal(0, applies);
    }

    static SubtitleData Cue(string? voice) => new()
    { Text = "line", StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(1), AssignedVoiceId = voice };
}
