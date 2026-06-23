using System.Linq;
using System.Threading.Channels;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib.MediaPlayer.Batch;

#nullable enable

public sealed class BatchSubtitleProcessor
{
    private readonly IBatchAsrTranscriber _asrTranscriber;
    private readonly IBatchSubtitleTranslator _translator;
    private readonly IBatchSubtitleWriter _writer;
    private readonly BatchSubtitleOptions _options;
    private readonly IProgress<BatchSubtitleProgress>? _progress;
    private readonly IDubbingRenderer? _dubber;

    public BatchSubtitleProcessor(
        IBatchAsrTranscriber asrTranscriber,
        IBatchSubtitleTranslator translator,
        IBatchSubtitleWriter writer,
        BatchSubtitleOptions options,
        IProgress<BatchSubtitleProgress>? progress = null,
        IDubbingRenderer? dubber = null)
    {
        _asrTranscriber = asrTranscriber;
        _translator = translator;
        _writer = writer;
        _options = options;
        _progress = progress;
        _dubber = dubber;
    }

    private bool DubbingEnabled => _options.GenerateDubbing && _dubber is not null;

    public async Task ProcessAsync(IReadOnlyList<BatchSubtitleJob> jobs, CancellationToken token)
    {
        // Force serialize-mode when dubbing is on: the GPU TTS render must never overlap the next file's
        // ASR (that would re-create the ASR/translate GPU contention the serialize/CPU-fallback work removed).
        if (_options.SerializeAsrAndTranslate || DubbingEnabled)
            await ProcessSerialAsync(jobs, token);
        else
            await ProcessPipelinedAsync(jobs, token);
    }

    // Background-friendly path: each file is taken fully through ASR -> translate -> save before the next
    // file's ASR starts, so ASR and translation never run concurrently (no double GPU load). Whether ASR runs
    // on GPU or CPU is decided per audio chunk inside the engine (see BatchAsrTranscriber's CPU-fallback).
    private async Task ProcessSerialAsync(IReadOnlyList<BatchSubtitleJob> jobs, CancellationToken token)
    {
        try
        {
            foreach (BatchSubtitleJob job in jobs)
            {
                if (token.IsCancellationRequested)
                {
                    MarkCanceled(job);
                    continue;
                }

                try
                {
                    // Already done? (SRT exists, and the dub too when dubbing is on.) When the SRT exists
                    // but the dub is missing, this renders the dub from the existing SRT without re-running
                    // ASR/translate. Inside the try so a dub-render error/cancel is per-job, not whole-batch.
                    if (await TryCompleteFromExistingAsync(job, token))
                        continue;

                    Report(job, BatchSubtitleStatus.RunningASR, startedAt: DateTimeOffset.Now);

                    IProgress<BatchAsrProgress>? asrProgress = _progress is null
                        ? null
                        : new AsrProgressForwarder(job, _progress);

                    BatchAsrResult result = await _asrTranscriber.TranscribeAsync(job.MediaPath, token, asrProgress);
                    token.ThrowIfCancellationRequested();

                    if (result.Subtitles.Count == 0)
                    {
                        Report(job, BatchSubtitleStatus.Skipped, error: "No speech detected.", completedAt: DateTimeOffset.Now);
                        continue;
                    }

                    Report(job, BatchSubtitleStatus.QueuedForTranslation, subtitleCount: result.Subtitles.Count);

                    await TranslateAndSaveAsync(job, result, token);
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
            if (token.IsCancellationRequested)
            {
                foreach (BatchSubtitleJob pending in jobs.Where(j => j.Status == BatchSubtitleStatus.Pending))
                {
                    MarkCanceled(pending);
                }
            }
        }
    }

    private async Task ProcessPipelinedAsync(IReadOnlyList<BatchSubtitleJob> jobs, CancellationToken token)
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

                try
                {
                    // A usable translation already exists — report Completed (not Skipped) so the dialog
                    // shows the file as done. (Dubbing forces the serial path, so the dub-from-existing
                    // branch never triggers here; kept for parity. Skipped is reserved for "no speech".)
                    if (await TryCompleteFromExistingAsync(job, token))
                        continue;

                    Report(job, BatchSubtitleStatus.RunningASR, startedAt: DateTimeOffset.Now);

                    IProgress<BatchAsrProgress>? asrProgress = _progress is null
                        ? null
                        : new AsrProgressForwarder(job, _progress);

                    BatchAsrResult result = await _asrTranscriber.TranscribeAsync(job.MediaPath, token, asrProgress);
                    token.ThrowIfCancellationRequested();

                    if (result.Subtitles.Count == 0)
                    {
                        // Valid media with no detectable speech (e.g. music-only) is not a failure — skip
                        // it (no output file is written) instead of marking the whole job Failed.
                        Report(job, BatchSubtitleStatus.Skipped, error: "No speech detected.", completedAt: DateTimeOffset.Now);
                        continue;
                    }

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
                await TranslateAndSaveAsync(job, result, token);
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

    // Translate (unless already Russian) then write the .ru.srt. Throws on error/cancel; callers map that to
    // Failed/Canceled. Shared by the pipelined worker and the serial path.
    private async Task TranslateAndSaveAsync(BatchSubtitleJob job, BatchAsrResult result, CancellationToken token)
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

        await DubIfEnabledAsync(job, subtitles, token);

        Report(job, BatchSubtitleStatus.Completed, completedAt: DateTimeOffset.Now);
    }

    // When the .ru.srt already exists (overwrite off), the file is "done" for subtitles. Returns true and
    // reports Completed in that case — but first, if dubbing is on and the dub is still missing, renders the
    // dub FROM the existing SRT (no re-ASR/translate), so "translate now, dub later" works on a re-run.
    // Returns false when the job still needs full ASR/translate processing. Throws on dub error/cancel
    // (the caller's per-job try/catch maps that to Failed/Canceled).
    private async Task<bool> TryCompleteFromExistingAsync(BatchSubtitleJob job, CancellationToken token)
    {
        if (_options.OverwriteExisting || !SubtitleOutputPathBuilder.OutputExists(job.OutputPath))
            return false;

        if (DubbingEnabled)
        {
            string dubPath = DubbingOutputPathBuilder.BuildRussianDubPath(job.MediaPath, _options.DubbingOutputFormat);
            if (!DubbingOutputPathBuilder.OutputExists(dubPath))
            {
                List<SubtitleData> existing = DubbingSrtReader.ParseFile(job.OutputPath);
                if (existing.Count > 0)
                    await DubIfEnabledAsync(job, existing, token);
            }
        }

        Report(job, BatchSubtitleStatus.Completed, completedAt: DateTimeOffset.Now);
        return true;
    }

    // Optional dub render after the .ru.srt is written, gated on its OWN .ru.dub output (independent of the
    // .srt overwrite check). Throws on error/cancel; the caller maps that to Failed/Canceled.
    private async Task DubIfEnabledAsync(BatchSubtitleJob job, IReadOnlyList<SubtitleData> subtitles, CancellationToken token)
    {
        if (!DubbingEnabled)
            return;

        token.ThrowIfCancellationRequested();

        string dubPath = DubbingOutputPathBuilder.BuildRussianDubPath(job.MediaPath, _options.DubbingOutputFormat);
        if (!_options.OverwriteExisting && DubbingOutputPathBuilder.OutputExists(dubPath))
            return;

        Report(job, BatchSubtitleStatus.Dubbing);

        IProgress<DubbingProgress>? dubProgress = _progress is null
            ? null
            : new DubProgressForwarder(job, _progress);

        await _dubber!.RenderAsync(subtitles, job.MediaPath, dubPath, dubProgress, token);
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

    // Forwards dub-render progress through the same UI-thread IProgress sink so the row/tray shows
    // "Dubbing N/M" liveness instead of a frozen status (mirrors AsrProgressForwarder).
    private sealed class DubProgressForwarder(BatchSubtitleJob job, IProgress<BatchSubtitleProgress> sink)
        : IProgress<DubbingProgress>
    {
        public void Report(DubbingProgress p) => sink.Report(new BatchSubtitleProgress(
            job,
            BatchSubtitleStatus.Dubbing,
            AsrSegmentText: $"{p.Phase} {p.CompletedLines}/{p.TotalLines}"));
    }
}
