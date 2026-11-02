using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;
using FlyleafLib.MediaPlayer.Batch;
using LLPlayer.Core.Services;
using LLPlayer.Core.Services.Models;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    public LanguageResourceCoordinator LanguageResources { get; } = new();
    DubbingVoiceSaveQueue? voiceSaveQueue;
    readonly object voiceSaveQueueGate = new();
    bool voiceSavesStopping;
    readonly object insightCaptureGate = new();
    readonly object?[] insightSources = new object?[2];
    long insightRevision;

    readonly object languageConfigurationGate = new();
    public Config CaptureLanguageWorkflowConfig()
    {
        lock (languageConfigurationGate) return BatchSubtitleConfigSnapshot.Create(player.Config);
    }

    void PublishLanguageConfiguration(Action publish)
    {
        lock (languageConfigurationGate) publish();
    }

    public void ApplyDubbingSettings(DubbingSettings settings)
        => PublishLanguageConfiguration(() => settings.Apply(player.Config.Subtitles));

    public bool IsInsightsTranscriptCurrent(InsightsTranscriptSnapshot snapshot)
    {
        if (disposed || snapshot.Slot is < 0 or > 1) return false;
        lock (insightCaptureGate)
        {
            if (snapshot.MediaGeneration != insightRevision) return false;
            return insightSources[snapshot.Slot] is SubtitleCueSource source
                ? IsCurrentSubtitleCueSource(source) : CaptureSubtitleCueSource(snapshot.Slot) == null;
        }
    }

    public async Task MutateModelFilesAsync(ModelEntry entry, Func<Task> action, CancellationToken cancellationToken)
    {
        IDisposable? lease = null;
        await Recognition.MutateIfIdleAsync(token =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            lease = LanguageResources.AcquireMutation();
        });
        using (lease)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action().ConfigureAwait(false);
        }
    }

    public bool IsModelInUse(ModelEntry entry)
    {
        if (!entry.Present) return false; // A selected but missing model can be installed after admission.
        var settings = player.Config.Subtitles;
        if (entry.Kind == ModelKind.Whisper)
        {
            return settings.WhisperCppConfig.Model != null
                && PathsEqual(settings.WhisperCppConfig.GetModelFilePath(), entry.FullPath);
        }
        if (entry.Kind == ModelKind.FasterWhisperEngine)
            return settings.ASREngine == SubASREngineType.FasterWhisper;
        return Recognition.IsBusy;
    }

    static bool PathsEqual(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public InsightsTranscriptSnapshot CaptureInsightsTranscript(int slot, bool translated)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        var source = CaptureSubtitleCueSource(slot);
        long revision;
        lock (insightCaptureGate)
        {
            if (!Equals(insightSources[slot], source)) { insightSources[slot] = source; insightRevision++; }
            revision = insightRevision;
        }
        string code = GetSubtitleLanguage(slot) ?? "";
        string target = GetSubtitleTargetLanguage() ?? "";
        var cues = bridge.Snapshot(player.SubtitlesManager[slot].Subs)
            .Select(cue => translated ? cue.TranslatedText : cue.Text).ToArray();
        if (source != null && !IsCurrentSubtitleCueSource(source))
            throw new InvalidOperationException("The subtitle source changed. Refresh the transcript and try again.");
        return new InsightsTranscriptSnapshot(revision, Title ?? Url ?? "No media", DateTimeOffset.UtcNow,
            slot, translated, code, code, target, target, cues,
            player.IsASRRunning || player.SubtitlesManager[slot].IsLoading);
    }

    public IReadOnlyList<SubtitleData> CaptureDubbingVoiceAssignments(int slot = 0)
    {
        if (slot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(slot));
        SubtitleCueSource? before = CaptureSubtitleCueSource(slot);
        if (before == null) return [];
        return CaptureDubbingVoiceAssignments(before);
    }

    IReadOnlyList<SubtitleData> CaptureDubbingVoiceAssignments(SubtitleCueSource before)
    {
        if (!IsCurrentSubtitleCueSource(before))
            throw new InvalidOperationException("The subtitle source changed. Try the voice operation again.");
        var cues = bridge.Snapshot(before.Manager.Subs).Select(cue => new SubtitleData
        { StartTime = cue.StartTime, EndTime = cue.EndTime, Text = cue.Text, AssignedVoiceId = cue.AssignedVoiceId }).ToArray();
        if (!Equals(before, CaptureSubtitleCueSource(before.Slot)) || !IsCurrentSubtitleCueSource(before))
            throw new InvalidOperationException("The subtitle source changed. Try the voice operation again.");
        return cues;
    }

    public IDubbingVoiceAssignmentProvider? CaptureBatchVoiceAssignments()
    {
        long generation = player.MediaGeneration;
        var item = player.Playlist?.Selected;
        string? mediaPath = CurrentMediaPath;
        var sources = Enumerable.Range(0, 2).Select(CaptureSubtitleCueSource).ToArray();
        SubtitleData[] rawCues = sources.Where(source => source != null)
            .SelectMany(source => CaptureDubbingVoiceAssignments(source!)).ToArray();
        IReadOnlyList<DubbingVoiceClaim> pendingClaims = [];
        if (LocalDubbingMediaPath(mediaPath) is { } localMedia)
            lock (voiceSaveQueueGate) pendingClaims = voiceSaveQueue?.SnapshotClaims(localMedia) ?? [];
        var claims = DubbingVoicePersistence.MergeEditClaims(CaptureDubbingVoiceEdits(sources), pendingClaims);
        var edits = claims.Select(claim => claim.ToSubtitle()).ToArray();
        var cues = DubbingVoicePersistence.MergeClaims(edits, rawCues);
        var cleared = edits.Where(cue => string.IsNullOrWhiteSpace(cue.AssignedVoiceId)).ToArray();
        var after = Enumerable.Range(0, 2).Select(CaptureSubtitleCueSource).ToArray();
        if (generation != player.MediaGeneration || !ReferenceEquals(item, player.Playlist?.Selected)
            || mediaPath != CurrentMediaPath || sources.Where((source, slot) => !Equals(source, after[slot])
                || source != null && !IsCurrentSubtitleCueSource(source)).Any())
            throw new InvalidOperationException("The media or subtitle source changed. Start the batch again.");
        return cleared.Length == 0
            ? DubbingWorkflow.CreateVoiceAssignments(player.Config.Subtitles.PersistPerLineVoices, mediaPath, cues)
            : new DubbingBatchVoiceAssignmentProvider(mediaPath, cues, cleared, player.Config.Subtitles.PersistPerLineVoices);
    }

    public string? GetCueVoiceId(CueInfo cue, int slot) => IsCueCurrent(cue, slot)
        && cue.Identity?.Source is SubtitleCueToken token ? token.Cue.AssignedVoiceId : null;

    public async Task SetCueVoiceAsync(CueInfo cue, int slot, string? voiceId)
    {
        // Resolve both ready sources before applying an edit: clearing A must not suppress an in-flight read
        // that carries B's saved voice. No disk work occurs when opt-in persistence is disabled.
        await Task.WhenAll(RestoreDubbingVoicesAsync(0), RestoreDubbingVoicesAsync(1)).ConfigureAwait(false);
        await Recognition.MutateIfIdleAsync(cancellation =>
        {
            if (!IsCueCurrent(cue, slot) || cue.Identity?.Source is not SubtitleCueToken token)
                throw new InvalidOperationException("The subtitle changed. Select the current row again.");
            string? saveMedia = null;
            IReadOnlyList<SubtitleData>? saveSnapshot = null;
            long saveRevision = 0;
            if (!player.TryMutateMedia(token.Source.Media.Generation, token.Source.Media.Item,
                    () => IsCueCurrent(cue, slot), () =>
                    {
                        NotifyDubbingVoiceEdited(slot);
                        token.Cue.AssignedVoiceId = voiceId;
                        saveRevision = RecordDubbingVoiceEdit(token);
                        if (!player.Config.Subtitles.PersistPerLineVoices) return;
                        saveMedia = LocalDubbingMediaPath(CurrentMediaPath);
                        if (saveMedia == null) return;
                        // This accepted edit is the only new persistence claim. The pending owner retains
                        // earlier edits from either slot/replaced sources, and the disk merge retains all
                        // untouched entries. A new track's unedited null is never inferred to be a clear.
                        saveSnapshot = [new SubtitleData { StartTime = token.Cue.StartTime,
                            EndTime = token.Cue.EndTime, AssignedVoiceId = token.Cue.AssignedVoiceId }];
                    }, cancellation))
                throw new InvalidOperationException("The subtitle changed. Select the current row again.");
            QueueCueNotification(slot);
            if (saveMedia != null && saveSnapshot != null)
                lock (voiceSaveQueueGate)
                {
                    if (voiceSavesStopping) return;
                    voiceSaveQueue ??= new DubbingVoiceSaveQueue(() => player.Config.Subtitles.PersistPerLineVoices,
                        TimeSpan.FromMilliseconds(400));
                    voiceSaveQueue.Enqueue(saveMedia, saveSnapshot, saveRevision);
                }
        }).ConfigureAwait(false);
    }

    public Task DrainVoiceAssignmentsAsync()
    {
        DubbingVoiceSaveQueue? queue;
        lock (voiceSaveQueueGate)
        {
            voiceSavesStopping = true;
            queue = voiceSaveQueue;
            voiceSaveQueue = null;
        }
        return Task.Run(() => queue?.Dispose());
    }
}
