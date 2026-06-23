using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib.MediaPlayer;

public class DubbingHookTests
{
    [Fact]
    public async Task ProcessAsync_DubbingEnabled_RendersDubAfterWrite()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string video = Path.Combine(dir, "movie.mkv");
            File.WriteAllText(video, "video");

            var asr = new FakeAsr(_ => Task.FromResult(
                new BatchAsrResult([CreateSub("привет")], Language.Russian)));
            var renderer = new FakeDubbingRenderer();

            var processor = new BatchSubtitleProcessor(
                asr,
                new NoopTranslator(),
                new NoopWriter(),
                new BatchSubtitleOptions { GenerateDubbing = true, DubbingOutputFormat = "flac" },
                progress: null,
                dubber: renderer);

            await processor.ProcessAsync([new BatchSubtitleJob(video)], TestContext.Current.CancellationToken);

            renderer.Calls.Should().ContainSingle();
            renderer.Calls[0].Output.Should().Be(DubbingOutputPathBuilder.BuildRussianDubPath(video, "flac"));
            renderer.Calls[0].Count.Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_ExistingDub_NotOverwritten_SkipsRender()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string video = Path.Combine(dir, "movie.mkv");
            File.WriteAllText(video, "video");
            File.WriteAllText(DubbingOutputPathBuilder.BuildRussianDubPath(video, "flac"), "existing dub");

            var renderer = new FakeDubbingRenderer();
            var processor = new BatchSubtitleProcessor(
                new FakeAsr(_ => Task.FromResult(new BatchAsrResult([CreateSub("привет")], Language.Russian))),
                new NoopTranslator(),
                new NoopWriter(),
                new BatchSubtitleOptions { GenerateDubbing = true, DubbingOutputFormat = "flac", OverwriteExisting = false },
                progress: null,
                dubber: renderer);

            await processor.ProcessAsync([new BatchSubtitleJob(video)], TestContext.Current.CancellationToken);

            renderer.Calls.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_DubberNull_NeverDubs()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string video = Path.Combine(dir, "movie.mkv");
            File.WriteAllText(video, "video");

            var processor = new BatchSubtitleProcessor(
                new FakeAsr(_ => Task.FromResult(new BatchAsrResult([CreateSub("привет")], Language.Russian))),
                new NoopTranslator(),
                new NoopWriter(),
                new BatchSubtitleOptions { GenerateDubbing = true });

            // No dubber supplied => additive guarantee: the run completes without dubbing.
            BatchSubtitleJob job = new(video);
            await processor.ProcessAsync([job], TestContext.Current.CancellationToken);

            job.Status.Should().Be(BatchSubtitleStatus.Completed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_ExistingSrt_NoDub_DubsFromSrtWithoutReAsr()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string video = Path.Combine(dir, "movie.mkv");
            File.WriteAllText(video, "video");
            // A translated .ru.srt already exists; the dub does not.
            File.WriteAllText(
                SubtitleOutputPathBuilder.BuildRussianSrtPath(video),
                "1\n00:00:00,000 --> 00:00:02,000\nPrivet\n\n2\n00:00:03,000 --> 00:00:05,000\nPoka\n");

            var renderer = new FakeDubbingRenderer();
            var processor = new BatchSubtitleProcessor(
                // ASR must NOT run on a re-run when the SRT already exists.
                new FakeAsr(_ => throw new InvalidOperationException("ASR must not be invoked on a dub-from-existing run")),
                new NoopTranslator(),
                new NoopWriter(),
                new BatchSubtitleOptions { GenerateDubbing = true, DubbingOutputFormat = "flac", OverwriteExisting = false },
                progress: null,
                dubber: renderer);

            BatchSubtitleJob job = new(video);
            await processor.ProcessAsync([job], TestContext.Current.CancellationToken);

            renderer.Calls.Should().ContainSingle();
            renderer.Calls[0].Count.Should().Be(2); // both SRT lines parsed and dubbed
            job.Status.Should().Be(BatchSubtitleStatus.Completed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static SubtitleData CreateSub(string text) => new()
    {
        Text = text,
        StartTime = TimeSpan.Zero,
        EndTime = TimeSpan.FromSeconds(1)
    };

    private sealed class FakeAsr(Func<string, Task<BatchAsrResult>> transcribe) : IBatchAsrTranscriber
    {
        public Task<BatchAsrResult> TranscribeAsync(string mediaPath, CancellationToken token, IProgress<BatchAsrProgress>? asrProgress = null)
            => transcribe(mediaPath);
    }

    private sealed class NoopTranslator : IBatchSubtitleTranslator
    {
        public Task TranslateAsync(IList<SubtitleData> subtitles, Language sourceLanguage, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class NoopWriter : IBatchSubtitleWriter
    {
        public Task WriteAsync(IReadOnlyList<SubtitleData> subtitles, string outputPath, bool overwrite, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class FakeDubbingRenderer : IDubbingRenderer
    {
        public List<(string Media, string Output, int Count)> Calls { get; } = [];

        public Task RenderAsync(
            IReadOnlyList<SubtitleData> translatedSubtitles,
            string mediaPath,
            string outputPath,
            IProgress<DubbingProgress>? progress,
            CancellationToken token)
        {
            Calls.Add((mediaPath, outputPath, translatedSubtitles.Count));
            return Task.CompletedTask;
        }
    }
}
