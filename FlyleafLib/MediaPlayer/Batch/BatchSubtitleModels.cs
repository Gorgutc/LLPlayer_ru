using System.Collections.ObjectModel;
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

    // Background-friendly mode: process each file fully (ASR -> translate -> save) before starting the next
    // file's ASR, instead of overlapping translation of one file with ASR of the next. This guarantees ASR
    // and translation never run at the same time, so a GPU ASR engine and a local-LLM translator do not both
    // saturate the GPU at once. Default false (library default = the faster pipelined behaviour); the app
    // turns it on via config.
    public bool SerializeAsrAndTranslate { get; init; }
}

public sealed class BatchSubtitleJob : NotifyPropertyChanged
{
    public BatchSubtitleJob(string mediaPath, string? rootFolder = null)
    {
        MediaPath = mediaPath;
        OutputPath = SubtitleOutputPathBuilder.BuildRussianSrtPath(mediaPath);
        FolderDisplay = BuildFolderDisplay(mediaPath, rootFolder);
    }

    public string MediaPath { get; }
    public string OutputPath { get; }

    // Just the file name (no directory) for the list — the containing folder is already shown in the group
    // header, so the full path only made the actual name unreadable. Full paths remain available as tooltips.
    public string FileName => Path.GetFileName(MediaPath);
    public string OutputFileName => Path.GetFileName(OutputPath);

    // The sub-folder this file lives in, relative to the scanned root — used to GROUP the batch list by
    // folder so a recursive scan visibly runs folder-by-folder. A file directly in the scanned root is
    // labelled with the root folder's own name; deeper files show the relative sub-folder path. Falls back
    // to the full directory when no root is known (e.g. unit tests construct jobs without a root).
    public string FolderDisplay { get; }

    private static string BuildFolderDisplay(string mediaPath, string? rootFolder)
    {
        string dir = Path.GetDirectoryName(mediaPath) ?? string.Empty;

        if (string.IsNullOrEmpty(rootFolder))
        {
            return dir;
        }

        string rel = Path.GetRelativePath(rootFolder, dir);
        if (rel is "." or "")
        {
            return Path.GetFileName(rootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return rel;
    }

    // UI selection: whether this file is processed when the batch runs. Default true.
    // Auto-cleared at scan time for files that already have a translation.
    public bool Include { get; set => Set(ref field, value); } = true;

    public BatchSubtitleStatus Status { get; set => Set(ref field, value); } = BatchSubtitleStatus.Pending;
    public string? Error { get; set => Set(ref field, value); }
    public int SubtitleCount { get; set => Set(ref field, value); }
    public DateTimeOffset? StartedAt { get; set => Set(ref field, value); }
    public DateTimeOffset? CompletedAt { get; set => Set(ref field, value); }

    // Liveness feedback — updated on the UI thread from the VM progress handler.
    public TimeSpan? ProcessedTime { get; set => Set(ref field, value); }
    public TimeSpan? TotalDuration { get; set => Set(ref field, value); }
    public double Progress { get; set => Set(ref field, value); }
    public bool IsIndeterminateProgress { get; set => Set(ref field, value); }
    public string Throughput { get; set => Set(ref field, value); } = string.Empty;
    // Wall-clock elapsed + estimated remaining (ETA) for this file's ASR. Refreshed once per second by the VM
    // heartbeat so the UI keeps moving even between (slow) ASR segment reports. ETA is approximate.
    public TimeSpan? Elapsed { get; set => Set(ref field, value); }
    public TimeSpan? Eta { get; set => Set(ref field, value); }
    public ObservableCollection<string> Transcript { get; } = new();
}

public sealed record BatchAsrResult(IReadOnlyList<SubtitleData> Subtitles, Language SourceLanguage);

// Incremental per-segment progress reported during ASR so the UI can show live feedback.
public sealed record BatchAsrProgress(
    string MediaPath,
    int SubtitleCount,
    string Text,
    TimeSpan Position,
    TimeSpan Duration);

public interface IBatchAsrTranscriber
{
    Task<BatchAsrResult> TranscribeAsync(
        string mediaPath,
        CancellationToken token,
        IProgress<BatchAsrProgress>? asrProgress = null);
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
    DateTimeOffset? CompletedAt = null,
    // Streaming ASR liveness — set only on per-segment reports:
    string? AsrSegmentText = null,
    TimeSpan? ProcessedTime = null,
    TimeSpan? TotalDuration = null);
