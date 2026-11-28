using System.Text.Json;
using FlyleafLib.MediaPlayer.AsrOmissions;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController : IAsrOmissionReviewController
{
    sealed record CompletedControlOmissionSource(ControlRecognitionInput Input, SubtitleCueSource? Subtitle,
        long SubtitleRevision, Guid JobId, AppPaths Paths);

    sealed record NativeOmissionRun(int Slot, PlaybackChoiceMedia Media, PlaybackAudioDelaySource Audio,
        long AudioRevision, long AudioDelay, long SubtitleDelay, AsrOmissionReport PreviousReport);

    sealed record NativeOmissionSource(NativeOmissionRun Run, SubtitleCueSource Subtitle, AsrOmissionReport Report,
        long SubtitleRevision);

    readonly CompletedControlOmissionSource?[] completedOmissionSources = new CompletedControlOmissionSource?[2];
    readonly NativeOmissionRun?[] nativeOmissionRuns = new NativeOmissionRun?[2];

    void RememberControlOmissionSource(ControlRecognitionSlot attachment, Guid jobId, AppPaths paths)
    {
        if (attachment.Observation.IsCancellationRequested || !OwnsControlRecognitionSlot(attachment)) return;
        int slot = attachment.Input.Slot;
        var source = new CompletedControlOmissionSource(attachment.Input, attachment.ProjectedSource ?? attachment.Input.Subtitle,
            Volatile.Read(ref controlSubtitleRevision[slot]), jobId, paths);
        lock (controlRecognitionGate)
            if (ReferenceEquals(controlRecognition[slot], attachment)) completedOmissionSources[slot] = source;
    }

    CompletedControlOmissionSource? CompletedOmissionSource(int slot)
    {
        lock (controlRecognitionGate) return completedOmissionSources[slot];
    }

    bool Current(CompletedControlOmissionSource source)
    {
        int slot = source.Input.Slot;
        return !disposed && ReferenceEquals(CompletedOmissionSource(slot), source)
            && OwnsControlRecognitionAudio(source.Input) && player.Config.Subtitles[slot].Delay == source.Input.SubtitleDelay
            && source.SubtitleRevision == Volatile.Read(ref controlSubtitleRevision[slot])
            && Equals(CaptureSubtitleCueSource(slot), source.Subtitle);
    }

    async Task StartNativeRecognitionWithOmissionSourceAsync(int slot, bool detectAsrOmissions)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        long audioRevision = Volatile.Read(ref controlAudioRevision);
        NativeOmissionRun? run = CapturePlaybackChoiceMedia() is { } media
            && CapturePlaybackAudioDelaySource() is { } audio && audioRevision == Volatile.Read(ref controlAudioRevision)
            ? new(slot, media, audio, audioRevision, player.Config.Audio.Delay, player.Config.Subtitles[slot].Delay,
                player.SubtitlesASR.GetOmissionReport(slot)) : null;

        Volatile.Write(ref nativeOmissionRuns[slot], run);
        Raise(nameof(RecognitionStatus));
        try
        {
            await Recognition.StartAsync(slot, detectAsrOmissions: detectAsrOmissions).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.CompareExchange(ref nativeOmissionRuns[slot], null, run);
            throw;
        }
        finally { Raise(nameof(RecognitionStatus)); }
    }

    bool Current(NativeOmissionRun run) => !disposed && ReferenceEquals(Volatile.Read(ref nativeOmissionRuns[run.Slot]), run)
        && run.AudioRevision == Volatile.Read(ref controlAudioRevision) && IsCurrentPlaybackMedia(run.Media)
        && IsCurrentPlaybackAudioDelaySource(run.Audio) && player.Config.Audio.Delay == run.AudioDelay
        && player.Config.Subtitles[run.Slot].Delay == run.SubtitleDelay;

    NativeOmissionSource? CaptureNativeOmissionSource(int slot)
    {
        if (player.IsASRRunning || player.IsOpening || player.IsMediaResetting
            || Volatile.Read(ref nativeOmissionRuns[slot]) is not { } run || !Current(run)
            || CaptureSubtitleCueSource(slot) is not { IsASR: true } subtitle)
            return null;
        var (report, origin) = player.SubtitlesASR.GetOmissionPlaybackReport(slot);
        return origin == null || origin.MediaGeneration != run.Media.Generation || !ReferenceEquals(origin.Audio, run.Audio.Source)
            || origin.AudioDelay != run.AudioDelay || ReferenceEquals(report, run.PreviousReport) || !Current(run) ? null
            : new(run, subtitle, report, Volatile.Read(ref controlSubtitleRevision[slot]));
    }

    public bool CanReviewAsrOmissions(int slot)
        => !disposed && slot is 0 or 1 && (controlAsrPaths == null
            ? CaptureNativeOmissionSource(slot) != null
            : CompletedOmissionSource(slot) is { } completed && Current(completed));

    public bool IsAsrOmissionReviewCurrent(AsrOmissionReviewSnapshot snapshot)
        => !disposed && snapshot.Slot is 0 or 1 && (snapshot.SourceToken switch
        {
            CompletedControlOmissionSource source => source.Input.Slot == snapshot.Slot && Current(source),
            NativeOmissionSource source => source.Subtitle.Slot == snapshot.Slot
                && CaptureNativeOmissionSource(snapshot.Slot) is { } current
                && ReferenceEquals(source.Report, current.Report) && source == current,
            _ => false
        });

    public async Task<AsrOmissionReviewSnapshot?> ReadAsrOmissionsAsync(int slot, CancellationToken cancellationToken)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        cancellationToken.ThrowIfCancellationRequested();
        if (disposed) return null;
        if (controlAsrPaths != null)
        {
            if (CompletedOmissionSource(slot) is not { } completed || !Current(completed)) return null;
            return await ReadControlOmissionReportAsync(completed, cancellationToken).ConfigureAwait(false);
        }
        if (CaptureNativeOmissionSource(slot) is not { } native) return null;
        var result = new AsrOmissionReviewSnapshot(slot, native.Report.Enabled, native.Report.Status.ToString(),
            Array.AsReadOnly(native.Report.Candidates.Select(candidate => new AsrOmissionReviewRow(candidate.Text,
                candidate.StartMs, candidate.EndMs, candidate.IsReviewOnly, candidate.ReviewOnlyReason is { } reason
                    ? JsonNamingPolicy.CamelCase.ConvertName(reason.ToString()) : null)).ToArray()), native);
        return IsAsrOmissionReviewCurrent(result) ? result : null;
    }

    async Task<AsrOmissionReviewSnapshot?> ReadControlOmissionReportAsync(CompletedControlOmissionSource source,
        CancellationToken cancellationToken)
    {
        using ControlRecognitionConnection connection = await ConnectControlRecognitionAsync(source.Paths, cancellationToken).ConfigureAwait(false);
        var scope = new ControlScope(ControlProtocol.DefaultProfileId);
        var rows = new List<AsrOmissionReviewRow>();
        ControlOmissionSummary? summary = null;
        while (Current(source))
        {
            int offset = rows.Count;
            const int limit = 64;
            ControlResult result = await connection.Operations.InvokeAsync(new(Guid.NewGuid(), "recognition.omissions.read",
                scope, null, ControlProtocol.Encode(new ControlOmissionRead(source.JobId, offset, limit))), cancellationToken).ConfigureAwait(false);
            if (!Current(source)) return null;
            if (result.Error is { Code: "omissions_unavailable" or "unknown_operation" } unavailable)
            {
                bool enabled = source.Input.DetectAsrOmissions;
                return new(source.Input.Slot, enabled,
                    !enabled ? "Disabled" : unavailable.Code == "unknown_operation" ? "Unsupported" : "Unavailable", [], source);
            }
            ThrowControlError(result);
            ControlOmissionPage page = result.Value?.Deserialize<ControlOmissionPage>(ControlProtocol.Json)
                ?? throw new InvalidOperationException("The host did not return an ASR suggestion report.");
            summary ??= page.OmissionDetection;
            if (page.JobId != source.JobId || page.FileIndex != null || page.OmissionDetection != summary
                || summary == null || summary.TotalCount < offset || page.Candidates == null || page.Candidates.Count > limit
                || page.Candidates.Count > summary.TotalCount - offset
                || page.Candidates.Any(candidate => candidate.StartMs < 0 || candidate.EndMs <= candidate.StartMs
                    || string.IsNullOrWhiteSpace(candidate.Text)))
                throw new InvalidOperationException("The host returned an inconsistent ASR suggestion report.");
            rows.AddRange(page.Candidates.Select(candidate => new AsrOmissionReviewRow(candidate.Text,
                candidate.StartMs, candidate.EndMs, candidate.IsReviewOnly, candidate.ReviewOnlyReason)));
            if (page.NextOffset == null && rows.Count == summary.TotalCount)
                return new(source.Input.Slot, summary.Enabled, summary.Status, Array.AsReadOnly(rows.ToArray()), source);
            if (page.NextOffset != rows.Count || rows.Count <= offset || rows.Count >= summary.TotalCount)
                throw new InvalidOperationException("The host did not advance the ASR suggestion report cursor.");
        }
        return null;
    }
}
