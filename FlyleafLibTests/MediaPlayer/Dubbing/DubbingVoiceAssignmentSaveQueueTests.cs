using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer.Dubbing;

[CollectionDefinition("CurrentDirectorySensitive", DisableParallelization = true)]
public sealed class CurrentDirectorySensitiveCollection;

[Collection("CurrentDirectorySensitive")]
public class DubbingVoiceAssignmentSaveQueueTests
{
    [Fact]
    public async Task Enqueue_DifferentMediaWithinDebounce_WritesBothMedia()
    {
        List<(string MediaPath, IReadOnlyList<SubtitleData> Subtitles)> writes = [];
        using DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (mediaPath, subtitles) => writes.Add((mediaPath, subtitles.Select(Clone).ToList())),
            TimeSpan.FromMilliseconds(25));

        string mediaA = NewMediaPath("a.mkv");
        string mediaB = NewMediaPath("b.mkv");

        queue.Enqueue(mediaA, [Cue(1, 2, "voice-a")]);
        queue.Enqueue(mediaB, [Cue(3, 4, "voice-b")]);

        await Task.Delay(250, TestContext.Current.CancellationToken);

        writes.Select(w => w.MediaPath).Should().BeEquivalentTo([mediaA, mediaB]);
        writes.Single(w => w.MediaPath == mediaA).Subtitles.Single().AssignedVoiceId.Should().Be("voice-a");
        writes.Single(w => w.MediaPath == mediaB).Subtitles.Single().AssignedVoiceId.Should().Be("voice-b");
    }

    [Fact]
    public async Task Enqueue_SameMediaWithinDebounce_WritesOnlyLatestSnapshot()
    {
        List<IReadOnlyList<SubtitleData>> writes = [];
        using DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (_, subtitles) => writes.Add(subtitles.Select(Clone).ToList()),
            TimeSpan.FromMilliseconds(25));

        string media = NewMediaPath("movie.mkv");

        queue.Enqueue(media, [Cue(1, 2, "voice-old")]);
        queue.Enqueue(media, [Cue(1, 2, "voice-new")]);

        await Task.Delay(250, TestContext.Current.CancellationToken);

        writes.Should().ContainSingle();
        writes[0].Single().AssignedVoiceId.Should().Be("voice-new");
    }

    [Fact]
    public void Dispose_FlushesPendingRequestsForAllMedia()
    {
        List<string> writes = [];
        DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (mediaPath, _) => writes.Add(mediaPath),
            TimeSpan.FromMinutes(5));

        string mediaA = NewMediaPath("a.mkv");
        string mediaB = NewMediaPath("b.mkv");

        queue.Enqueue(mediaA, [Cue(1, 2, "voice-a")]);
        queue.Enqueue(mediaB, [Cue(3, 4, "voice-b")]);

        queue.Dispose();

        writes.Should().BeEquivalentTo([mediaA, mediaB]);
    }

    [Fact]
    public async Task Dispose_WaitsForSaveAlreadyInProgress()
    {
        TaskCompletionSource saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool saveFinished = false;
        using DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (_, _) =>
            {
                saveStarted.SetResult();
                releaseSave.Task.Wait();
                saveFinished = true;
            },
            TimeSpan.Zero);

        queue.Enqueue(NewMediaPath("movie.mkv"), [Cue(1, 2, "voice-a")]);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Task disposeTask = Task.Run(queue.Dispose, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        disposeTask.IsCompleted.Should().BeFalse();

        releaseSave.SetResult();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        saveFinished.Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_WaitsForSaveClaimedButQueuedBehindAnotherSave()
    {
        TaskCompletionSource saveAStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource saveBClaimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSaveA = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool saveBFinished = false;
        string mediaA = NewMediaPath("a.mkv");
        string mediaB = NewMediaPath("b.mkv");
        using DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (mediaPath, _) =>
            {
                if (mediaPath == mediaA)
                {
                    saveAStarted.SetResult();
                    releaseSaveA.Task.Wait();
                }
                else if (mediaPath == mediaB)
                {
                    saveBFinished = true;
                }
            },
            TimeSpan.Zero,
            mediaPath =>
            {
                if (mediaPath == mediaB)
                    saveBClaimed.SetResult();
            });

        queue.Enqueue(mediaA, [Cue(1, 2, "voice-a")]);
        await saveAStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        queue.Enqueue(mediaB, [Cue(3, 4, "voice-b")]);
        await saveBClaimed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Task disposeTask = Task.Run(queue.Dispose, TestContext.Current.CancellationToken);
        releaseSaveA.SetResult();

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        saveBFinished.Should().BeTrue();
    }

    [Fact]
    public async Task Enqueue_DoesNotWaitForSlowSaveAlreadyInProgress()
    {
        TaskCompletionSource saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string mediaA = NewMediaPath("a.mkv");
        string mediaB = NewMediaPath("b.mkv");
        using DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (mediaPath, _) =>
            {
                if (mediaPath == mediaA)
                {
                    saveStarted.SetResult();
                    releaseSave.Task.Wait();
                }
            },
            TimeSpan.Zero);

        queue.Enqueue(mediaA, [Cue(1, 2, "voice-a")]);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Task enqueueB = Task.Run(() => queue.Enqueue(mediaB, [Cue(3, 4, "voice-b")]), TestContext.Current.CancellationToken);

        try
        {
            await enqueueB.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseSave.SetResult();
        }
    }

    [Fact]
    public async Task Save_ClaimedBehindAnotherSave_DoesNotRunAfterPersistenceTurnsOff()
    {
        int enabled = 1;
        TaskCompletionSource saveAStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource saveBClaimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSaveA = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> writes = [];
        string mediaA = NewMediaPath("a.mkv");
        string mediaB = NewMediaPath("b.mkv");
        using DubbingVoiceAssignmentSaveQueue queue = new(
            () => Volatile.Read(ref enabled) != 0,
            (mediaPath, _) =>
            {
                writes.Add(mediaPath);
                if (mediaPath == mediaA)
                {
                    saveAStarted.SetResult();
                    releaseSaveA.Task.Wait();
                }
            },
            TimeSpan.Zero,
            mediaPath =>
            {
                if (mediaPath == mediaB)
                    saveBClaimed.SetResult();
            });

        queue.Enqueue(mediaA, [Cue(1, 2, "voice-a")]);
        await saveAStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        queue.Enqueue(mediaB, [Cue(3, 4, "voice-b")]);
        await saveBClaimed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Volatile.Write(ref enabled, 0);

        Task disposeTask = Task.Run(queue.Dispose, TestContext.Current.CancellationToken);
        releaseSaveA.SetResult();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        writes.Should().Equal(mediaA);
    }

    [Fact]
    public async Task Save_NewerSameMediaRequestSupersedesClaimedOlderRequest()
    {
        TaskCompletionSource blockerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource oldClaimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource newClaimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseBlocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string?> mediaWrites = [];
        string blocker = NewMediaPath("blocker.mkv");
        string media = NewMediaPath("movie.mkv");
        int mediaClaims = 0;
        using DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (mediaPath, subtitles) =>
            {
                if (mediaPath == blocker)
                {
                    blockerStarted.SetResult();
                    releaseBlocker.Task.Wait();
                }
                else if (mediaPath == media)
                {
                    mediaWrites.Add(subtitles.Single().AssignedVoiceId);
                }
            },
            TimeSpan.Zero,
            mediaPath =>
            {
                if (mediaPath != media)
                    return;

                if (Interlocked.Increment(ref mediaClaims) == 1)
                    oldClaimed.SetResult();
                else
                    newClaimed.SetResult();
            });

        queue.Enqueue(blocker, [Cue(0, 1, "blocker")]);
        await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        queue.Enqueue(media, [Cue(1, 2, "voice-old")]);
        await oldClaimed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        queue.Enqueue(media, [Cue(1, 2, "voice-new")]);
        await newClaimed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Task disposeTask = Task.Run(queue.Dispose, TestContext.Current.CancellationToken);
        releaseBlocker.SetResult();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        mediaWrites.Should().ContainSingle().Which.Should().Be("voice-new");
    }

    [Fact]
    public void Enqueue_TakesOwnershipWithoutEnumeratingOrCloningSnapshot()
    {
        CountingReadOnlyList snapshot = new([Cue(1, 2, "voice-a")]);
        IReadOnlyList<SubtitleData>? savedSnapshot = null;
        DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (_, subtitles) => savedSnapshot = subtitles,
            TimeSpan.FromMinutes(5));

        queue.Enqueue(NewMediaPath("movie.mkv"), snapshot);
        queue.Dispose();

        snapshot.EnumerationCount.Should().Be(0);
        savedSnapshot.Should().BeSameAs(snapshot);
    }

    [Fact]
    public async Task Save_CaptureAliasesResolvingToSameMedia_WritesOnlyLatestSnapshot()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"llp-voice-alias-{Guid.NewGuid():N}");
        string media = Path.Combine(dir, "movie.mkv");
        string blocker = NewMediaPath("blocker.mkv");
        Directory.CreateDirectory(dir);
        File.WriteAllText(media, string.Empty);

        TaskCompletionSource blockerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource oldClaimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource newClaimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseBlocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string?> mediaWrites = [];
        int mediaClaims = 0;
        DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (mediaPath, subtitles) =>
            {
                if (mediaPath == blocker)
                {
                    blockerStarted.SetResult();
                    releaseBlocker.Task.Wait();
                }
                else if (mediaPath == media)
                {
                    mediaWrites.Add(subtitles.Single().AssignedVoiceId);
                }
            },
            TimeSpan.Zero,
            mediaPath =>
            {
                if (mediaPath != media)
                    return;

                if (Interlocked.Increment(ref mediaClaims) == 1)
                    oldClaimed.SetResult();
                else
                    newClaimed.SetResult();
            });

        try
        {
            queue.Enqueue(blocker, [Cue(0, 1, "blocker")]);
            await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            queue.Enqueue(
                DubbingVoiceAssignmentMediaTarget.Capture(Path.Combine(dir, "missing-old.mkv"), media, null),
                [Cue(1, 2, "voice-old")]);
            await oldClaimed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            queue.Enqueue(
                DubbingVoiceAssignmentMediaTarget.Capture(Path.Combine(dir, "missing-new.mkv"), media, null),
                [Cue(1, 2, "voice-new")]);
            await newClaimed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Task disposeTask = Task.Run(queue.Dispose, TestContext.Current.CancellationToken);
            releaseBlocker.SetResult();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            mediaWrites.Should().ContainSingle().Which.Should().Be("voice-new");
        }
        finally
        {
            releaseBlocker.TrySetResult();
            queue.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dispose_FlushesCapturedTargetOffCallingThread()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"llp-voice-dispose-{Guid.NewGuid():N}");
        string media = Path.Combine(dir, "movie.mkv");
        Directory.CreateDirectory(dir);
        File.WriteAllText(media, string.Empty);

        int callerThread = Environment.CurrentManagedThreadId;
        int saveThread = callerThread;
        DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (_, _) => saveThread = Environment.CurrentManagedThreadId,
            TimeSpan.FromMinutes(5));

        try
        {
            queue.Enqueue(DubbingVoiceAssignmentMediaTarget.Capture(media, null, null), [Cue(1, 2, "voice-a")]);

            queue.Dispose();

            saveThread.Should().NotBe(callerThread);
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Capture_RelativeAndAbsoluteCandidates_HaveSameQueueIdentity()
    {
        string absolute = Path.GetFullPath(Path.Combine("media", "movie.mkv"));
        string relative = Path.GetRelativePath(Environment.CurrentDirectory, absolute);
        string fallbackAbsolute = Path.GetFullPath(Path.Combine("media", "fallback.mkv"));
        string fallbackRelative = Path.GetRelativePath(Environment.CurrentDirectory, fallbackAbsolute);

        DubbingVoiceAssignmentMediaTarget.Capture(absolute, fallbackAbsolute, null).QueueKey
            .Should().Be(DubbingVoiceAssignmentMediaTarget.Capture(relative, fallbackRelative, null).QueueKey);
    }

    [Fact]
    public void Enqueue_SameMissingFirstCandidateWithDifferentFallbackMedia_WritesBoth()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"llp-voice-fallback-{Guid.NewGuid():N}");
        string missing = Path.Combine(dir, "missing.mkv");
        string mediaA = Path.Combine(dir, "a.mkv");
        string mediaB = Path.Combine(dir, "b.mkv");
        Directory.CreateDirectory(dir);
        File.WriteAllText(mediaA, string.Empty);
        File.WriteAllText(mediaB, string.Empty);
        List<string> writes = [];
        DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (mediaPath, _) => writes.Add(mediaPath),
            TimeSpan.FromMinutes(5));

        try
        {
            queue.Enqueue(
                DubbingVoiceAssignmentMediaTarget.Capture(missing, mediaA, null),
                [Cue(1, 2, "voice-a")]);
            queue.Enqueue(
                DubbingVoiceAssignmentMediaTarget.Capture(missing, mediaB, null),
                [Cue(3, 4, "voice-b")]);

            queue.Dispose();

            writes.Should().BeEquivalentTo([mediaA, mediaB]);
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Capture_RelativeCandidateResolvesAgainstCaptureDirectoryAfterCurrentDirectoryChanges()
    {
        string originalDirectory = Environment.CurrentDirectory;
        string root = Path.Combine(Path.GetTempPath(), $"llp-voice-cwd-{Guid.NewGuid():N}");
        string captureDirectory = Path.Combine(root, "capture");
        string laterDirectory = Path.Combine(root, "later");
        Directory.CreateDirectory(captureDirectory);
        Directory.CreateDirectory(laterDirectory);
        string media = Path.Combine(captureDirectory, "movie.mkv");
        File.WriteAllText(media, string.Empty);

        try
        {
            Environment.CurrentDirectory = captureDirectory;
            DubbingVoiceAssignmentMediaTarget target =
                DubbingVoiceAssignmentMediaTarget.Capture("movie.mkv", null, null);

            Environment.CurrentDirectory = laterDirectory;

            target.ResolveLocalMediaPath().Should().Be(media);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Save_ClaimHookThrows_ReleasesActiveSaveAndDisposeCompletes()
    {
        TaskCompletionSource hookCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes = 0;
        DubbingVoiceAssignmentSaveQueue queue = new(
            () => true,
            (_, _) => Interlocked.Increment(ref writes),
            TimeSpan.Zero,
            _ =>
            {
                hookCalled.SetResult();
                throw new InvalidOperationException("test hook failure");
            });

        queue.Enqueue(NewMediaPath("movie.mkv"), [Cue(1, 2, "voice-a")]);
        await hookCalled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await Task.Run(queue.Dispose, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        writes.Should().Be(0);
    }

    private static SubtitleData Cue(double startSec, double endSec, string? voiceId) => new()
    {
        StartTime = TimeSpan.FromSeconds(startSec),
        EndTime = TimeSpan.FromSeconds(endSec),
        AssignedVoiceId = voiceId,
    };

    private static SubtitleData Clone(SubtitleData sub) => new()
    {
        StartTime = sub.StartTime,
        EndTime = sub.EndTime,
        AssignedVoiceId = sub.AssignedVoiceId,
    };

    private static string NewMediaPath(string fileName)
        => Path.Combine(Path.GetTempPath(), $"llp-voice-save-{Guid.NewGuid():N}", fileName);

    private sealed class CountingReadOnlyList(IReadOnlyList<SubtitleData> items) : IReadOnlyList<SubtitleData>
    {
        public int EnumerationCount { get; private set; }
        public int Count => items.Count;
        public SubtitleData this[int index] => items[index];

        public IEnumerator<SubtitleData> GetEnumerator()
        {
            EnumerationCount++;
            return items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
