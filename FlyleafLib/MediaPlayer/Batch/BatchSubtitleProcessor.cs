using System.Linq;
using System.Threading.Channels;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public sealed class BatchSubtitleProcessor
{
    private readonly IBatchAsrTranscriber _asrTranscriber;
    private readonly IBatchSubtitleTranslator _translator;
    private readonly IBatchSubtitleWriter _writer;
    private readonly BatchSubtitleOptions _options;
    private readonly IProgress<BatchSubtitleProgress>? _progress;

    public BatchSubtitleProcessor(
        IBatchAsrTranscriber asrTranscriber,
        IBatchSubtitleTranslator translator,
        IBatchSubtitleWriter writer,
        BatchSubtitleOptions options,
        IProgress<BatchSubtitleProgress>? progress = null)
    {
        _asrTranscriber = asrTranscriber;
        _translator = translator;
        _writer = writer;
        _options = options;
        _progress = progress;
    }

    public async Task ProcessAsync(IReadOnlyList<BatchSubtitleJob> jobs, CancellationToken token)
    {
        Channel<(BatchSubtitleJob Job, BatchAsrResult Result)> channel =
            Channel.CreateBounded<(BatchSubtitleJob, BatchAsrResult)>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });

        Task translationTask = Task.Run(
            async () => await TranslateAndSaveWorkerAsync(channel.Reader, token),
            CancellationToken.None);

        try
        {
            foreach (BatchSubtitleJob job in jobs)
            {
                if (token.IsCancellationRequested)
                {
                    MarkCanceled(job);
                    continue;
                }

                if (!_options.OverwriteExisting && File.Exists(job.OutputPath))
                {
                    Report(job, BatchSubtitleStatus.Skipped, completedAt: DateTimeOffset.Now);
                    continue;
                }

                try
                {
                    Report(job, BatchSubtitleStatus.RunningASR, startedAt: DateTimeOffset.Now);

                    IProgress<BatchAsrProgress>? asrProgress = _progress is null
                        ? null
                        : new AsrProgressForwarder(job, _progress);

                    BatchAsrResult result = await _asrTranscriber.TranscribeAsync(job.MediaPath, token, asrProgress);
                    token.ThrowIfCancellationRequested();

                    if (result.Subtitles.Count == 0)
                        throw new InvalidOperationException("ASR did not produce subtitles.");

                    Report(job, BatchSubtitleStatus.QueuedForTranslation, subtitleCount: result.Subtitles.Count);

                    await channel.Writer.WriteAsync((job, result), token);
                }
                catch (OperationCanceledException)
                {
                    MarkCanceled(job);
                    break;
                }
                catch (Exception ex)
                {
                    MarkFailed(job, ex);
                }
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            await translationTask;

            if (token.IsCancellationRequested)
            {
                foreach (BatchSubtitleJob pending in jobs.Where(j => j.Status == BatchSubtitleStatus.Pending))
                {
                    MarkCanceled(pending);
                }
            }
        }
    }

    private async Task TranslateAndSaveWorkerAsync(
        ChannelReader<(BatchSubtitleJob Job, BatchAsrResult Result)> reader,
        CancellationToken token)
    {
        await foreach ((BatchSubtitleJob job, BatchAsrResult result) in reader.ReadAllAsync(CancellationToken.None))
        {
            try
            {
                token.ThrowIfCancellationRequested();

                if (result.SourceLanguage == Language.Unknown)
                    throw new InvalidOperationException("ASR could not detect source language.");

                List<SubtitleData> subtitles = result.Subtitles
                    .OrderBy(s => s.StartTime)
                    .Select((s, index) =>
                    {
                        s.Index = index;
                        return s;
                    })
                    .ToList();

                if (result.SourceLanguage.ISO6391 != TargetLanguageRussianIso)
                {
                    Report(job, BatchSubtitleStatus.Translating);
                    await _translator.TranslateAsync(subtitles, result.SourceLanguage, token);
                }

                Report(job, BatchSubtitleStatus.Saving);
                await _writer.WriteAsync(subtitles, job.OutputPath, _options.OverwriteExisting, token);

                Report(job, BatchSubtitleStatus.Completed, completedAt: DateTimeOffset.Now);
            }
            catch (OperationCanceledException)
            {
                MarkCanceled(job);
            }
            catch (Exception ex)
            {
                MarkFailed(job, ex);
            }
        }
    }

    private static string TargetLanguageRussianIso => "ru";

    private void MarkCanceled(BatchSubtitleJob job)
    {
        Report(job, BatchSubtitleStatus.Canceled, completedAt: DateTimeOffset.Now);
    }

    private void MarkFailed(BatchSubtitleJob job, Exception ex)
    {
        Report(job, BatchSubtitleStatus.Failed, error: ex.Message, completedAt: DateTimeOffset.Now);
    }

    private void Report(
        BatchSubtitleJob job,
        BatchSubtitleStatus status,
        string? error = null,
        int? subtitleCount = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null)
    {
        job.Status = status;
        if (error != null)
            job.Error = error;
        if (subtitleCount.HasValue)
            job.SubtitleCount = subtitleCount.Value;
        if (startedAt.HasValue)
            job.StartedAt = startedAt.Value;
        if (completedAt.HasValue)
            job.CompletedAt = completedAt.Value;

        _progress?.Report(new BatchSubtitleProgress(
            job,
            status,
            error,
            subtitleCount,
            startedAt,
            completedAt));
    }

    // Forwards per-segment ASR progress as a BatchSubtitleProgress so it flows through the same
    // UI-thread IProgress sink (the VM marshals it onto the dispatcher).
    private sealed class AsrProgressForwarder(BatchSubtitleJob job, IProgress<BatchSubtitleProgress> sink)
        : IProgress<BatchAsrProgress>
    {
        public void Report(BatchAsrProgress p) => sink.Report(new BatchSubtitleProgress(
            job,
            BatchSubtitleStatus.RunningASR,
            SubtitleCount: p.SubtitleCount,
            AsrSegmentText: p.Text,
            ProcessedTime: p.Position,
            TotalDuration: p.Duration));
    }
}
