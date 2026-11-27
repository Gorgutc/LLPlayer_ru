using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;

namespace LLPlayer.Core.Services;

public interface ISrtAlignmentSession
{
    string SourcePath { get; }
    string AudioLabel { get; }
    string DefaultLanguage { get; }
    bool CanUseCuda { get; }
    SrtAlignmentDevice DefaultDevice { get; }
    bool IsCurrent { get; }

    Task<SrtAlignmentDraft> PrepareAsync(string language, SrtAlignmentDevice device, CancellationToken token);
    Task SaveAsync(SrtAlignmentDraft draft, string path, bool translated, bool acceptUnmatched, CancellationToken token);
    Task CancelAndDrainAsync();
}

public sealed record SrtAlignmentRow(int Id, string Text, TimeSpan OriginalStart, TimeSpan OriginalEnd,
    TimeSpan? ProposedStart, TimeSpan? ProposedEnd, double Coverage, string? Reason);

public sealed class SrtAlignmentDraft(IReadOnlyList<SrtAlignmentRow> rows, bool canSaveOriginal, bool canSaveTranslated)
{
    public IReadOnlyList<SrtAlignmentRow> Rows { get; } = Array.AsReadOnly(rows.ToArray());
    public bool CanSaveOriginal { get; } = canSaveOriginal;
    public bool CanSaveTranslated { get; } = canSaveTranslated;
    public bool HasTimingConflicts { get; init; }
    public SrtClockCorrection? ClockCorrection { get; init; }
    public int MatchedCount => ClockCorrection?.AnchorCount ?? matchedCount;
    private readonly int matchedCount = rows.Count(row => row.ProposedStart.HasValue);
    public int TotalCount => Rows.Count;
    public bool NeedsTimingAcknowledgment =>
        (CanSaveOriginal || CanSaveTranslated) && (ClockCorrection != null || MatchedCount < TotalCount);
}

internal sealed record SrtAlignmentInput(string SourcePath, SrtTimingDocument Document, string MediaPath,
    int StreamIndex, TimeSpan Duration, string AudioLabel, string Language, Func<Config> CaptureConfig,
    Func<bool> IsCurrent, Func<ImmutableArray<SrtTimingCueMetadata>> CaptureMetadata,
    Func<Func<bool>, Action, bool> TryPublish);

public sealed class SrtAlignmentWorkflow
{
    const string GpuOperation = "SRT alignment";

    readonly GpuWorkCoordinator? coordinator;
    readonly LanguageResourceCoordinator resources;
    readonly Func<Config, SrtAlignmentAudioRequest, CancellationToken, Task<IReadOnlyList<WordTiming>>> readWords;
    readonly object gate = new();
    readonly SemaphoreSlim drainGate = new(1, 1);
    Operation? active;
    Func<ValueTask>? pendingCpuDrain;
    readonly HashSet<Operation> activeSaves = [];
    bool shuttingDown;

    public SrtAlignmentWorkflow(GpuWorkCoordinator? coordinator, LanguageResourceCoordinator resources)
        : this(coordinator, resources, SrtAlignmentAudioSource.ReadWordsAsync) { }

    internal SrtAlignmentWorkflow(GpuWorkCoordinator? coordinator, LanguageResourceCoordinator resources,
        Func<Config, SrtAlignmentAudioRequest, CancellationToken, Task<IReadOnlyList<WordTiming>>> readWords)
    {
        this.coordinator = coordinator;
        this.resources = resources;
        this.readWords = readWords;
    }

    public bool CanStart(Player player, int slot) => player.CaptureSrtAlignmentTarget(slot) != null;

    public ISrtAlignmentSession Capture(Player player, int slot, Func<Config>? captureConfig = null)
    {
        Player.SrtAlignmentTarget target = player.CaptureSrtAlignmentTarget(slot)
            ?? throw new InvalidOperationException("Select a loaded local UTF-8 SRT and an embedded audio track first.");

        return CreateSession(new(target.Source.Path, target.Source.Document, target.MediaPath, target.StreamIndex,
            target.Duration, target.AudioLabel, target.Language,
            captureConfig ?? (() => BatchSubtitleConfigSnapshot.CreateForRecognition(player.Config)),
            () => player.TryUseSrtAlignmentTarget(target, () => true, () => { }),
            () => target.Manager.TryCaptureSrtTimingMetadata(target.Source, out var metadata) ? metadata : [],
            (owns, publish) => player.TryUseSrtAlignmentTarget(target, owns, publish)));
    }

    internal ISrtAlignmentSession CreateSession(SrtAlignmentInput input) => new Session(this, input);

    public void BeginShutdown()
    {
        lock (gate)
        {
            shuttingDown = true;
            active?.Cancellation.CancelAsync();
            foreach (Operation save in activeSaves)
                _ = save.Cancellation.CancelAsync();
        }
    }

    public void ResumeAfterFailedShutdown()
    {
        lock (gate) shuttingDown = false;
    }

    public void CancelStaleSources()
    {
        Operation? operation;
        lock (gate) operation = active;
        if (operation == null || operation.Session.IsCurrent) return;
        lock (gate)
            if (ReferenceEquals(active, operation)) operation.Cancellation.CancelAsync();
    }

    public Task CancelAndDrainAsync() => CancelAndDrainAsync(null);

    public Task DrainAsync() => CancelAndDrainAsync(null);

    async Task CancelAndDrainAsync(Session? owner)
    {
        await drainGate.WaitAsync().ConfigureAwait(false);
        try
        {
            List<Task> pending = [];
            bool unrelatedRecognition;
            lock (gate)
            {
                unrelatedRecognition = active != null && owner != null && !ReferenceEquals(owner, active.Session);
                if (active != null && !unrelatedRecognition)
                    Cancel(active);
                foreach (Operation save in activeSaves)
                    if (owner == null || ReferenceEquals(owner, save.Session))
                        Cancel(save);
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
            if (unrelatedRecognition) return;

            Func<ValueTask>? stop;
            lock (gate) stop = pendingCpuDrain;
            if (stop != null)
            {
                await stop().ConfigureAwait(false);
                lock (gate) pendingCpuDrain = null;
            }
            if (coordinator != null) await coordinator.RetryDrainAsync(GpuOperation).ConfigureAwait(false);

            void Cancel(Operation operation)
            {
                pending.Add(operation.Cancellation.CancelAsync());
                pending.Add(operation.Completion.Task);
            }
        }
        finally { drainGate.Release(); }
    }

    Operation BeginSave(Session session, CancellationToken token)
    {
        lock (gate)
        {
            if (shuttingDown)
                throw new InvalidOperationException("SRT alignment is shutting down.");

            var save = new Operation(session, CancellationTokenSource.CreateLinkedTokenSource(token));
            activeSaves.Add(save);
            return save;
        }
    }

    void CompleteSave(Operation save)
    {
        lock (gate) activeSaves.Remove(save);
        save.Cancellation.Dispose();
        save.Completion.SetResult();
    }

    async Task<SrtAlignmentDraft> RunAsync(Session owner, SrtAlignmentInput input, Config config,
        string language, SrtAlignmentDevice device, CancellationToken token)
    {
        Operation operation;
        lock (gate)
        {
            if (shuttingDown || active != null || pendingCpuDrain != null || coordinator?.PendingDrain == GpuOperation)
                throw new InvalidOperationException("Finish the running alignment or retry stopping its recognizer first.");
            operation = new(owner, CancellationTokenSource.CreateLinkedTokenSource(token));
            active = operation;
        }

        GpuWorkLease? gpu = null;
        IDisposable? model = null;
        try
        {
            CancellationToken runToken = operation.Cancellation.Token;
            if (device == SrtAlignmentDevice.Cuda)
            {
                if (coordinator == null) throw new InvalidOperationException("CUDA alignment is unavailable in this player.");
                gpu = await coordinator.AcquireAsync(GpuOperation, runToken).ConfigureAwait(false);
            }
            runToken.ThrowIfCancellationRequested();
            owner.EnsureCurrent();
            model = resources.AcquireUse();
            var request = new SrtAlignmentAudioRequest(input.MediaPath, input.StreamIndex, language, device);
            IReadOnlyList<WordTiming> words = await readWords(config, request, runToken).ConfigureAwait(false);
            var firstPass = SrtAudioAlignment.Align(input.Document.Cues, words, input.Duration, runToken);
            if (firstPass.Any(proposal => !proposal.Start.HasValue) && !HasValidOrder(BuildRows(firstPass)))
            {
                var evidence = firstPass;
                var fit = SrtSubtitleClock.TryCreate(evidence, input.Duration, runToken);
                if (fit == null)
                {
                    evidence = SrtAudioAlignment.Align(input.Document.Cues, words, input.Duration, runToken, allowElidedRepeat: true);
                    if (evidence.Any(proposal => proposal.OneRepeatedWordElided))
                        fit = SrtSubtitleClock.TryCreate(evidence, input.Duration, runToken);
                }

                if (fit is { } clock)
                {
                    var projected = evidence.Select((proposal, index) => proposal with
                    {
                        Start = clock.Intervals[index].Start,
                        End = clock.Intervals[index].End,
                        IsExactAnchor = SrtSubtitleClock.IsSupported(proposal, clock.Intervals[index])
                    }).ToArray();
                    return await owner.PrepareResultAsync(firstPass, projected, runToken, clock.Summary).ConfigureAwait(false);
                }

                var speechEnds = SrtAudioAlignment.ReadSpeechEnds(firstPass, words, input.Duration, runToken);
                if (speechEnds != null && SrtSubtitleEndClock.TryCreate(firstPass, speechEnds.Anchors, (first, last, intervals) =>
                    speechEnds.CorroboratesEdge(first, last, intervals, runToken), input.Duration, runToken) is { } endClock)
                {
                    var projected = firstPass.Select((proposal, index) => proposal with
                    {
                        Start = endClock.Intervals[index].Start,
                        End = endClock.Intervals[index].End,
                        IsExactAnchor = endClock.AnchorIndices.Contains(index)
                    }).ToArray();
                    return await owner.PrepareResultAsync(firstPass, projected, runToken,
                        endClock.Summary with { UsesSpeechEnds = true }).ConfigureAwait(false);
                }
            }

            Task<IReadOnlyList<WordTiming>> ReadWindow(SrtAlignmentAudioWindow window)
            {
                owner.EnsureCurrent();
                return readWords(config, request with { Window = window }, runToken);
            }

            var proposals = await SrtAlignmentSpeechRetry.RetryAsync(firstPass, input.Duration, ReadWindow, runToken)
                .ConfigureAwait(false);
            if (proposals.Count == 1 && proposals[0].Start.HasValue)
            {
                var match = proposals[0];
                var uniqueness = await SrtAlignmentUniqueness.VerifyAsync(match, input.Duration, ReadWindow, runToken, words)
                    .ConfigureAwait(false);
                if (uniqueness != SrtAlignmentRejection.None)
                    proposals = firstPass = [new(match.Cue, null, null, 0, uniqueness == SrtAlignmentRejection.InvalidTiming
                        ? SrtAlignmentRejection.UnverifiedUniqueness : uniqueness)];
            }
            return await owner.PrepareResultAsync(firstPass, proposals, runToken).ConfigureAwait(false);
        }
        catch (EngineProcessDrainException error)
        {
            IDisposable? held = model;
            async ValueTask Retry()
            {
                await error.RetryStop().ConfigureAwait(false);
                held?.Dispose();
            }

            if (gpu != null) gpu.RequireDrain(Retry);
            else lock (gate) pendingCpuDrain = Retry;
            model = null;
            throw;
        }
        finally
        {
            model?.Dispose();
            gpu?.Complete();
            lock (gate) active = null;
            operation.Cancellation.Dispose();
            operation.Completion.SetResult();
        }
    }

    sealed record Operation(Session Session, CancellationTokenSource Cancellation)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    sealed class Session(SrtAlignmentWorkflow owner, SrtAlignmentInput input) : ISrtAlignmentSession
    {
        readonly SemaphoreSlim transition = new(1, 1);
        SrtAlignmentDraft? prepared;
        ImmutableArray<SrtTimingCueMetadata> metadata;
        int sourceInvalidated;

        public string SourcePath => input.SourcePath;
        public string AudioLabel => input.AudioLabel;
        public string DefaultLanguage => input.Language;
        public bool CanUseCuda => owner.coordinator != null;
        public SrtAlignmentDevice DefaultDevice => CanUseCuda
            && FasterWhisperDevicePolicy.RequiresGpuAdmission(input.CaptureConfig())
                ? SrtAlignmentDevice.Cuda : SrtAlignmentDevice.Cpu;
        public bool IsCurrent => Volatile.Read(ref sourceInvalidated) == 0 && input.IsCurrent();
        public Task CancelAndDrainAsync() => owner.CancelAndDrainAsync(this);

        public async Task<SrtAlignmentDraft> PrepareAsync(string language, SrtAlignmentDevice device, CancellationToken token)
        {
            if (!Enum.IsDefined(device)) throw new ArgumentOutOfRangeException(nameof(device));
            string? selectedLanguage = language?.Trim().ToLowerInvariant();
            if (selectedLanguage == null || !WhisperLanguage.LanguageToCode.ContainsValue(selectedLanguage))
                throw new ArgumentException("Choose the language spoken on the audio track.", nameof(language));

            await transition.WaitAsync(token).ConfigureAwait(false);
            try
            {
                prepared = null;
                EnsureCurrent();
                await VerifySelectedFileAsync(token).ConfigureAwait(false);
                return await owner.RunAsync(this, input, input.CaptureConfig(), selectedLanguage, device, token).ConfigureAwait(false);
            }
            finally { transition.Release(); }
        }

        public async Task<SrtAlignmentDraft> PrepareResultAsync(IReadOnlyList<SrtAlignmentProposal> firstPass,
            IReadOnlyList<SrtAlignmentProposal> proposals, CancellationToken token, SrtClockCorrection? clock = null)
        {
            token.ThrowIfCancellationRequested();
            EnsureCurrent();
            await VerifySelectedFileAsync(token).ConfigureAwait(false);

            var rows = BuildRows(proposals, clock);
            metadata = input.CaptureMetadata();
            bool hasProposals = rows.Any(row => row.ProposedStart.HasValue);
            bool timingConflicts = hasProposals && !HasValidOrder(rows);
            if (timingConflicts)
            {
                // Optional additional speech must never take away an already exportable result.
                var original = BuildRows(firstPass);
                if (original.Any(row => row.ProposedStart.HasValue) && HasValidOrder(original))
                {
                    rows = original;
                    timingConflicts = false;
                    clock = null;
                }
            }
            bool canSave = hasProposals && !timingConflicts;
            var translations = metadata.Where(item => !string.IsNullOrWhiteSpace(item.TranslatedText))
                .Select(item => item.Id).ToHashSet();
            token.ThrowIfCancellationRequested();
            EnsureCurrent();
            return prepared = new SrtAlignmentDraft(rows, canSave,
                canSave && rows.All(row => translations.Contains(row.Id)))
            {
                HasTimingConflicts = timingConflicts,
                ClockCorrection = canSave ? clock : null
            };
        }

        public async Task SaveAsync(SrtAlignmentDraft draft, string path, bool translated, bool acceptUnmatched, CancellationToken token)
        {
            await transition.WaitAsync(token).ConfigureAwait(false);
            Operation? save = null;
            try
            {
                save = owner.BeginSave(this, token);
                token = save.Cancellation.Token;
                token.ThrowIfCancellationRequested();
                EnsureCurrent();
                if (!ReferenceEquals(prepared, draft)) throw new InvalidOperationException("Prepare a new alignment first.");
                if (!(translated ? draft.CanSaveTranslated : draft.CanSaveOriginal))
                    throw new InvalidOperationException("This result cannot be exported. Check the highlighted cues and translation availability.");
                if (draft.NeedsTimingAcknowledgment && !acceptUnmatched)
                    throw new InvalidOperationException(draft.ClockCorrection != null
                        ? "Confirm applying the inferred timing correction to all cues."
                        : "Confirm keeping the unresolved cues at their original times.");

                string destination = Path.GetFullPath(path);
                var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!string.Equals(Path.GetExtension(destination), ".srt", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(destination, Path.GetFullPath(input.SourcePath), pathComparison))
                    throw new IOException("Choose a new .srt file other than the original subtitle file.");

                string stage = Path.Combine(Path.GetDirectoryName(destination)!, $".llplayer-alignment-{Guid.NewGuid():N}.tmp");
                bool ownsStage = false;
                try
                {
                    byte[] contents = translated ? BuildTranslation(draft) : input.Document.Rewrite(draft.Rows
                        .Where(row => row.ProposedStart.HasValue && row.ProposedEnd.HasValue)
                        .ToDictionary(row => row.Id, row => (row.ProposedStart!.Value, row.ProposedEnd!.Value)));
                    await using (var provisional = new FileStream(stage, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, bufferSize: 4096, useAsync: true))
                    {
                        ownsStage = true;
                        await provisional.WriteAsync(contents, token).ConfigureAwait(false);
                    }
                    if (!input.TryPublish(() => !token.IsCancellationRequested && HasCurrentSourceFile() &&
                        (!translated || SameMetadata(metadata, input.CaptureMetadata())),
                        () =>
                        {
                            token.ThrowIfCancellationRequested();
                            File.Move(stage, destination, overwrite: false);
                            ownsStage = false;
                        }))
                        throw Stale();
                }
                finally
                {
                    try { if (ownsStage) File.Delete(stage); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            finally
            {
                transition.Release();
                if (save != null)
                    owner.CompleteSave(save);
            }
        }

        byte[] BuildTranslation(SrtAlignmentDraft draft)
        {
            var text = metadata.ToDictionary(item => item.Id, item => item.TranslatedText);
            var lines = draft.Rows.Select(row => new SubtitleExportLine(row.ProposedStart ?? row.OriginalStart,
                row.ProposedEnd ?? row.OriginalEnd, text[row.Id]!)).ToArray();
            return Encoding.UTF8.GetBytes(SubtitleExporter.Build(lines, SubtitleExportFormat.Srt));
        }

        public void EnsureCurrent()
        {
            if (!IsCurrent) throw Stale();
        }

        async Task VerifySelectedFileAsync(CancellationToken token)
        {
            await using var stream = File.Open(SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await VerifySelectedFileAsync(stream, token).ConfigureAwait(false);
        }

        async Task VerifySelectedFileAsync(Stream stream, CancellationToken token)
        {
            string sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
            if (!string.Equals(sha, input.Document.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Volatile.Write(ref sourceInvalidated, 1);
                throw Stale();
            }
        }

        bool HasCurrentSourceFile()
        {
            try
            {
                using var stream = File.Open(SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                string sha = Convert.ToHexString(SHA256.HashData(stream));
                if (string.Equals(sha, input.Document.Sha256, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Volatile.Write(ref sourceInvalidated, 1);
            return false;
        }

        static OperationCanceledException Stale() =>
            new("The media, selected track or loaded subtitle file changed. Reopen the file and prepare a new alignment.");
    }

    private static SrtAlignmentRow[] BuildRows(IReadOnlyList<SrtAlignmentProposal> proposals, SrtClockCorrection? clock = null) =>
        proposals.Select(proposal => new SrtAlignmentRow(proposal.Cue.Id, proposal.Cue.Text,
            proposal.Cue.Start, proposal.Cue.End, proposal.Start, proposal.End, proposal.Coverage,
            clock == null ? Explain(proposal.Rejection) : ExplainClockEvidence(proposal, clock.UsesSpeechEnds))).ToArray();

    private static string ExplainClockEvidence(SrtAlignmentProposal proposal, bool usesSpeechEnds) => proposal switch
    {
        { IsExactAnchor: true } when usesSpeechEnds => "Speech end anchor for track correction",
        { IsExactAnchor: true, OneRepeatedWordElided: true } => "Speech anchor (repeated word omitted)",
        { IsExactAnchor: true } => "Speech anchor for track correction",
        _ => "Timing inferred from track correction"
    };

    internal static bool HasValidOrder(IReadOnlyList<SrtAlignmentRow> rows)
    {
        TimeSpan lastEnd = TimeSpan.Zero;
        TimeSpan lastChangedEnd = TimeSpan.Zero;
        foreach (SrtAlignmentRow row in rows)
        {
            TimeSpan start = row.ProposedStart ?? row.OriginalStart;
            TimeSpan end = row.ProposedEnd ?? row.OriginalEnd;
            bool changed = start != row.OriginalStart || end != row.OriginalEnd;
            if (start < TimeSpan.Zero || end <= start || start < (changed ? lastEnd : lastChangedEnd))
                return false;

            if (end > lastEnd) lastEnd = end;
            if (changed && end > lastChangedEnd) lastChangedEnd = end;
        }
        return true;
    }

    static bool SameMetadata(ImmutableArray<SrtTimingCueMetadata> original, ImmutableArray<SrtTimingCueMetadata> current)
    {
        if (original.Length != current.Length) return false;
        for (int i = 0; i < original.Length; i++)
        {
            var a = original[i];
            var b = current[i];
            if (a.Id != b.Id || a.Text != b.Text || a.TranslatedText != b.TranslatedText
                || !Equals(a.Language, b.Language) || a.SpeakerId != b.SpeakerId || a.AssignedVoiceId != b.AssignedVoiceId
                || !a.Styles.AsSpan().SequenceEqual(b.Styles.AsSpan()))
                return false;
        }
        return true;
    }

    static string? Explain(SrtAlignmentRejection rejection) => rejection switch
    {
        SrtAlignmentRejection.None => null,
        SrtAlignmentRejection.NoText => "No spoken text",
        SrtAlignmentRejection.MissingSpeech => "No timed words",
        SrtAlignmentRejection.Ambiguous => "Several speech matches",
        SrtAlignmentRejection.InvalidTiming => "Invalid speech interval",
        SrtAlignmentRejection.ConflictingOrder => "Conflicting cue order",
        SrtAlignmentRejection.UnverifiedUniqueness => "Could not check for repeated speech",
        _ => "Insufficient speech match"
    };
}
