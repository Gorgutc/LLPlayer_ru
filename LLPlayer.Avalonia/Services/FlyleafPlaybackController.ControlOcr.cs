using FlyleafLib.MediaPlayer;
using LLPlayer.ControlApi;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    sealed record ControlOcrInput(SubtitleCueSource Source, string Path, int StreamIndex, bool External,
        long Revision, IReadOnlyList<SubtitleData> Cues);

    sealed class ControlOcrRun
    {
        public CancellationTokenSource Observation { get; } = new();
        public TaskCompletionSource<Guid?> Job { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    readonly ControlOcrRun?[] controlOcr = new ControlOcrRun?[2];

    ControlOcrInput? CaptureControlOcrInput(int slot)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        SubtitleCueSource? source = CaptureSubtitleCueSource(slot);
        if (source?.Stream is not { IsBitmap: true, StreamIndex: >= 0 } stream || source.ExternalIO != null
            || !LocalMediaNavigation.TryGetLocalPath(source.ExternalUrl ?? source.NativeUrl, out string path) || !File.Exists(path))
            return null;
        if (source.Manager.IsLoading)
            throw new InvalidOperationException("Wait for the bitmap subtitles to finish loading before running OCR.");
        long revision = Volatile.Read(ref controlSubtitleRevision[slot]);
        var cues = source.Manager.WithCueLock(items => items.Where(cue => cue.IsBitmap).ToArray());
        if (!IsCurrentSubtitleCueSource(source) || revision != Volatile.Read(ref controlSubtitleRevision[slot]))
            throw StaleControlMedia();
        return new(source, path, stream.StreamIndex, source.External != null, revision, cues);
    }

    async Task RunControlOcrAsync(ControlOcrInput input, AppPaths paths)
    {
        var run = new ControlOcrRun();
        int slot = input.Source.Slot;
        lock (controlRecognitionGate)
        {
            if (controlOcr[slot] != null)
                throw new InvalidOperationException("OCR is already active for this subtitle slot.");
            controlOcr[slot] = run;
        }
        Raise(nameof(RecognitionStatus));
        CancellationToken token = run.Observation.Token;
        ControlScope scope = new(ControlProtocol.DefaultProfileId);
        try
        {
            using ControlClient client = await ControlClient.ConnectOrStartAsync(paths.ConfigDir, paths.StateDir, token);
            var args = new { MediaPath = input.Path, input.StreamIndex, input.External, Slot = slot };
            ControlResult accepted = await client.InvokeAsync(new(Guid.NewGuid(), "ocr.start", scope, null,
                ControlProtocol.Encode(args)), token);
            ThrowControlError(accepted);
            Guid jobId = accepted.JobId ?? throw new InvalidOperationException("The host did not return an OCR job.");
            run.Job.TrySetResult(jobId);
            await WaitForControlJobAsync(client, scope, jobId, token);
            (List<SubtitleData> recognized, _) = await ReadRecognizedCuesAsync(client, scope, jobId, token);
            var textByTiming = recognized.GroupBy(cue => (cue.StartTime, cue.EndTime))
                .ToDictionary(group => group.Key, group => new Queue<SubtitleData>(group));
            var source = input.Source;
            await MutateWithTranslationAsync(cancellation =>
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation);
                if (!player.TryMutateMedia(source.Media.Generation, source.Media.Item, () => IsCurrentSubtitleCueSource(source)
                    && input.Revision == Volatile.Read(ref controlSubtitleRevision[slot]), () =>
                {
                    bool applied = source.Manager.WithCueLock(cues =>
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (!cues.Where(cue => cue.IsBitmap).SequenceEqual(input.Cues)) return false;
                        List<(SubtitleData Cue, string Text)> updates = [];
                        foreach (SubtitleData cue in input.Cues)
                            if (textByTiming.TryGetValue((cue.StartTime, cue.EndTime), out var matches)
                                && matches.TryDequeue(out SubtitleData? result))
                                updates.Add((cue, result.Text ?? ""));
                        if (textByTiming.Values.Any(matches => matches.Count != 0))
                            throw new InvalidOperationException("The OCR result timing does not match the attached subtitle track.");
                        foreach (var update in updates) update.Cue.Text = update.Text;
                        return true;
                    });
                    if (!applied) throw StaleControlMedia();
                }, linked.Token)) throw StaleControlMedia();
            });
            QueueCueNotification(slot);
        }
        finally
        {
            run.Job.TrySetResult(null);
            lock (controlRecognitionGate)
            {
                if (ReferenceEquals(controlOcr[slot], run)) controlOcr[slot] = null;
                run.Observation.Dispose();
            }
            Raise(nameof(RecognitionStatus));
        }
    }
}
