using System.ComponentModel;
using System.Text.Json;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Diarization;
using LLPlayer.ControlApi;
using LLPlayer.Core.Services.Diarization;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController : IDiarizationController
{
    sealed record DiarizationSelection(SubtitleCueSource Source, AudioStream Audio, Demuxer Demuxer,
        string AudioUrl, ExternalStream? ExternalAudio, DiarizationAudioSource Input, long AudioDelay,
        long SubtitleDelay, DiarizationCueSnapshot[] Cues);

    sealed record DiarizationCueSnapshot(SubtitleData Cue, TimeSpan Start, TimeSpan End, bool IsText);

    sealed record DiarizationOperation(DiarizationSelection Selection)
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<int> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Guid?> RemoteJob { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AppPaths? ControlPaths { get; init; }
    }

    readonly DiarizationWorkflow diarization;
    readonly GpuWorkCoordinator diarizationCoordinator;
    readonly Func<(IDiarizationEngine Engine, bool UseGpu)> configureDiarization;
    readonly Lock diarizationGate = new();
    readonly DiarizationSelection?[] speakerOwners = new DiarizationSelection?[2];
    DiarizationOperation? activeDiarization;
    bool diarizationShuttingDown;
    int pendingDiarizationValidation;

    void OnDiarizationAudioConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Config.AudioConfig.Delay)) QueueDiarizationValidation();
    }

    void OnDiarizationCoordinatorChanged() => Raise(nameof(CanCancelDiarization));

    public bool IsDiarizing
    {
        get { lock (diarizationGate) return activeDiarization != null; }
    }

    public bool CanCancelDiarization => IsDiarizing || diarization.HasPendingDrain;

    public Task<int> AnalyzeSpeakersAsync(int slot)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        lock (diarizationGate) CheckDiarizationAdmission();
        DiarizationSelection selection = CaptureDiarizationSelection(slot);
        AppPaths? control = controlRecognitionPaths;
        (IDiarizationEngine? engine, bool useGpu) = control == null ? configureDiarization() : (null, false);
        DiarizationOperation operation;
        lock (diarizationGate)
        {
            CheckDiarizationAdmission();
            operation = new(selection) { ControlPaths = control };
            activeDiarization = operation;
        }
        NotifyDiarizationStatus();
        _ = Task.Run(() => RunDiarizationAsync(operation, engine, useGpu));
        return operation.Completion.Task;
    }

    void CheckDiarizationAdmission()
    {
        ObjectDisposedException.ThrowIf(disposed || diarizationShuttingDown, this);
        if (activeDiarization != null || diarization.HasPendingDrain)
            throw new InvalidOperationException("Finish the current speaker analysis or retry stopping it first.");
    }

    DiarizationSelection CaptureDiarizationSelection(int slot)
    {
        SubtitleCueSource? source = CaptureSubtitleCueSource(slot);
        if (source == null || source.Manager.IsLoading || (source.IsASR && player.IsASRRunning))
            throw new InvalidOperationException("Load text subtitles or finish speech recognition in the selected subtitle track first.");

        DiarizationSelection? selection = null;
        player.TryMutateMedia(source.Media.Generation, source.Media.Item, () => IsCurrentSubtitleCueSource(source), () =>
        {
            AudioStream? audio = player.decoder.AudioStream;
            if (!player.Audio.IsOpened || audio is not { StreamIndex: >= 0 } || audio.Demuxer.IsLive
                || source.Media.Item.IOStream != null || audio.ExternalStream?.IOStream != null
                || source.Media.Item.ExternalVideoStream?.IOStream != null
                || !LocalMediaNavigation.TryGetLocalPath(source.Media.Item.Url, out string mediaPath) || !File.Exists(mediaPath)
                || !LocalMediaNavigation.TryGetLocalPath(audio.Demuxer.Url, out string audioPath) || !File.Exists(audioPath))
                throw new InvalidOperationException("Speaker analysis needs an opened local media file and a local selected audio track.");

            selection = source.Manager.WithCueLock(cues => new DiarizationSelection(source, audio, audio.Demuxer,
                audio.Demuxer.Url, audio.ExternalStream, new(audioPath, audio.StreamIndex), player.Config.Audio.Delay,
                player.Config.Subtitles[slot].Delay, cues.Select(cue =>
                    new DiarizationCueSnapshot(cue, cue.StartTime, cue.EndTime, IsDiarizationText(cue))).ToArray()));
        });
        if (selection == null) throw StaleDiarization();
        if (!selection.Cues.Any(cue => cue.IsText))
            throw new InvalidOperationException("The selected subtitle track does not contain timed text cues.");
        return selection;
    }

    static bool IsDiarizationText(SubtitleData cue)
        => !string.IsNullOrWhiteSpace(cue.Text) && !cue.IsBitmap && cue.EndTime > cue.StartTime;

    bool OwnsDiarizationSelection(DiarizationSelection selection)
    {
        AudioStream? audio = player.decoder.AudioStream;
        SubtitleCueSource source = selection.Source;
        return IsCurrentSubtitleCueSource(source) && !source.Manager.IsLoading && (!source.IsASR || !player.IsASRRunning)
            && player.Audio.IsOpened && ReferenceEquals(selection.Audio, audio)
            && ReferenceEquals(audio!.Demuxer, selection.Demuxer) && audio.Demuxer.Url == selection.AudioUrl
            && ReferenceEquals(audio.ExternalStream, selection.ExternalAudio)
            && audio.StreamIndex == selection.Input.StreamIndex
            && player.Config.Audio.Delay == selection.AudioDelay && player.Config.Subtitles[source.Slot].Delay == selection.SubtitleDelay;
    }

    static bool SameDiarizationCues(DiarizationSelection selection, IReadOnlyList<SubtitleData> cues)
    {
        if (cues.Count != selection.Cues.Length) return false;
        for (int i = 0; i < cues.Count; i++)
        {
            DiarizationCueSnapshot snapshot = selection.Cues[i];
            SubtitleData cue = cues[i];
            if (!ReferenceEquals(cue, snapshot.Cue) || cue.StartTime != snapshot.Start || cue.EndTime != snapshot.End
                || IsDiarizationText(cue) != snapshot.IsText)
                return false;
        }
        return true;
    }

    async Task RunDiarizationAsync(DiarizationOperation operation, IDiarizationEngine? engine, bool useGpu)
    {
        CancellationToken token = operation.Cancellation.Token;
        DiarizationSelection selection = operation.Selection;
        int count = 0;
        Exception? error = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!IsCurrentDiarizationSelection(selection)) throw StaleDiarization();
            // The WAV clock excludes playback delays. Compare the heard audio to the displayed cue.
            TimeSpan shift = TimeSpan.FromTicks(checked(selection.SubtitleDelay - selection.AudioDelay));
            DiarizationCue[] timings = selection.Cues.Where(cue => cue.IsText)
                .Select(cue => new DiarizationCue(cue.Start + shift, cue.End + shift)).ToArray();
            IReadOnlyList<string?> labels = operation.ControlPaths is { } paths
                ? await RunControlDiarizationAsync(operation, paths, timings, token).ConfigureAwait(false)
                : await diarization.RunAsync(engine!, selection.Input, timings, useGpu, token).ConfigureAwait(false);
            count = await ApplyDiarizationAsync(selection, labels, token).ConfigureAwait(false);
        }
        catch (Exception exception) { error = exception; }
        finally
        {
            operation.RemoteJob.TrySetResult(null);
            lock (diarizationGate) activeDiarization = null;
            operation.Cancellation.Dispose();
            if (error is OperationCanceledException) operation.Completion.TrySetCanceled(token);
            else if (error != null) operation.Completion.TrySetException(error);
            else operation.Completion.TrySetResult(count);
            NotifyDiarizationStatus();
        }
    }

    static async Task<IReadOnlyList<string?>> RunControlDiarizationAsync(DiarizationOperation operation, AppPaths paths,
        IReadOnlyList<DiarizationCue> timings, CancellationToken token)
    {
        using ControlClient client = await ControlClient.ConnectOrStartAsync(paths.ConfigDir, paths.StateDir, token).ConfigureAwait(false);
        ControlScope scope = new(ControlProtocol.DefaultProfileId);
        var cues = timings.Select(cue => new { CueId = Guid.NewGuid(), StartTicks = cue.Start.Ticks, EndTicks = cue.End.Ticks }).ToArray();
        var request = new
        {
            MediaPath = operation.Selection.Input.Path,
            operation.Selection.Input.StreamIndex,
            Cues = cues
        };
        ControlResult accepted = await client.InvokeAsync(new(Guid.NewGuid(), "diarization.start", scope, null,
            ControlProtocol.Encode(request)), token).ConfigureAwait(false);
        ThrowControlError(accepted);
        Guid jobId = accepted.JobId ?? throw new InvalidOperationException("The host did not return a speaker analysis job.");
        operation.RemoteJob.TrySetResult(jobId);
        ControlJob job = await WaitForControlJobAsync(client, scope, jobId, token).ConfigureAwait(false);
        SpeakerResult result = job.Value?.Deserialize<SpeakerResult>(ControlProtocol.Json)
            ?? throw new InvalidOperationException("The host returned no speaker analysis result.");
        var speakers = result.Speakers.ToDictionary(speaker => speaker.CueId, speaker => speaker.SpeakerId);
        if (speakers.Count != cues.Length || cues.Any(cue => !speakers.ContainsKey(cue.CueId)))
            throw new InvalidOperationException("The speaker analysis result does not match the requested cues.");
        return cues.Select(cue => speakers[cue.CueId]).ToArray();
    }

    sealed record SpeakerResult(IReadOnlyList<SpeakerLabel> Speakers);
    sealed record SpeakerLabel(Guid CueId, string? SpeakerId);

    Task<int> ApplyDiarizationAsync(DiarizationSelection selection, IReadOnlyList<string?> labels, CancellationToken token)
    {
        TaskCompletionSource<int> applied = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                SubtitleCueSource source = selection.Source;
                int count = 0;
                bool matched = false;
                player.TryMutateMedia(source.Media.Generation, source.Media.Item, () => OwnsDiarizationSelection(selection), () =>
                {
                    matched = source.Manager.WithCueLock(cues =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (!SameDiarizationCues(selection, cues) || !OwnsDiarizationSelection(selection)) return false;
                        int label = 0;
                        for (int i = 0; i < cues.Count; i++)
                        {
                            string? speaker = selection.Cues[i].IsText ? labels[label++] : null;
                            cues[i].SpeakerId = speaker;
                            if (speaker != null) count++;
                        }
                        speakerOwners[source.Slot] = count > 0 ? selection : null;
                        return true;
                    });
                }, token);
                if (!matched) throw StaleDiarization();
                QueueCueNotification(source.Slot);
                applied.TrySetResult(count);
            }
            catch (Exception exception) { applied.TrySetException(exception); }
        });
        return applied.Task;
    }

    static OperationCanceledException StaleDiarization()
        => new("The audio track, subtitle track or timing changed during speaker analysis.");

    void QueueDiarizationValidation()
    {
        if (Interlocked.Exchange(ref pendingDiarizationValidation, 1) != 0) return;
        // Source listeners may be running under engine locks. Never enter the player gate from a listener.
        if (ui == null) _ = Task.Run(ValidateDiarizationOwners);
        else ui.Post(ValidateDiarizationOwners);
    }

    void ValidateDiarizationOwners()
    {
        Interlocked.Exchange(ref pendingDiarizationValidation, 0);
        if (disposed) return;
        DiarizationOperation? operation;
        lock (diarizationGate) operation = activeDiarization;
        if (operation != null && !IsCurrentDiarizationSelection(operation.Selection))
            CancelDiarization(operation);

        // A preserving video switch can retain cue objects even though the audio/time binding changed.
        // Clear only labels this controller wrote, on cue objects that still belong to the current player.
        for (int slot = 0; slot < speakerOwners.Length; slot++)
        {
            DiarizationSelection? previous = speakerOwners[slot];
            if (previous == null || IsCurrentDiarizationSelection(previous)) continue;
            PlaybackChoiceMedia? media = CapturePlaybackChoiceMedia();
            if (media == null) continue;
            if (!player.TryMutateMedia(media.Generation, media.Item, () => IsCurrentPlaybackMedia(media), () =>
                previous.Source.Manager.WithCueLock(cues =>
                {
                    if (!ReferenceEquals(speakerOwners[slot], previous) || IsCurrentDiarizationSelection(previous)) return false;
                    var owned = previous.Cues.Select(cue => cue.Cue).ToHashSet();
                    foreach (SubtitleData cue in cues)
                        if (owned.Contains(cue)) cue.SpeakerId = null;
                    speakerOwners[slot] = null;
                    return true;
                }))) continue;
            QueueCueNotification(slot);
        }
    }

    bool IsCurrentDiarizationSelection(DiarizationSelection selection)
        => OwnsDiarizationSelection(selection) && selection.Source.Manager.WithCueLock(cues => SameDiarizationCues(selection, cues));

    public async Task CancelDiarizationAsync()
    {
        DiarizationOperation? operation;
        lock (diarizationGate) operation = activeDiarization;
        if (operation?.ControlPaths is not { } paths)
        {
            await DrainDiarizationAsync().ConfigureAwait(false);
            return;
        }
        if (await operation.RemoteJob.Task.ConfigureAwait(false) is not { } jobId) return;
        using ControlClient client = await ControlClient.ConnectOrStartAsync(paths.ConfigDir, paths.StateDir).ConfigureAwait(false);
        ThrowControlError(await client.CancelAsync(new(jobId, new(ControlProtocol.DefaultProfileId))).ConfigureAwait(false));
    }

    void CancelActiveDiarization()
    {
        DiarizationOperation? operation;
        lock (diarizationGate) operation = activeDiarization;
        CancelDiarization(operation);
    }

    internal void BeginDiarizationShutdown()
    {
        DiarizationOperation? operation;
        lock (diarizationGate)
        {
            diarizationShuttingDown = true;
            operation = activeDiarization;
        }
        CancelDiarization(operation);
    }

    internal void ResumeDiarizationAfterFailedShutdown()
    {
        lock (diarizationGate) diarizationShuttingDown = false;
        NotifyDiarizationStatus();
    }

    internal async Task DrainDiarizationAsync()
    {
        DiarizationOperation? operation;
        lock (diarizationGate) operation = activeDiarization;
        CancelDiarization(operation);
        if (operation != null)
        {
            // AnalyzeSpeakersAsync owns the error. Cleanup still has to retry a failed external stop.
            try { await operation.Completion.Task.ConfigureAwait(false); }
            catch (Exception) { }
        }
        try
        {
            if (diarization.HasPendingDrain && !await diarization.RetryDrainAsync().ConfigureAwait(false))
                throw new InvalidOperationException("Could not confirm that the speaker analysis process stopped. Retry Cancel speaker analysis.");
        }
        finally { NotifyDiarizationStatus(); }
    }

    static void CancelDiarization(DiarizationOperation? operation)
    {
        try { operation?.Cancellation.Cancel(); }
        catch (ObjectDisposedException) { /* The operation completed before cancellation reached it. */ }
    }

    void NotifyDiarizationStatus()
    {
        Raise(nameof(IsDiarizing));
        Raise(nameof(CanCancelDiarization));
    }

    static (IDiarizationEngine Engine, bool UseGpu) ReadDiarizationSetup()
    {
        const string pythonVariable = "LLPLAYER_DIARIZATION_PYTHON";
        const string modelVariable = "LLPLAYER_DIARIZATION_MODEL";
        const string deviceVariable = "LLPLAYER_DIARIZATION_DEVICE";
        string? python = Environment.GetEnvironmentVariable(pythonVariable);
        string? model = Environment.GetEnvironmentVariable(modelVariable);
        string device = Environment.GetEnvironmentVariable(deviceVariable)?.Trim().ToLowerInvariant() ?? "cpu";
        if (python == null || model == null || !Path.IsPathFullyQualified(python) || !Path.IsPathFullyQualified(model)
            || !File.Exists(python) || !Directory.Exists(model))
            throw new InvalidOperationException($"Set {pythonVariable} to your local Python executable and {modelVariable} to the local pyannote Community-1 model directory. Restart LLPlayer after changing the environment.");
        if (device is not ("cpu" or "cuda"))
            throw new InvalidOperationException($"Set {deviceVariable} to cpu or cuda. Restart LLPlayer after changing the environment.");
        return (new LocalDiarizationEngine(python, model, device), device == "cuda");
    }
}
