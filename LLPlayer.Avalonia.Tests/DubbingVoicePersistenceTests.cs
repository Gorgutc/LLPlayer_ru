using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public class DubbingVoicePersistenceTests
{
    [Fact]
    public void Pending_claim_snapshot_is_media_scoped_and_revision_ordered_before_ambient_defaults()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string media = Path.Combine(directory, "movie.mkv"), foreign = Path.Combine(directory, "foreign.mkv");
        try
        {
            using var queue = new DubbingVoiceSaveQueue(() => true, TimeSpan.FromHours(1));
            queue.Enqueue(media, [Cue(0, "pending-source-a")], 1);
            queue.Enqueue(media, [Cue(2, "older-c")], 2);
            queue.Enqueue(foreign, [Cue(4, "foreign")], 4);
            var current = new[] { new DubbingVoiceClaim(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "latest-c") };
            var claims = DubbingVoicePersistence.MergeEditClaims(current, queue.SnapshotClaims(media));
            var effective = DubbingVoicePersistence.MergeClaims(claims.Select(claim => claim.ToSubtitle()).ToArray(),
                [Cue(0, null), Cue(2, null)]); // Actual replacement source can contain the old timing with no voice.
            var provider = new DubbingBatchVoiceAssignmentProvider(media, effective, [], true);
            SubtitleData a = Cue(0, null), c = Cue(2, null), unrelated = Cue(4, null);
            provider.Apply(media, [a, c, unrelated]);
            Assert.Equal("pending-source-a", a.AssignedVoiceId);
            Assert.Equal("latest-c", c.AssignedVoiceId);
            Assert.Null(unrelated.AssignedVoiceId);
            var latestClear = DubbingVoicePersistence.MergeEditClaims(
                [new DubbingVoiceClaim(5, TimeSpan.Zero, TimeSpan.FromSeconds(1), null)], claims);
            Assert.Null(Assert.Single(latestClear, claim => claim.Start == TimeSpan.Zero).VoiceId);
            Assert.Empty(queue.SnapshotClaims(Path.Combine(directory, "not-owned.mkv")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("source-a-voice")]
    [InlineData(null)]
    public void Source_replacement_before_debounce_preserves_all_pending_claims_including_clear(string? firstVoice)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string media = Path.Combine(directory, "movie.mkv");
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(media, [Cue(0, "old-a"), Cue(4, "untouched")]);
            using (var queue = new DubbingVoiceSaveQueue(() => true, TimeSpan.FromHours(1)))
            {
                queue.Enqueue(media, [Cue(0, firstVoice)]); // Accepted edit in source S1.
                queue.Enqueue(media, [Cue(2, "source-c-voice")]); // Replace S1 with S2; no timer was allowed to run.
            }
            var saved = DubbingVoiceAssignmentStore.FromJson(File.ReadAllText(DubbingVoiceAssignmentStore.BuildVoicesPath(media)));
            Assert.Contains(saved, entry => entry.StartMs == 2000 && entry.VoiceId == "source-c-voice");
            Assert.Contains(saved, entry => entry.StartMs == 4000 && entry.VoiceId == "untouched");
            if (firstVoice == null) Assert.DoesNotContain(saved, entry => entry.StartMs == 0);
            else Assert.Contains(saved, entry => entry.StartMs == 0 && entry.VoiceId == firstVoice);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("primary-old", "secondary-latest")]
    [InlineData("primary-old", null)]
    [InlineData("secondary-old", "primary-latest")]
    public void Latest_equal_timing_claim_controls_immediate_batch_and_saved_voice_identically(string earlier, string? latest)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string media = Path.Combine(directory, "movie.mkv");
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(media, [Cue(0, "disk-old")]);
            var edits = DubbingVoicePersistence.MergeClaims([Cue(0, latest), Cue(0, earlier)], []);
            var effective = DubbingVoicePersistence.MergeClaims(edits, [Cue(0, earlier)]);
            var provider = new DubbingBatchVoiceAssignmentProvider(media, effective,
                edits.Where(cue => string.IsNullOrWhiteSpace(cue.AssignedVoiceId)).ToArray(), true);
            SubtitleData renderCue = Cue(0, null);
            provider.Apply(media, [renderCue]);
            Assert.Equal(latest, renderCue.AssignedVoiceId);
            using (var queue = new DubbingVoiceSaveQueue(() => true, TimeSpan.FromHours(1))) queue.Enqueue(media, effective);
            string voicesPath = DubbingVoiceAssignmentStore.BuildVoicesPath(media);
            var saved = DubbingVoiceAssignmentStore.FromJson(File.Exists(voicesPath) ? File.ReadAllText(voicesPath) : null);
            if (latest == null) Assert.Empty(saved);
            else Assert.Equal(latest, Assert.Single(saved).VoiceId);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Batch_snapshot_keeps_explicit_clear_before_save_flush_and_still_reads_unrelated_disk_voices()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string media = Path.Combine(directory, "movie.mkv");
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(media, [Cue(0, "old-a"), Cue(2, "saved-b")]);
            var provider = new DubbingBatchVoiceAssignmentProvider(media, [Cue(0, null)], [Cue(0, null)], true);
            SubtitleData a = Cue(0, null), b = Cue(2, null);
            provider.Apply(media, [a, b]);
            Assert.Null(a.AssignedVoiceId);
            Assert.Equal("saved-b", b.AssignedVoiceId);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Clearing_one_loaded_cue_retains_other_slot_and_unloaded_companion_entries()
    {
        var persisted = new VoiceAssignmentEntry[]
        {
            new() { StartMs = 0, EndMs = 1000, VoiceId = "old-a" },
            new() { StartMs = 2000, EndMs = 3000, VoiceId = "old-b" },
            new() { StartMs = 4000, EndMs = 5000, VoiceId = "not-loaded" }
        };
        var merged = DubbingVoicePersistence.Merge([Cue(0, null), Cue(2, "new-b")], persisted);
        var json = DubbingVoiceAssignmentStore.ToJson(merged);
        var entries = DubbingVoiceAssignmentStore.FromJson(json);
        Assert.DoesNotContain(entries, value => value.StartMs == 0);
        Assert.Equal("new-b", Assert.Single(entries, value => value.StartMs == 2000).VoiceId);
        Assert.Equal("not-loaded", Assert.Single(entries, value => value.StartMs == 4000).VoiceId);
    }

    [Fact]
    public void Save_queue_keeps_both_slots_and_clears_only_one_claimed_timing()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string media = Path.Combine(directory, "movie.mkv");
        try
        {
            DubbingVoiceAssignmentStore.SaveAtomic(media, [Cue(4, "not-loaded")]);
            using (var queue = new DubbingVoiceAssignmentSaveQueue(() => true, DubbingVoicePersistence.SaveMerged, TimeSpan.FromHours(1)))
            {
                queue.Enqueue(media, [Cue(0, "slot0")]);
                queue.Enqueue(media, [Cue(2, "slot1"), Cue(0, "slot0")]);
            }
            var saved = DubbingVoiceAssignmentStore.FromJson(File.ReadAllText(DubbingVoiceAssignmentStore.BuildVoicesPath(media)));
            Assert.Equal(3, saved.Count);
            using (var queue = new DubbingVoiceAssignmentSaveQueue(() => true, DubbingVoicePersistence.SaveMerged, TimeSpan.FromHours(1)))
                queue.Enqueue(media, [Cue(0, null), Cue(2, "slot1")]);
            saved = DubbingVoiceAssignmentStore.FromJson(File.ReadAllText(DubbingVoiceAssignmentStore.BuildVoicesPath(media)));
            Assert.Equal(2, saved.Count);
            Assert.DoesNotContain(saved, value => value.StartMs == 0);
            Assert.Contains(saved, value => value.VoiceId == "slot1");
            Assert.Contains(saved, value => value.VoiceId == "not-loaded");
        }
        finally { Directory.Delete(directory, true); }
    }

    static SubtitleData Cue(int startSeconds, string? voice) => new()
    { StartTime = TimeSpan.FromSeconds(startSeconds), EndTime = TimeSpan.FromSeconds(startSeconds + 1), AssignedVoiceId = voice };
}
