using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib.MediaPlayer;

public class AudioStreamPropagationTests
{
    [Fact]
    public async Task ProcessAsync_FreshAsr_PassesResolvedGlobalIndexWithoutResolvingAgain()
    {
        string dir = CreateTempDirectory();
        try
        {
            string video = Path.Combine(dir, "fresh.mkv");
            File.WriteAllText(video, "video");

            var resolver = new RecordingAudioStreamResolver(13);
            var renderer = new RecordingDubbingRenderer();
            var processor = CreateProcessor(
                new FakeAsr(new BatchAsrResult([Cue("privet")], Language.Russian, 7)),
                renderer,
                resolver,
                overwrite: true);

            BatchSubtitleJob job = new(video);
            await processor.ProcessAsync([job], TestContext.Current.CancellationToken);

            job.Status.Should().Be(BatchSubtitleStatus.Completed);
            renderer.Calls.Should().ContainSingle().Which.AudioStreamIndex.Should().Be(7);
            resolver.Calls.Should().Be(0, "fresh ASR already resolved the exact stream for this run");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_ExistingSrt_ResolvesOnceAndPassesExactGlobalIndex()
    {
        string dir = CreateTempDirectory();
        try
        {
            string video = Path.Combine(dir, "existing.mkv");
            File.WriteAllText(video, "video");
            WriteSrt(video);

            var resolver = new RecordingAudioStreamResolver(11);
            var renderer = new RecordingDubbingRenderer();
            var processor = CreateProcessor(
                new ThrowingAsr(),
                renderer,
                resolver,
                overwrite: false);

            BatchSubtitleJob job = new(video);
            await processor.ProcessAsync([job], TestContext.Current.CancellationToken);

            job.Status.Should().Be(BatchSubtitleStatus.Completed);
            resolver.Calls.Should().Be(1);
            renderer.Calls.Should().ContainSingle().Which.AudioStreamIndex.Should().Be(11);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_ExistingSrtAndDub_DoesNotResolveOrRender()
    {
        string dir = CreateTempDirectory();
        try
        {
            string video = Path.Combine(dir, "complete.mkv");
            File.WriteAllText(video, "video");
            WriteSrt(video);
            File.WriteAllText(DubbingOutputPathBuilder.BuildRussianDubPath(video, "flac"), "dub");

            var resolver = new RecordingAudioStreamResolver(5);
            var renderer = new RecordingDubbingRenderer();
            var processor = CreateProcessor(
                new ThrowingAsr(),
                renderer,
                resolver,
                overwrite: false);

            BatchSubtitleJob job = new(video);
            await processor.ProcessAsync([job], TestContext.Current.CancellationToken);

            job.Status.Should().Be(BatchSubtitleStatus.Completed);
            resolver.Calls.Should().Be(0);
            renderer.Calls.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_ExistingSrtWithDubbingDisabled_DoesNotResolve()
    {
        string dir = CreateTempDirectory();
        try
        {
            string video = Path.Combine(dir, "subtitles-only.mkv");
            File.WriteAllText(video, "video");
            WriteSrt(video);

            var resolver = new RecordingAudioStreamResolver(5);
            var processor = new BatchSubtitleProcessor(
                new ThrowingAsr(),
                new NoopTranslator(),
                new NoopWriter(),
                new BatchSubtitleOptions { GenerateDubbing = false },
                audioStreamResolver: resolver);

            BatchSubtitleJob job = new(video);
            await processor.ProcessAsync([job], TestContext.Current.CancellationToken);

            job.Status.Should().Be(BatchSubtitleStatus.Completed);
            resolver.Calls.Should().Be(0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_ExistingSrtWithoutResolver_FailsClosed()
    {
        string dir = CreateTempDirectory();
        try
        {
            string video = Path.Combine(dir, "missing-resolver.mkv");
            File.WriteAllText(video, "video");
            WriteSrt(video);

            var renderer = new RecordingDubbingRenderer();
            var processor = CreateProcessor(
                new ThrowingAsr(),
                renderer,
                resolver: null,
                overwrite: false);

            BatchSubtitleJob job = new(video);
            await processor.ProcessAsync([job], TestContext.Current.CancellationToken);

            job.Status.Should().Be(BatchSubtitleStatus.Failed);
            job.Error.Should().Contain("audio stream resolver");
            renderer.Calls.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RequireResolvedAudioStream_UsesGlobalIndexInNonContiguousMap()
    {
        AudioStream expected = Uninitialized<AudioStream>();
        VideoStream other = Uninitialized<VideoStream>();
        IReadOnlyDictionary<int, StreamBase> streams = new Dictionary<int, StreamBase>
        {
            [0] = other,
            [4] = expected,
        };

        AudioReader.RequireResolvedAudioStream(streams, 4, "movie.mkv").Should().BeSameAs(expected);
    }

    [Fact]
    public void RequireResolvedAudioStream_MissingOrWrongType_FailsClosed()
    {
        VideoStream video = Uninitialized<VideoStream>();
        IReadOnlyDictionary<int, StreamBase> streams = new Dictionary<int, StreamBase> { [4] = video };

        Action missing = () => AudioReader.RequireResolvedAudioStream(streams, 9, "movie.mkv");
        Action wrongType = () => AudioReader.RequireResolvedAudioStream(streams, 4, "movie.mkv");

        missing.Should().Throw<InvalidOperationException>()
            .WithMessage("*audio stream #9*no longer available*movie.mkv*");
        wrongType.Should().Throw<InvalidOperationException>()
            .WithMessage("*stream #4*not an audio stream*movie.mkv*");
    }

    [Fact]
    public void BuildAssembleRequestDto_SerializesRequiredGlobalIndex()
    {
        AssembleRequest request = new(
            "movie.mkv",
            6,
            "movie.ru.dub.flac",
            "flac",
            15,
            1_000,
            []);

        string json = JsonSerializer.Serialize(DubSidecarHost.BuildAssembleRequestDto(request));
        using JsonDocument document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("audio_stream_index").GetInt32().Should().Be(6);
    }

    [Fact]
    public void BuildAssembleRequestDto_NegativeIndex_FailsBeforeHttp()
    {
        AssembleRequest request = new(
            "movie.mkv",
            -1,
            "movie.ru.dub.flac",
            "flac",
            15,
            1_000,
            []);

        Action act = () => DubSidecarHost.BuildAssembleRequestDto(request);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Resolved audio stream index must be non-negative*");
    }

    private static BatchSubtitleProcessor CreateProcessor(
        IBatchAsrTranscriber asr,
        IDubbingRenderer renderer,
        IBatchAudioStreamResolver? resolver,
        bool overwrite)
        => new(
            asr,
            new NoopTranslator(),
            new NoopWriter(),
            new BatchSubtitleOptions
            {
                GenerateDubbing = true,
                DubbingOutputFormat = "flac",
                OverwriteExisting = overwrite,
            },
            progress: null,
            dubber: renderer,
            voiceAssignments: null,
            audioStreamResolver: resolver);

    private static SubtitleData Cue(string text) => new()
    {
        Text = text,
        StartTime = TimeSpan.Zero,
        EndTime = TimeSpan.FromSeconds(1),
    };

    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteSrt(string mediaPath)
        => File.WriteAllText(
            SubtitleOutputPathBuilder.BuildRussianSrtPath(mediaPath),
            "1\n00:00:00,000 --> 00:00:01,000\nprivet\n");

    private static T Uninitialized<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class FakeAsr(BatchAsrResult result) : IBatchAsrTranscriber
    {
        public Task<BatchAsrResult> TranscribeAsync(
            string mediaPath,
            CancellationToken token,
            IProgress<BatchAsrProgress>? asrProgress = null)
            => Task.FromResult(result);
    }

    private sealed class ThrowingAsr : IBatchAsrTranscriber
    {
        public Task<BatchAsrResult> TranscribeAsync(
            string mediaPath,
            CancellationToken token,
            IProgress<BatchAsrProgress>? asrProgress = null)
            => throw new InvalidOperationException("ASR must not run for existing SRT");
    }

    private sealed class RecordingAudioStreamResolver(int streamIndex) : IBatchAudioStreamResolver
    {
        public int Calls { get; private set; }

        public Task<int> ResolveAudioStreamIndexAsync(string mediaPath, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(streamIndex);
        }
    }

    private sealed class RecordingDubbingRenderer : IDubbingRenderer
    {
        public List<(string MediaPath, int AudioStreamIndex, string OutputPath)> Calls { get; } = [];

        public Task RenderAsync(
            IReadOnlyList<SubtitleData> translatedSubtitles,
            string mediaPath,
            int resolvedAudioStreamIndex,
            string outputPath,
            IProgress<DubbingProgress>? progress,
            CancellationToken token)
        {
            Calls.Add((mediaPath, resolvedAudioStreamIndex, outputPath));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopTranslator : IBatchSubtitleTranslator
    {
        public Task TranslateAsync(IList<SubtitleData> subtitles, Language sourceLanguage, CancellationToken token)
            => Task.CompletedTask;
    }

    private sealed class NoopWriter : IBatchSubtitleWriter
    {
        public Task WriteAsync(
            IReadOnlyList<SubtitleData> subtitles,
            string outputPath,
            bool overwrite,
            CancellationToken token)
            => Task.CompletedTask;
    }
}
