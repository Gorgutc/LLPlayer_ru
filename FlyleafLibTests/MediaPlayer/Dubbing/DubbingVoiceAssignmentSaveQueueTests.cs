using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer.Dubbing;

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
}
