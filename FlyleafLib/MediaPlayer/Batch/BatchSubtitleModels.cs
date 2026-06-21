using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public enum BatchSubtitleStatus
{
    Pending,
    RunningASR,
    QueuedForTranslation,
    Translating,
    Saving,
    Completed,
    Skipped,
    Failed,
    Canceled
}

public sealed class BatchSubtitleOptions
{
    public bool Recursive { get; init; }
    public bool OverwriteExisting { get; init; }
    public bool Utf8Bom { get; init; } = true;
    public TargetLanguage TargetLanguage { get; init; } = TargetLanguage.Russian;
}

public sealed class BatchSubtitleJob : NotifyPropertyChanged
{
    public BatchSubtitleJob(string mediaPath)
    {
        MediaPath = mediaPath;
        OutputPath = SubtitleOutputPathBuilder.BuildRussianSrtPath(mediaPath);
    }

    public string MediaPath { get; }
    public string OutputPath { get; }

    public BatchSubtitleStatus Status { get; set => Set(ref field, value); } = BatchSubtitleStatus.Pending;
    public string? Error { get; set => Set(ref field, value); }
    public int SubtitleCount { get; set => Set(ref field, value); }
    public DateTimeOffset? StartedAt { get; set => Set(ref field, value); }
    public DateTimeOffset? CompletedAt { get; set => Set(ref field, value); }
}

public sealed record BatchAsrResult(IReadOnlyList<SubtitleData> Subtitles, Language SourceLanguage);

public interface IBatchAsrTranscriber
{
    Task<BatchAsrResult> TranscribeAsync(string mediaPath, CancellationToken token);
}

public interface IBatchSubtitleTranslator
{
    Task TranslateAsync(IList<SubtitleData> subtitles, Language sourceLanguage, CancellationToken token);
}

public interface IBatchSubtitleWriter
{
    Task WriteAsync(
        IReadOnlyList<SubtitleData> subtitles,
        string outputPath,
        bool overwrite,
        CancellationToken token);
}

public sealed record BatchSubtitleProgress(
    BatchSubtitleJob Job,
    BatchSubtitleStatus Status,
    string? Error = null,
    int? SubtitleCount = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null);
