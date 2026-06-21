using System.Collections.Concurrent;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib.MediaPlayer;

public class BatchSubtitleProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldSkipExistingOutputAndContinueAfterFileFailure()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string skippedVideo = Path.Combine(tempDir, "skip.mkv");
            string failedVideo = Path.Combine(tempDir, "failed.mkv");
            string russianVideo = Path.Combine(tempDir, "russian.mkv");
            File.WriteAllText(skippedVideo, "");
            File.WriteAllText(failedVideo, "");
            File.WriteAllText(russianVideo, "");
            File.WriteAllText(SubtitleOutputPathBuilder.BuildRussianSrtPath(skippedVideo), "already exists");

            BatchSubtitleJob[] jobs =
            [
                new(skippedVideo),
                new(failedVideo),
                new(russianVideo)
            ];

            var asr = new FakeAsrTranscriber(path =>
            {
                if (path == failedVideo)
                    throw new InvalidOperationException("no audio stream");

                return Task.FromResult(new BatchAsrResult(
                    [CreateSub("hello")],
                    Language.Russian));
            });

            var writer = new MemorySubtitleWriter();
            var processor = new BatchSubtitleProcessor(
                asr,
                new FakeBatchTranslator(),
                writer,
                new BatchSubtitleOptions { OverwriteExisting = false });

            await processor.ProcessAsync(jobs, CancellationToken.None);

            jobs[0].Status.Should().Be(BatchSubtitleStatus.Skipped);
            jobs[1].Status.Should().Be(BatchSubtitleStatus.Failed);
            jobs[1].Error.Should().Contain("no audio stream");
            jobs[2].Status.Should().Be(BatchSubtitleStatus.Completed);
            writer.Writes.Should().ContainSingle(w => w.Path == jobs[2].OutputPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_ShouldStartNextAsrWhilePreviousFileIsTranslating()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string firstVideo = Path.Combine(tempDir, "first.mkv");
            string secondVideo = Path.Combine(tempDir, "second.mkv");
            File.WriteAllText(firstVideo, "");
            File.WriteAllText(secondVideo, "");

            TaskCompletionSource firstTranslationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource allowFirstTranslation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource secondAsrStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var asr = new FakeAsrTranscriber(path =>
            {
                if (path == secondVideo)
                    secondAsrStarted.SetResult();

                return Task.FromResult(new BatchAsrResult(
                    [CreateSub(Path.GetFileNameWithoutExtension(path))],
                    Language.English));
            });

            var translator = new FakeBatchTranslator(async (subtitles, _, token) =>
            {
                if (subtitles[0].Text == "first")
                {
                    firstTranslationStarted.SetResult();
                    await allowFirstTranslation.Task.WaitAsync(token);
                }

                subtitles[0].TranslatedText = "translated " + subtitles[0].Text;
            });

            var processor = new BatchSubtitleProcessor(
                asr,
                translator,
                new MemorySubtitleWriter(),
                new BatchSubtitleOptions());

            Task processing = processor.ProcessAsync(
                [new BatchSubtitleJob(firstVideo), new BatchSubtitleJob(secondVideo)],
                CancellationToken.None);

            await firstTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await secondAsrStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            allowFirstTranslation.SetResult();
            await processing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_ShouldMarkRunningAndPendingJobsCanceled()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string firstVideo = Path.Combine(tempDir, "first.mkv");
            string secondVideo = Path.Combine(tempDir, "second.mkv");
            File.WriteAllText(firstVideo, "");
            File.WriteAllText(secondVideo, "");

            using CancellationTokenSource cts = new();
            var asr = new FakeAsrTranscriber(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

            BatchSubtitleJob[] jobs =
            [
                new(firstVideo),
                new(secondVideo)
            ];

            var processor = new BatchSubtitleProcessor(
                asr,
                new FakeBatchTranslator(),
                new MemorySubtitleWriter(),
                new BatchSubtitleOptions());

            await processor.ProcessAsync(jobs, cts.Token);

            jobs[0].Status.Should().Be(BatchSubtitleStatus.Canceled);
            jobs[1].Status.Should().Be(BatchSubtitleStatus.Canceled);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_ShouldKeepCancellationFromBeingReportedAsAsrFailure()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string video = Path.Combine(tempDir, "video.mkv");
            File.WriteAllText(video, "");

            using CancellationTokenSource cts = new();
            var asr = new FakeAsrTranscriber(_ =>
            {
                cts.Cancel();
                return Task.FromResult(new BatchAsrResult([], Language.Unknown));
            });

            BatchSubtitleJob job = new(video);
            var processor = new BatchSubtitleProcessor(
                asr,
                new FakeBatchTranslator(),
                new MemorySubtitleWriter(),
                new BatchSubtitleOptions());

            await processor.ProcessAsync([job], cts.Token);

            job.Status.Should().Be(BatchSubtitleStatus.Canceled);
            job.Error.Should().BeNull();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static SubtitleData CreateSub(string text) => new()
    {
        Text = text,
        StartTime = TimeSpan.Zero,
        EndTime = TimeSpan.FromSeconds(1)
    };

    private sealed class FakeAsrTranscriber(Func<string, Task<BatchAsrResult>> transcribe)
        : IBatchAsrTranscriber
    {
        public Task<BatchAsrResult> TranscribeAsync(string mediaPath, CancellationToken token)
            => transcribe(mediaPath);
    }

    private sealed class FakeBatchTranslator(
        Func<IList<SubtitleData>, Language, CancellationToken, Task>? translate = null)
        : IBatchSubtitleTranslator
    {
        public Task TranslateAsync(IList<SubtitleData> subtitles, Language sourceLanguage, CancellationToken token)
            => translate?.Invoke(subtitles, sourceLanguage, token) ?? Task.CompletedTask;
    }

    private sealed class MemorySubtitleWriter : IBatchSubtitleWriter
    {
        public ConcurrentBag<(string Path, string[] Lines)> Writes { get; } = [];

        public Task WriteAsync(
            IReadOnlyList<SubtitleData> subtitles,
            string outputPath,
            bool overwrite,
            CancellationToken token)
        {
            Writes.Add((outputPath, subtitles.Select(s => s.DisplayText ?? string.Empty).ToArray()));
            return Task.CompletedTask;
        }
    }
}
