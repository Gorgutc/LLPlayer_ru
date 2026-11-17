using System.Text.Json;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    internal sealed record ControlRecognitionConnection(IPlayerControlService Operations, IControlJobService Jobs,
        IDisposable? Lifetime = null) : IDisposable
    {
        public void Dispose() => Lifetime?.Dispose();
    }

    sealed record ControlRecognitionInput(PlaybackChoiceMedia Media, PlaybackAudioDelaySource Audio,
        int Slot, SubtitleCueSource? Subtitle, long SubtitleRevision, long AudioRevision, long SubtitleDelay, long AudioDelay,
        string? Path, int StreamIndex, long StartTicks, ControlNetworkAudio? NetworkAudio = null);

    sealed class ControlRecognitionRun(ControlRecognitionInput input)
    {
        public ControlRecognitionInput Input { get; } = input;
        public TaskCompletionSource<Guid?> Job { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Observation { get; } = new();
        public CancellationTokenSource Preparation { get; } = new();
        public ControlJobStatus Status { get; set; } = ControlJobStatus.Accepted;
        public bool IsPaused { get; set; }
        public bool StopRequested { get; set; }
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ControlRecognitionDocument? Latest { get; set; }
        public Exception? Error { get; set; }
    }

    sealed record ControlRecognitionDocument(string Revision, IReadOnlyList<SubtitleData> Cues, Language? Language);

    sealed class ControlRecognitionSlot(ControlRecognitionRun run, ControlRecognitionInput input)
    {
        public ControlRecognitionRun Run { get; } = run;
        public ControlRecognitionInput Input { get; } = input;
        public CancellationTokenSource Observation { get; } = new();
        public SubtitleCueSource? ProjectedSource { get; set; }
        public IReadOnlyList<SubtitleData> ProjectedCues { get; set; } = [];
        public string? ProjectedRevision { get; set; }
    }

    readonly Lock controlRecognitionGate = new();
    readonly ControlRecognitionSlot?[] controlRecognition = new ControlRecognitionSlot?[2];
    readonly long[] controlSubtitleRevision = new long[2];
    long controlAudioRevision;
    AppPaths? controlAsrPaths;
    AppPaths? controlRecognitionPaths;
    Func<CancellationToken, Task<ControlRecognitionConnection>>? connectRecognitionForTest;

    internal void ConfigureControlRecognition(AppPaths paths, Func<CancellationToken, Task<ControlRecognitionConnection>>? connect = null)
    {
        ConfigureControlAsr(paths);
        controlRecognitionPaths = paths;
        connectRecognitionForTest = connect;
    }

    internal void ConfigureControlAsr(AppPaths paths) => controlAsrPaths = paths;

    async Task<ControlRecognitionConnection> ConnectControlRecognitionAsync(AppPaths paths, CancellationToken token = default)
    {
        if (connectRecognitionForTest is { } connect) return await connect(token);
        ControlClient client = await ControlClient.ConnectOrStartAsync(paths.ConfigDir, paths.StateDir, token);
        return new(client, client, client);
    }

    internal void DetachControlRecognition()
    {
        lock (controlRecognitionGate)
        {
            foreach (ControlRecognitionSlot? slot in controlRecognition)
                slot?.Observation.Cancel();
            foreach (ControlOcrRun? run in controlOcr)
                run?.Observation.Cancel();
        }
    }

    string ControlRecognitionStatus
    {
        get
        {
            lock (controlRecognitionGate)
            {
                var statuses = controlRecognition.OfType<ControlRecognitionSlot>().Select(slot =>
                    $"{(slot.Input.Slot == 0 ? "Primary" : "Secondary")} ASR: {(slot.Run.IsPaused ? "Paused" : slot.Run.Status.ToString())}");
                return string.Join(" · ", statuses.Concat(controlOcr.Select((run, slot) => run == null ? null :
                    $"{(slot == 0 ? "Primary" : "Secondary")} OCR: Running").OfType<string>()));
            }
        }
    }

    ControlRecognitionInput CaptureControlRecognitionInput(int slot)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        ObjectDisposedException.ThrowIf(disposed || player.IsDisposed, this);
        if (CanCancelOpen || player.IsOpening || player.IsMediaResetting)
            throw new ControlException(new("media_opening", "Wait for the current media to finish opening before starting recognition."));
        PlaybackChoiceMedia media = CapturePlaybackChoiceMedia()
            ?? throw new ControlException(new("audio_required", "Open media with an audio track before starting recognition."));

        ControlRecognitionInput? input = null;
        if (!player.TryMutateMedia(media.Generation, media.Item, () => IsCurrentPlaybackMedia(media), () =>
        {
            PlaybackAudioDelaySource? audio = CapturePlaybackAudioDelaySource();
            var stream = player.decoder.AudioStream;
            if (!player.Audio.IsOpened || audio == null || stream is not { StreamIndex: >= 0 })
                throw new ControlException(new("audio_required", "Open media with an audio track before starting recognition."));
            if (player.IsLive || stream.Demuxer.IsLive)
                throw new ControlException(new("live_source", "Recognition is unavailable for live streams."));
            if (player.Playlist.IOStream != null || media.Item.IOStream != null || audio.ExternalIO != null)
                throw new ControlException(new("unsupported_source", "The selected audio input is process-local and cannot be read by ControlHost."));

            bool hasLocalPath = LocalMediaNavigation.TryGetLocalPath(stream.Demuxer.Url, out string path);
            if (hasLocalPath && !File.Exists(path))
                throw new ControlException(new("source_unavailable", "The selected local audio file is unavailable."));
            ControlNetworkAudio? network = hasLocalPath ? null : CaptureControlNetworkAudio(media, audio);
            input = new(media, audio, slot, CaptureSubtitleCueSource(slot),
                Volatile.Read(ref controlSubtitleRevision[slot]), Volatile.Read(ref controlAudioRevision),
                player.Config.Subtitles[slot].Delay, player.Config.Audio.Delay, hasLocalPath ? path : null,
                stream.StreamIndex, Math.Max(0L, player.CurTime), network);
        }))
            throw new RecognitionActionCanceledException("Recognition was canceled because the audio source changed.");
        return input!;
    }

    bool OwnsControlRecognitionAudio(ControlRecognitionInput input)
        => IsCurrentPlaybackMedia(input.Media) && IsCurrentPlaybackAudioDelaySource(input.Audio)
            && Volatile.Read(ref controlAudioRevision) == input.AudioRevision
            && player.Config.Audio.Delay == input.AudioDelay;

    bool OwnsControlRecognitionSlot(ControlRecognitionSlot slot)
    {
        ControlRecognitionInput input = slot.Input;
        if (!OwnsControlRecognitionAudio(input) || player.Config.Subtitles[input.Slot].Delay != input.SubtitleDelay)
            return false;
        if (slot.ProjectedSource is not { } projected)
            return Volatile.Read(ref controlSubtitleRevision[input.Slot]) == input.SubtitleRevision
                && Equals(CaptureSubtitleCueSource(input.Slot), input.Subtitle);
        return IsCurrentSubtitleCueSource(projected)
            && projected.Manager.WithCueLock(cues => cues.SequenceEqual(slot.ProjectedCues));
    }

    static bool SameControlAudio(ControlRecognitionInput first, ControlRecognitionInput second)
        => first.Media == second.Media && first.Audio == second.Audio && first.AudioRevision == second.AudioRevision
            && first.AudioDelay == second.AudioDelay && first.StreamIndex == second.StreamIndex;

    async Task StartControlRecognitionAsync(ControlRecognitionInput input, AppPaths paths)
    {
        ControlRecognitionSlot attachment;
        ControlRecognitionSlot? initial;
        lock (controlRecognitionGate) initial = controlRecognition[input.Slot];
        bool ownsInitial = initial != null && OwnsControlRecognitionSlot(initial);
        bool start;
        lock (controlRecognitionGate)
        {
            if (controlRecognition[input.Slot] is { } previous)
            {
                if (!ReferenceEquals(previous, initial) || ownsInitial)
                    throw new InvalidOperationException("Recognition is already active for this subtitle slot.");
                previous.Observation.Cancel();
            }
            ControlRecognitionRun? shared = controlRecognition[(input.Slot + 1) % 2]?.Run;
            start = shared == null || shared.StopRequested || (shared.Finished.Task.IsCompleted && shared.Error != null)
                || !SameControlAudio(shared.Input, input);
            attachment = new(start ? new(input) : shared!, input);
            controlRecognition[input.Slot] = attachment;
        }
        Raise(nameof(RecognitionStatus));

        ControlRecognitionRun run = attachment.Run;
        CancellationToken token = attachment.Observation.Token;
        if (start) _ = ObserveControlRecognitionRunAsync(run, paths);
        try
        {
            ControlRecognitionDocument? latest;
            lock (controlRecognitionGate) latest = run.Latest;
            if (latest != null) await ProjectControlRecognitionAsync(attachment, latest, token);
            await run.Finished.Task.WaitAsync(token);
            if (run.Error is { } error) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        finally
        {
            lock (controlRecognitionGate)
            {
                if (ReferenceEquals(controlRecognition[input.Slot], attachment)) controlRecognition[input.Slot] = null;
                attachment.Observation.Dispose();
                if (!controlRecognition.Any(slot => ReferenceEquals(slot?.Run, run)) && !run.Finished.Task.IsCompleted)
                    run.Observation.Cancel();
            }
            Raise(nameof(RecognitionStatus));
        }
    }

    async Task ObserveControlRecognitionRunAsync(ControlRecognitionRun run, AppPaths paths)
    {
        ControlRecognitionInput input = run.Input;
        CancellationToken token = run.Observation.Token;
        var scope = new ControlScope(ControlProtocol.DefaultProfileId);
        ControlPreparedAudio? prepared = null;
        bool preserveSnapshot = false;
        try
        {
            using ControlRecognitionConnection client = await ConnectControlRecognitionAsync(paths, token);
            Guid requestId = Guid.NewGuid();
            using (var preparation = CancellationTokenSource.CreateLinkedTokenSource(token, run.Preparation.Token))
            {
                prepared = await PrepareControlAudioAsync(input, paths, requestId, preparation.Token);
                preparation.Token.ThrowIfCancellationRequested();
            }
            if (prepared.IsSnapshot && !OwnsControlRecognitionAudio(input))
                throw new InvalidOperationException("The selected audio source changed before recognition.");

            var args = new { MediaPath = prepared.Path, prepared.StreamIndex, input.StartTicks, input.Slot,
                prepared.SourceRevision, prepared.SourceLanguage };
            // After submission begins, a lost HTTP response cannot prove the host did not accept the input.
            preserveSnapshot = true;
            ControlResult accepted = await client.Operations.InvokeAsync(new(requestId, "recognition.start", scope,
                ExpectedRevision: null, ControlProtocol.Encode(args)), token);
            if (accepted is { JobId: null, Error: not null }) preserveSnapshot = false;
            ThrowControlError(accepted);
            Guid jobId = accepted.JobId ?? throw new InvalidOperationException("The host did not return a recognition job.");
            run.Job.TrySetResult(jobId);

            long previewRevision = -1;
            while (true)
            {
                ControlJob job = await client.Jobs.GetAsync(scope, jobId, token)
                    ?? throw new InvalidOperationException("The host could not find the accepted job.");
                lock (controlRecognitionGate) run.Status = job.Status;
                Raise(nameof(RecognitionStatus));
                if (job.Status == ControlJobStatus.Completed)
                {
                    await PublishControlRecognitionAsync(run, await ReadControlDocumentAsync(client.Operations, scope, jobId, token));
                    return;
                }
                ThrowIfControlJobStopped(job);
                if (job.Status == ControlJobStatus.Running && previewRevision != job.Revision)
                {
                    previewRevision = job.Revision;
                    try { await PublishControlRecognitionAsync(run, await ReadControlDocumentAsync(client.Operations, scope, jobId, token)); }
                    catch (ControlException error) when (error.Error.Code is "document_unavailable" or "revision_conflict") { }
                }
                await Task.Delay(TimeSpan.FromMilliseconds(300), token);
            }
        }
        catch (OperationCanceledException) when (run.Preparation.IsCancellationRequested)
        {
            run.Error = new RecognitionActionCanceledException("Audio capture was cancelled.");
        }
        catch (Exception error) { run.Error = error; }
        finally
        {
            if (!preserveSnapshot && prepared is { IsSnapshot: true })
                DeleteUnsubmittedControlAudio(prepared.Path);
            run.Job.TrySetResult(null);
            lock (controlRecognitionGate)
            {
                run.Finished.TrySetResult();
                run.Preparation.Dispose();
                run.Observation.Dispose();
            }
            Raise(nameof(RecognitionStatus));
        }
    }

    async Task PublishControlRecognitionAsync(ControlRecognitionRun run, ControlRecognitionDocument changes)
    {
        if (changes.Cues.Count == 0) return;
        ControlRecognitionSlot[] slots;
        lock (controlRecognitionGate)
        {
            if (run.Latest?.Revision == changes.Revision) return;
            run.Latest = changes;
            slots = controlRecognition.OfType<ControlRecognitionSlot>().Where(slot => ReferenceEquals(slot.Run, run)).ToArray();
        }
        foreach (ControlRecognitionSlot slot in slots)
        {
            try { await ProjectControlRecognitionAsync(slot, changes, run.Observation.Token); }
            catch (OperationCanceledException)
            {
                lock (controlRecognitionGate)
                    if (ReferenceEquals(controlRecognition[slot.Input.Slot], slot)) slot.Observation.Cancel();
            }
        }
    }

    Task ProjectControlRecognitionAsync(ControlRecognitionSlot slot, ControlRecognitionDocument changes, CancellationToken token)
        => MutateWithTranslationAsync(cancellation =>
        {
            ControlRecognitionInput input = slot.Input;
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation);
            if (!player.TryMutateMedia(input.Media.Generation, input.Media.Item, () => OwnsControlRecognitionSlot(slot), () =>
            {
                lock (controlRecognitionGate)
                    if (!ReferenceEquals(controlRecognition[input.Slot], slot) || slot.Run.Latest?.Revision != changes.Revision
                        || slot.ProjectedRevision == changes.Revision) return;
                ApplyControlRecognition(slot, changes);
            }, linked.Token))
                throw new RecognitionActionCanceledException("The media, audio or subtitle slot changed before recognition could be applied.");
        });

    void ApplyControlRecognition(ControlRecognitionSlot slot, ControlRecognitionDocument changes)
    {
        int slotIndex = slot.Input.Slot;
        if (slot.ProjectedSource is { } projected && changes.Cues.Count >= slot.ProjectedCues.Count
            && slot.ProjectedCues.Select((cue, index) => SameControlCue(cue, changes.Cues[index])).All(same => same))
        {
            projected.Manager.AddRange(changes.Cues.Skip(slot.ProjectedCues.Count).Select(cue => cue.Clone()));
            projected.Manager.LanguageSource = changes.Language;
            player.SubtitlesManager.SetCurrentTime(TimeSpan.FromTicks(player.CurTime));
        }
        else
            player.Subtitles[slotIndex].LoadControlRecognitionResult(changes.Cues.Select(cue => cue.Clone()).ToArray(), changes.Language);

        slot.ProjectedSource = CaptureSubtitleCueSource(slotIndex);
        slot.ProjectedCues = player.SubtitlesManager[slotIndex].SnapshotSubs();
        slot.ProjectedRevision = changes.Revision;
    }

    static bool SameControlCue(SubtitleData first, SubtitleData second)
        => first.StartTime == second.StartTime && first.EndTime == second.EndTime && first.Text == second.Text;

    static async Task<ControlJob> WaitForControlJobAsync(ControlClient client, ControlScope scope, Guid jobId,
        CancellationToken token, Action<ControlJob>? publish = null)
    {
        while (true)
        {
            ControlJob job = await client.GetAsync(scope, jobId, token)
                ?? throw new InvalidOperationException("The host could not find the accepted job.");
            publish?.Invoke(job);
            if (job.Status == ControlJobStatus.Completed) return job;
            ThrowIfControlJobStopped(job);
            await Task.Delay(TimeSpan.FromMilliseconds(300), token);
        }
    }

    static void ThrowIfControlJobStopped(ControlJob job)
    {
        if (job.Status == ControlJobStatus.Cancelled)
            throw new RecognitionActionCanceledException("The host job was cancelled.");
        if (job.Status is ControlJobStatus.Failed or ControlJobStatus.Interrupted)
            throw new InvalidOperationException(job.Error?.Message ?? $"The host job ended with status {job.Status}.");
    }

    static async Task<(List<SubtitleData> Cues, Language? Language)> ReadRecognizedCuesAsync(ControlClient client,
        ControlScope scope, Guid jobId, CancellationToken token)
    {
        ControlRecognitionDocument document = await ReadControlDocumentAsync(client, scope, jobId, token);
        return (document.Cues.ToList(), document.Language);
    }

    static async Task<ControlRecognitionDocument> ReadControlDocumentAsync(IPlayerControlService client,
        ControlScope scope, Guid jobId, CancellationToken token)
    {
        List<SubtitleData> cues = [];
        string? revision = null;
        Language? language = null;
        int offset = 0;
        while (true)
        {
            ControlResult page = await client.InvokeAsync(new(Guid.NewGuid(), "subtitles.document.read", scope, null,
                ControlProtocol.Encode(new ControlDocumentRead(jobId, revision, offset))), token);
            ThrowControlError(page);
            ControlDocumentPage document = page.Value?.Deserialize<ControlDocumentPage>(ControlProtocol.Json)
                ?? throw new InvalidOperationException("The host did not return the recognized subtitles.");
            revision ??= document.Revision;
            if (language == null && document.SourceLanguage is { } sourceLanguage) language = Language.Get(sourceLanguage);
            foreach (ControlCue cue in document.Cues)
            {
                cues.Add(new SubtitleData
                {
                    StartTime = cue.Start,
                    EndTime = cue.End,
                    Text = cue.Text ?? "",
                    TranslatedText = cue.TranslatedText,
                    SpeakerId = cue.SpeakerId,
                    AssignedVoiceId = cue.AssignedVoiceId
                });
            }
            if (!document.NextOffset.HasValue) break;
            if (document.NextOffset.Value <= offset)
                throw new InvalidOperationException("The host did not advance the subtitle document cursor.");
            offset = document.NextOffset.Value;
        }
        return new(revision!, cues, language);
    }

    Task DetachSharedControlRecognitionAsync(ControlRecognitionSlot slot)
        => MutateWithTranslationAsync(cancellation =>
        {
            if (slot.ProjectedSource is not { } source) return;
            player.TryMutateMedia(source.Media.Generation, source.Media.Item,
                () => IsCurrentSubtitleCueSource(source) && source.Manager.WithCueLock(cues => cues.SequenceEqual(slot.ProjectedCues)),
                () => player.Commands.SubtitlesOffAction(source.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                cancellation);
        });

    async Task CancelControlRecognitionSlotAsync(ControlRecognitionSlot slot, AppPaths paths)
    {
        await CancelControlRecognitionAsync(slot.Run.Job.Task, paths);
        await DetachSharedControlRecognitionAsync(slot);
    }

    async Task CancelControlRecognitionAsync(Task<Guid?> acceptedJob, AppPaths paths)
    {
        Guid? jobId = await acceptedJob;
        if (!jobId.HasValue) return;
        using ControlRecognitionConnection client = await ConnectControlRecognitionAsync(paths);
        ThrowControlError(await client.Jobs.CancelAsync(new(jobId.Value, new(ControlProtocol.DefaultProfileId))));
    }

    async Task ToggleControlRecognitionPauseAsync(IReadOnlyList<ControlRecognitionRun> runs, AppPaths paths)
    {
        using ControlRecognitionConnection client = await ConnectControlRecognitionAsync(paths);
        bool pause;
        lock (controlRecognitionGate) pause = runs.Any(run => !run.IsPaused);
        foreach (ControlRecognitionRun run in runs)
        {
            Guid? jobId = await run.Job.Task;
            if (!jobId.HasValue) continue;
            ThrowControlError(await client.Operations.InvokeAsync(new(Guid.NewGuid(), pause ? "recognition.pause" : "recognition.resume",
                new(ControlProtocol.DefaultProfileId), null, ControlProtocol.Encode(new { JobId = jobId.Value }))));
            lock (controlRecognitionGate) run.IsPaused = pause;
        }
        Raise(nameof(RecognitionStatus));
    }

    static void ThrowControlError(ControlResult result)
    {
        if (result.Error != null) throw new ControlException(result.Error);
    }
}
