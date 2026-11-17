using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Batch;

namespace LLPlayer.ControlHost;

internal sealed record ControlBatchRow(string MediaPath, int? StreamIndex, string SubtitlePath,
    string? DubPath, long? SourceLength = null, long? SourceModifiedUtcTicks = null, ControlBatchReceipt? Receipt = null);

internal sealed record ControlBatchReceipt(BatchSubtitleStatus Status, string? Error, int SubtitleCount,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

internal sealed record ControlBatchCheckpoint(BatchSettings Settings, ControlBatchRow[] Rows,
    string RecognitionIdentity, string TranslationIdentity, string DubbingIdentity, bool Utf8Bom, Guid? WatchEpoch,
    ControlBatchVoiceReference? LiveVoices = null);

internal sealed record ControlBatchVoiceReference(Guid RequestId, string Fingerprint, string MediaPath);

/// <summary>Maps the existing Batch writers to the owning job's per-file publication receipts.</summary>
internal sealed class ControlBatchFiles(ControlJobContext context, ControlBatchCheckpoint checkpoint)
{
    private readonly ControlJobContext job = context;
    private readonly bool overwrite = checkpoint.Settings.OverwriteExisting;
    private readonly Dictionary<string, ControlBatchRow> bySubtitle = checkpoint.Rows
        .ToDictionary(row => row.SubtitlePath, BatchWorkflow.PathComparer);
    private readonly HashSet<string> recoveredSubtitles = new(BatchWorkflow.PathComparer);
    private readonly Dictionary<string, string> existingSubtitles = new(BatchWorkflow.PathComparer);

    public BatchOutputOwnership Outputs => new(
        writer => new SubtitleWriter(this, writer),
        renderer => new DubWriter(this, renderer),
        recoveredSubtitles.Contains);

    public async Task<bool> RecoverRowAsync(ControlBatchRow row, CancellationToken token)
    {
        FileInfo source = new(row.MediaPath);
        if (source.Exists && (source.Length != row.SourceLength || source.LastWriteTimeUtc.Ticks != row.SourceModifiedUtcTicks))
            throw new ControlJobException("input_changed", "A selected batch media file changed after submission.",
                LLPlayer.ControlApi.ControlJobStatus.Interrupted);
        await job.VerifyInputAsync(row.MediaPath, token).ConfigureAwait(false);
        bool subtitle = await job.TryRecoverFileAsync(Effect(row, "subtitle"), row.SubtitlePath, overwrite, token)
            .ConfigureAwait(false);
        if (subtitle)
            recoveredSubtitles.Add(row.SubtitlePath);
        else if (!overwrite && SubtitleOutputPathBuilder.OutputExists(row.SubtitlePath))
        {
            await job.VerifyInputAsync(row.SubtitlePath, token).ConfigureAwait(false);
            existingSubtitles.Add(row.MediaPath, row.SubtitlePath);
        }

        bool dub = row.DubPath == null || await job.TryRecoverFileAsync(Effect(row, "dub"), row.DubPath, overwrite, token)
            .ConfigureAwait(false);
        if (!dub && !overwrite && DubbingOutputPathBuilder.ResolveExistingRussianDubPath(row.MediaPath) is { } existingDub)
        {
            await job.VerifyInputAsync(existingDub, token).ConfigureAwait(false);
            dub = true;
        }

        if (row.Receipt?.Status == BatchSubtitleStatus.Completed && (!(subtitle || existingSubtitles.ContainsKey(row.MediaPath)) || !dub))
            throw new ControlJobException("output_changed", "A completed batch file's output is no longer available.",
                LLPlayer.ControlApi.ControlJobStatus.Interrupted);
        return row.Receipt is not null || ((subtitle || existingSubtitles.ContainsKey(row.MediaPath)) && dub);
    }

    private static string Effect(ControlBatchRow row, string kind) => "batch:" + kind + ":" + row.MediaPath;

    private sealed class SubtitleWriter(ControlBatchFiles owner, IBatchSubtitleWriter writer) : IBatchSubtitleWriter
    {
        public async Task WriteAsync(IReadOnlyList<SubtitleData> subtitles, string outputPath, bool overwrite,
            CancellationToken token)
        {
            ControlBatchRow row = owner.bySubtitle[outputPath];
            await owner.job.VerifyInputAsync(row.MediaPath, token).ConfigureAwait(false);
            await owner.job.WriteAndPublishFileAsync(Effect(row, "subtitle"), outputPath, overwrite, async (stage, ct) =>
            {
                await writer.WriteAsync(subtitles, stage, true, ct).ConfigureAwait(false);
                await owner.job.VerifyInputAsync(row.MediaPath, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
    }

    private sealed class DubWriter(ControlBatchFiles owner, IDubbingRenderer renderer) : IDubbingRenderer
    {
        public async Task RenderAsync(IReadOnlyList<SubtitleData> subtitles, string mediaPath, string outputPath,
            IProgress<DubbingProgress>? progress, CancellationToken token)
        {
            string subtitlePath = SubtitleOutputPathBuilder.BuildRussianSrtPath(mediaPath);
            ControlBatchRow row = owner.bySubtitle[subtitlePath];
            if (!BatchWorkflow.PathComparer.Equals(row.DubPath, outputPath))
                throw new InvalidOperationException("The batch dub destination differs from its accepted output path.");
            await owner.job.VerifyInputAsync(row.MediaPath, token).ConfigureAwait(false);
            if (owner.existingSubtitles.TryGetValue(mediaPath, out string? existing))
                await owner.job.VerifyInputAsync(existing, token).ConfigureAwait(false);
            if (owner.recoveredSubtitles.Contains(subtitlePath))
                await owner.job.TryRecoverFileAsync(Effect(row, "subtitle"), subtitlePath, owner.overwrite, token).ConfigureAwait(false);
            await owner.job.WriteAndPublishFileAsync(Effect(row, "dub"), outputPath, owner.overwrite, async (stage, ct) =>
            {
                await renderer.RenderAsync(subtitles, mediaPath, stage, progress, ct).ConfigureAwait(false);
                if (!DubbingOutputPathBuilder.OutputExists(stage))
                    throw new IOException("The dubbing engine produced no output.");
                await owner.job.VerifyInputAsync(row.MediaPath, ct).ConfigureAwait(false);
                if (existing != null) await owner.job.VerifyInputAsync(existing, ct).ConfigureAwait(false);
                if (owner.recoveredSubtitles.Contains(subtitlePath))
                    await owner.job.TryRecoverFileAsync(Effect(row, "subtitle"), subtitlePath, owner.overwrite, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => renderer.DisposeAsync();
    }
}
