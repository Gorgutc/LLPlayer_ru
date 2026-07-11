using AwesomeAssertions;

using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib.MediaPlayer;

public class SubManagerVoiceAssignmentIndexTests
{
    public SubManagerVoiceAssignmentIndexTests()
    {
        Utils.IsTesting = true;
    }

    [Fact]
    public void Load_ReplacesIndexAndSnapshotContainsOnlyOwnedMinimalAssignments()
    {
        SubManager manager = CreateManager();
        manager.Add(Cue(0, "old-voice"));

        SubtitleData unassigned = Cue(1, null, text: "unassigned");
        SubtitleData assigned = Cue(2, "voice-a", text: "assigned");
        assigned.Language = Language.English;
        assigned.SpeakerId = "SPEAKER_00";
        SubtitleData blank = Cue(3, "   ", text: "blank");

        manager.Load([unassigned, assigned, blank]);

        SubtitleData snapshot = manager.SnapshotVoiceAssignments().Should().ContainSingle().Subject;
        snapshot.Should().NotBeSameAs(assigned);
        snapshot.StartTime.Should().Be(assigned.StartTime);
        snapshot.EndTime.Should().Be(assigned.EndTime);
        snapshot.AssignedVoiceId.Should().Be("voice-a");
        snapshot.Index.Should().Be(0);
        snapshot.Text.Should().BeNull();
        snapshot.TranslatedText.Should().BeNull();
        snapshot.Language.Should().BeNull();
        snapshot.SpeakerId.Should().BeNull();

        assigned.StartTime = TimeSpan.FromMinutes(5);
        assigned.AssignedVoiceId = "voice-mutated";

        snapshot.StartTime.Should().Be(TimeSpan.FromSeconds(2));
        snapshot.AssignedVoiceId.Should().Be("voice-a");
    }

    [Fact]
    public void Add_TracksAssignedCue()
    {
        SubManager manager = CreateManager();
        manager.Add(Cue(4, null));
        manager.Add(Cue(5, "voice-a"));

        manager.SnapshotVoiceAssignments()
            .Should().ContainSingle()
            .Which.AssignedVoiceId.Should().Be("voice-a");
    }

    [Fact]
    public void AddRange_TracksEveryAssignedCue()
    {
        SubManager manager = CreateManager();

        manager.AddRange([
            Cue(3, "voice-b"),
            Cue(1, null),
            Cue(2, "voice-a"),
        ]);

        manager.SnapshotVoiceAssignments()
            .Select(sub => sub.AssignedVoiceId)
            .Should().Equal("voice-b", "voice-a");
    }

    [Fact]
    public void SetAssignedVoiceId_AddsUpdatesAndClearsIndexEntry()
    {
        SubManager manager = CreateManager();
        SubtitleData cue = Cue(1, null);
        manager.Load([cue]);

        manager.TrySetAssignedVoiceId(cue, "voice-a").Should().BeTrue();
        manager.SnapshotVoiceAssignments().Should().ContainSingle()
            .Which.AssignedVoiceId.Should().Be("voice-a");

        manager.TrySetAssignedVoiceId(cue, "voice-b").Should().BeTrue();
        manager.SnapshotVoiceAssignments().Should().ContainSingle()
            .Which.AssignedVoiceId.Should().Be("voice-b");

        manager.TrySetAssignedVoiceId(cue, "   ").Should().BeTrue();
        cue.AssignedVoiceId.Should().BeNull();
        manager.SnapshotVoiceAssignments().Should().BeEmpty();
    }

    [Fact]
    public void Clear_RemovesAllIndexEntries()
    {
        SubManager manager = CreateManager();
        manager.Load([Cue(1, "voice-a"), Cue(2, "voice-b")]);

        manager.Clear();

        manager.SnapshotVoiceAssignments().Should().BeEmpty();
    }

    [Fact]
    public void TrySetAssignedVoiceId_RejectsCueRemovedByNewLoad()
    {
        SubManager manager = CreateManager();
        SubtitleData stale = Cue(1, null);
        manager.Load([stale]);
        manager.Load([Cue(2, null)]);

        manager.TrySetAssignedVoiceId(stale, "voice-stale").Should().BeFalse();

        stale.AssignedVoiceId.Should().BeNull();
        manager.SnapshotVoiceAssignments().Should().BeEmpty();
    }

    [Fact]
    public void SnapshotVoiceAssignments_DuplicateTimingPreservesCollectionFirstWinsOrder()
    {
        SubManager manager = CreateManager();
        SubtitleData first = Cue(1, "voice-first");
        SubtitleData second = Cue(1, "voice-second");
        first.Index = 99;
        second.Index = -10;
        manager.Load([first, second]);

        List<SubtitleData> snapshot = manager.SnapshotVoiceAssignments();

        snapshot.Select(sub => sub.AssignedVoiceId).Should().Equal("voice-first", "voice-second");
        DubbingVoiceAssignmentStore.FromJson(DubbingVoiceAssignmentStore.ToJson(snapshot))
            .Should().ContainSingle()
            .Which.VoiceId.Should().Be("voice-first");
    }

    [Fact]
    public void SubtitlesManager_GenerationRejectsSnapshotAfterTrackMutation()
    {
        SubtitlesManager managers = new(new Config(true), 2);
        managers[0].Load([Cue(1, "voice-a")]);
        managers[1].Load([Cue(2, null)]);

        List<SubtitleData> snapshot = managers.SnapshotVoiceAssignments(out long[] generations);
        managers.IsVoiceAssignmentSnapshotCurrent(generations).Should().BeTrue();

        managers[1].Add(Cue(3, "voice-b"));

        snapshot.Should().ContainSingle();
        managers.IsVoiceAssignmentSnapshotCurrent(generations).Should().BeFalse();
    }

    [Fact]
    public void TrySnapshotVoiceAssignments_RetriesInvalidatedCaptureWithoutDroppingEdit()
    {
        SubtitlesManager managers = new(new Config(true), 2);
        managers[0].Load([Cue(1, "voice-a")]);
        managers[1].Load([Cue(2, null)]);
        int validations = 0;

        List<SubtitleData>? snapshot = managers.TrySnapshotVoiceAssignments(
            () =>
            {
                if (Interlocked.Increment(ref validations) == 1)
                    managers[1].Add(Cue(3, "voice-b"));
                return true;
            },
            maxAttempts: 3);

        validations.Should().Be(2);
        snapshot.Should().NotBeNull();
        snapshot!.Select(sub => sub.AssignedVoiceId).Should().Equal("voice-a", "voice-b");
    }

    [Fact]
    public void SubtitlesManager_TrySetAssignedVoiceIdFindsOwnerAndRejectsStaleCue()
    {
        SubtitlesManager managers = new(new Config(true), 2);
        SubtitleData primary = Cue(1, null);
        managers[0].Load([primary]);
        managers[1].Load([Cue(2, null)]);

        managers.TrySetAssignedVoiceId(primary, " voice-a ").Should().BeTrue();
        primary.AssignedVoiceId.Should().Be("voice-a");

        managers[0].Load([Cue(3, null)]);
        managers.TrySetAssignedVoiceId(primary, "voice-stale").Should().BeFalse();
    }

    [Fact]
    public async Task ApplyVoiceAssignments_HoldsIndexLockUntilRebuildCompletes()
    {
        SubManager manager = CreateManager();
        manager.Load([Cue(1, null)]);
        TaskCompletionSource assignmentApplied = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseApply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingAssignmentProvider provider = new(assignmentApplied, releaseApply.Task);

        Task applyTask = Task.Run(
            () => manager.ApplyVoiceAssignments("movie.mkv", provider),
            TestContext.Current.CancellationToken);
        await assignmentApplied.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        TaskCompletionSource snapshotStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<List<SubtitleData>> snapshotTask = Task.Run(
            () =>
            {
                snapshotStarted.TrySetResult();
                return manager.SnapshotVoiceAssignments();
            },
            TestContext.Current.CancellationToken);
        await snapshotStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        try
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            snapshotTask.IsCompleted.Should().BeFalse();
        }
        finally
        {
            releaseApply.TrySetResult();
        }

        await applyTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        List<SubtitleData> snapshot = await snapshotTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        snapshot.Should().ContainSingle().Which.AssignedVoiceId.Should().Be("voice-restored");
    }

    private static SubManager CreateManager() => new(new Config(true), 0);

    private static SubtitleData Cue(int second, string? voiceId, string? text = null) => new()
    {
        Index = second,
        StartTime = TimeSpan.FromSeconds(second),
        EndTime = TimeSpan.FromSeconds(second + 1),
        Text = text,
        AssignedVoiceId = voiceId,
    };

    private sealed class BlockingAssignmentProvider(
        TaskCompletionSource assignmentApplied,
        Task releaseApply) : IDubbingVoiceAssignmentProvider
    {
        public void Apply(string mediaPath, IReadOnlyList<SubtitleData> subtitles)
        {
            subtitles[0].AssignedVoiceId = "voice-restored";
            assignmentApplied.TrySetResult();
            releaseApply.GetAwaiter().GetResult();
        }
    }
}
