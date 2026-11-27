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
    public int MatchedCount { get; } = rows.Count(row => row.ProposedStart.HasValue);
    public int TotalCount => Rows.Count;
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
            Task? completion = null;
            Task? cancellation = null;
            lock (gate)
            {
                if (active != null)
                {
                    if (owner != null && !ReferenceEquals(owner, active.Session)) return;
                    cancellation = active.Cancellation.CancelAsync();
                    completion = active.Completion.Task;
                }
            }

            if (cancellation != null) await cancellation.ConfigureAwait(false);
            if (completion != null) await completion.ConfigureAwait(false);

            Func<ValueTask>? stop;
            lock (gate) stop = pendingCpuDrain;
            if (stop != null)
            {
                await stop().ConfigureAwait(false);
                lock (gate) pendingCpuDrain = null;
            }
            if (coordinator != null) await coordinator.RetryDrainAsync(GpuOperation).ConfigureAwait(false);
        }
        finally { drainGate.Release(); }
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
            IReadOnlyList<WordTiming> words = await readWords(config,
                new(input.MediaPath, input.StreamIndex, language, device), runToken).ConfigureAwait(false);
            return await owner.PrepareResultAsync(words, runToken).ConfigureAwait(false);
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

        public async Task<SrtAlignmentDraft> PrepareResultAsync(IReadOnlyList<WordTiming> words, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            EnsureCurrent();
            await VerifySelectedFileAsync(token).ConfigureAwait(false);

            IReadOnlyList<SrtAlignmentProposal> proposals = SrtAudioAlignment.Align(input.Document.Cues, words, input.Duration, token);
            var rows = proposals.Select(proposal => new SrtAlignmentRow(proposal.Cue.Id, proposal.Cue.Text,
                proposal.Cue.Start, proposal.Cue.End, proposal.Start, proposal.End, proposal.Coverage,
                Explain(proposal.Rejection))).ToArray();
            metadata = input.CaptureMetadata();
            bool canSave = rows.Any(row => row.ProposedStart.HasValue) && HasValidOrder(rows);
            var translations = metadata.Where(item => !string.IsNullOrWhiteSpace(item.TranslatedText))
                .Select(item => item.Id).ToHashSet();
            token.ThrowIfCancellationRequested();
            EnsureCurrent();
            return prepared = new SrtAlignmentDraft(rows, canSave,
                canSave && rows.All(row => translations.Contains(row.Id)));
        }

        public async Task SaveAsync(SrtAlignmentDraft draft, string path, bool translated, bool acceptUnmatched, CancellationToken token)
        {
            await transition.WaitAsync(token).ConfigureAwait(false);
            try
            {
                EnsureCurrent();
                if (!ReferenceEquals(prepared, draft)) throw new InvalidOperationException("Prepare a new alignment first.");
                if (!(translated ? draft.CanSaveTranslated : draft.CanSaveOriginal))
                    throw new InvalidOperationException("This result cannot be exported. Check the highlighted cues and translation availability.");
                if (draft.MatchedCount != draft.TotalCount && !acceptUnmatched)
                    throw new InvalidOperationException("Confirm keeping the unresolved cues at their original times.");

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
            finally { transition.Release(); }
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
        _ => "Insufficient speech match"
    };
}
