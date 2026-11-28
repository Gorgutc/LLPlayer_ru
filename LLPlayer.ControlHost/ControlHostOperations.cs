using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Batch;
using FlyleafLib.MediaPlayer.Translation;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Services.Models;

namespace LLPlayer.ControlHost;

internal sealed partial class ControlHostOperations(ControlOperationCatalog catalog, ControlJobService jobs,
    Dictionary<string, ControlJobHandler> handlers, ControlGuiBroker broker,
    ControlSettingsService settings, ControlLanguageRuntime runtime)
{
    private readonly ControlTranslationOperations documents = new(jobs);
    private readonly ConcurrentDictionary<Guid, PauseTokenSource> recognition = new();

    public void Register(string configDir, string stateDir)
    {
        foreach (var schema in ControlGuiOperations.All.Concat(ControlExtendedGuiOperations.All))
        {
            if (ControlGuiLifecycleOperations.IsApplicationLifecycle(schema.Operation.Id))
                continue; // The host also waits for process registration or exit.
            if (schema.Operation.Id is "words.save" or "words.update" or "words.remove" or "words.clear")
                continue; // These operations can also run against the shared word owner without a window.
            if (schema.Operation.Id is "dubbing.cue_voice.set" or "dubbing.cue_voice.clear")
            {
                // Persistence is controlled by the existing voice owner. A durable request would
                // retain the assignment even when the user has disabled per-line persistence.
                catalog.Add(schema.Operation, schema.Arguments, schema.Result,
                    (request, _, ct) => broker.ExecuteAsync(request, ct),
                    scope => scope.SessionId is null ? "session_required" : broker.GetUnavailableReason(scope));
                continue;
            }
            if (schema.Operation.Mutates)
            {
                var operation = schema.Operation with { Completion = ControlOperationCompletion.Job };
                handlers.Add(operation.Id, async (context, ct) =>
                {
                    context.RequireOriginalHost();
                    return FromResult(await broker.ExecuteAsync(context.Request, ct));
                });
                Type result = operation.Id is "ui.surface.open" or "ui.sidebar.search.focus" ? schema.Result : typeof(ControlResult);
                catalog.Add(operation, schema.Arguments, result,
                    (request, argument, ct) => request.Scope.SessionId is null
                        ? Task.FromResult(ControlResult.Fail("session_required", "Select an explicit GUI session for this mutation."))
                        : jobs.EnqueueAsync(Normalize(request, argument), ct), broker.GetUnavailableReason,
                    requireAvailabilityAtInvocation: false);
            }
            else
            {
                catalog.Add(schema.Operation, schema.Arguments, schema.Result,
                    (request, _, ct) => broker.ExecuteAsync(request, ct), broker.GetUnavailableReason);
            }
        }
        Read<ControlSettingsFamilyRequest, ControlSettingsSchema>("settings.schema", "settings", "Describe editable fields.",
            (_, args, _) => Task.FromResult(settings.Schema(args.Family)));
        Read<NoArguments, IReadOnlyList<string>>("settings.families", "settings", "List available settings families.",
            (_, _, _) => Task.FromResult(settings.Families));
        Read<ControlSettingsFamilyRequest, ControlSettingsSnapshot>("settings.read", "settings", "Read saved and effective settings.",
            (_, args, _) => Task.FromResult(settings.Read(args.Family)));
        Read<ControlSettingsPatch, ControlSettingsSnapshot>("settings.validate", "settings", "Validate a settings patch.",
            (request, args, _) => Task.FromResult(settings.Validate(args, request.ExpectedRevision)));
        Job<ControlSettingsPatch>("settings.apply", "settings", "Persist a validated settings patch.",
            (context, args, _) => Task.FromResult(Done(settings.Apply(args, context.Request.ExpectedRevision))),
            settings.ValidatePatchAdmission);
        Job<ControlCredentialSet>("settings.credentials.set", "settings", "Persist a credential through its settings owner.",
            (context, args, _) => Task.FromResult(Done(settings.SetCredential(args, context.Request.ExpectedRevision))));
        Job<ControlCredentialClear>("settings.credentials.clear", "settings", "Clear a credential through its settings owner.",
            (context, args, _) => Task.FromResult(Done(settings.ClearCredential(args, context.Request.ExpectedRevision))));
        Job<BackendRequest>("backends.set_requested", "backends", "Save requested decode, device, and presentation options.",
            (context, args, _) => Task.FromResult(Done(settings.Apply(BackendPatch(args), context.Request.ExpectedRevision))),
            args => settings.ValidatePatchAdmission(BackendPatch(args)));

        Read<ProbeRequest, ProbeResult>("media.probe", "media", "List streams and revision of a local finite media source.",
            (_, args, ct) => ProbeAsync(args, ct));
        Job<RecognitionRequest>("recognition.start", "recognition",
            "Recognize a selected audio stream. Omission suggestions are enabled unless detectAsrOmissions is false.",
            RecognizeAsync);
        Read<ControlOmissionRead, ControlOmissionPage>("recognition.omissions.read", "recognition",
            "Read timed omission suggestions separately from the retained original cue document.",
            (request, args, ct) => ReadRecognitionOmissionsAsync(request.Scope, args, ct));
        Read<JobIdRequest, ControlResult>("recognition.pause", "recognition", "Pause recognition at a chunk boundary.",
            (request, args, ct) => UpdatePauseAsync(request.Scope, args.JobId, true, ct), mutates: true);
        Read<JobIdRequest, ControlResult>("recognition.resume", "recognition", "Continue paused recognition.",
            (request, args, ct) => UpdatePauseAsync(request.Scope, args.JobId, false, ct), mutates: true);
        Read<ControlDocumentRead, ControlResult>("subtitles.document.read", "subtitles", "Read detached subtitle cues from a job.",
            (request, args, ct) => documents.ReadDocumentAsync(request.Scope, args, ct));
        Job<ProviderTranslationRequest>("translation.start", "translation", "Translate the document or a selected cue range using the configured provider.",
            TranslateAsync);
        Job<BeginTranslationDraftRequest>("translation.document.begin", "translation", "Prepare an existing document or range for external translation.",
            async (context, args, ct) =>
            {
                var input = await documents.GetDocumentAsync(context.Request.Scope, new(args.JobId, args.Revision), ct);
                context.VerifyIdentity("source_document", input.Revision);
                return ControlTranslationOperations.RestoreHarnessTranslation(context)
                    ?? ControlTranslationOperations.AwaitHarnessTranslation(context, input, args.TargetLanguage, args.Start, args.Count);
            });
        Read<ControlTranslationRead, ControlResult>("translation.document.read", "translation", "Read a page for external translation.",
            (request, args, ct) => documents.ReadDraftAsync(request.Scope, args, ct));
        Read<ControlTranslationSubmit, ControlResult>("translation.document.apply", "translation",
            "Submit draft translations by stable cue ID.",
            (request, args, ct) => documents.SubmitAsync(request.Scope, request.ExpectedRevision, args, ct), mutates: true);
        Read<ControlTranslationCommit, ControlResult>("translation.document.commit", "translation",
            "Publish a complete draft at the specified revision.",
            (request, args, ct) => documents.CommitAsync(request.Scope, request.ExpectedRevision, args, ct), mutates: true);
        Job<ExportRequest>("export.subtitles", "export", "Write an existing subtitle document atomically.",
            ExportAsync);
        Job<PipelineRequest>("workflow.create_subtitles", "recognition",
            "Recognize, translate and export each listed media file. Omission suggestions are kept separate.",
            RunPipelineAsync);

        Read<ModelListRequest, IReadOnlyList<ModelEntry>>("models.list", "models", "List models and installation status.",
            (_, args, _) => Task.FromResult(runtime.Models.List(args.Kind, args.WhisperDirectory)));
        Read<ModelListRequest, string>("models.directory", "models", "Find the configured model directory.",
            (_, args, _) => Task.FromResult(runtime.Models.GetDirectory(args.Kind, args.WhisperDirectory)));
        Job<ModelRequest>("models.download", "models", "Download and install a listed model explicitly.",
            async (context, args, ct) =>
            {
                await runtime.Models.DownloadAsync(args.Id, new InlineProgress<ModelDownloadProgress>(progress =>
                    context.Report(progress.Stage, progress.TotalBytes is > 0
                        ? Math.Clamp((double)progress.BytesReceived / progress.TotalBytes.Value, 0, 1) : null)), ct, args.WhisperDirectory);
                return Done(new { args.Id });
            });
        RegisterRuntime();
        RegisterWords(configDir);
        RegisterDiagnostics();
        RegisterWindow(configDir, stateDir);
        RegisterApplicationLifecycle();
        Job<ModelRequest>("models.remove", "models", "Remove a listed managed model when it is not in use.",
            async (_, args, ct) =>
            {
                await runtime.Models.RemoveAsync(args.Id, ct, args.WhisperDirectory);
                return Done(new { args.Id });
            });
    }

    public static bool RequiresCleanResources(string operation)
        => operation is "recognition.start" or "workflow.create_subtitles" or "ocr.start" or "batch.start" or "batch.retry"
            or "watch.start" or "watch.process" or "diarization.start" or "dubbing.render" or "models.remove";

    private async Task<ProbeResult> ProbeAsync(ProbeRequest args, CancellationToken ct)
    {
        var path = Absolute(args.MediaPath);
        if (!File.Exists(CheckedSourcePath(path, args.RequirePhysicalSource)))
            throw new ControlException(new("source_unavailable", "The source file is unavailable.", true));
        var before = await ControlJobContext.FingerprintInputAsync(CheckedSourcePath(path, args.RequirePhysicalSource), ct);
        var result = await runtime.Workflows.ProbeAsync(CheckedSourcePath(path, args.RequirePhysicalSource), args.StreamIndex, ct);
        var after = await ControlJobContext.FingerprintInputAsync(CheckedSourcePath(path, args.RequirePhysicalSource), ct);
        if (before != after)
            throw new ControlException(new("stale_media", "The source changed while its streams were being probed.", true));
        return new(result.MediaPath, result.StreamIndex, result.MediaType, result.Language, result.Duration,
            result.Tracks, result.LanguageTagged, before);
    }

    private async Task<ControlJobOutcome> RecognizeAsync(ControlJobContext context, RecognitionRequest args, CancellationToken ct)
    {
        var path = Absolute(args.MediaPath);
        context.Report("checking_input");
        context.VerifyIdentity(ControlJobContext.RecognitionSourcePathIdentity, PhysicalSourcePath(path, args.RequirePhysicalSource));
        await context.VerifyInputAsync(path, ct, args.SourceRevision);
        var saved = settings.CaptureWorkflowSettings();
        context.VerifyIdentity("recognition_settings", saved.RecognitionIdentity);
        if (ControlTranslationOperations.RestoreHarnessTranslation(context) is { } existing)
            return existing;
        if (args.StartTicks < 0 || args.Slot is not (null or 0 or 1) || args.StreamIndex is < 0)
            throw new ControlJobException("invalid_arguments", "The selected stream, position, or subtitle slot is invalid.");
        Language? sourceLanguage = null;
        if (args.SourceLanguage is { } sourceLanguageCode)
        {
            if (sourceLanguageCode.Length != 2 || !sourceLanguageCode.All(char.IsAsciiLetter)
                || (sourceLanguage = Language.Get(sourceLanguageCode)) == Language.Unknown)
                throw new ControlJobException("invalid_arguments", "The source language must be a known two-letter language code.");
        }

        var pause = new PauseTokenSource();
        recognition[context.JobId] = pause;
        try
        {
            string sourceStamp = $"{path}|{args.StreamIndex}|{args.StartTicks}|{sourceLanguage?.ISO6391}|{saved.RecognitionIdentity}";
            var preview = new List<SubtitleData>();
            var previewClock = Stopwatch.StartNew();
            bool previewSaved = false;
            void ReportAsr(BatchAsrProgress progress)
            {
                if (progress.NewCues is { Count: > 0 } additions)
                {
                    preview.AddRange(additions);
                    if (!previewSaved || previewClock.ElapsedMilliseconds >= 500)
                    {
                        var current = ControlCueDocument.Capture(context.JobId, sourceStamp, progress.SourceLanguage?.ISO6391,
                            preview.OrderBy(cue => cue.StartTime).ToArray());
                        ControlTranslationOperations.SaveDocumentPreview(context, current);
                        previewSaved = true;
                        previewClock.Restart();
                    }
                }
                context.Report("recognizing", progress.Duration > TimeSpan.Zero
                    ? Math.Clamp(progress.Position.TotalSeconds / progress.Duration.TotalSeconds, 0, 1) : null);
            }
            var result = await runtime.Workflows.TranscribeAsync(CheckedSourcePath(path, args.RequirePhysicalSource), args.StreamIndex, args.Engine,
                TimeSpan.FromTicks(args.StartTicks), args.Slot, pause.Token,
                new InlineProgress<BatchAsrProgress>(ReportAsr), ct,
                waiting => context.Report(waiting ? "waiting_for_gpu" : "recognizing", status: waiting
                    ? ControlJobStatus.WaitingForResource : ControlJobStatus.Running), saved.Config, sourceLanguage,
                detectAsrOmissions: args.DetectAsrOmissions ?? true);
            await context.VerifyInputAsync(CheckedSourcePath(path, args.RequirePhysicalSource), ct);
            context.VerifyIdentity(ControlJobContext.RecognitionSourcePathIdentity, PhysicalSourcePath(path, args.RequirePhysicalSource));
            var document = ControlCueDocument.Capture(context.JobId, sourceStamp, result.SourceLanguage.ISO6391, result.Subtitles);
            var omissions = ControlRecognitionOmissions.Capture(result.OmissionReport, args.DetectAsrOmissions ?? true);
            return args.ExternalTargetLanguage is { } target
                ? ControlTranslationOperations.AwaitHarnessTranslation(context, document, target, omissions: omissions)
                : ControlTranslationOperations.SaveDocument(context, document, omissions);
        }
        finally
        {
            recognition.TryRemove(context.JobId, out _);
        }
    }

    private async Task<ControlOmissionPage> ReadRecognitionOmissionsAsync(ControlScope scope, ControlOmissionRead args,
        CancellationToken ct)
    {
        ControlJob job = await jobs.GetAsync(scope, args.JobId, ct)
            ?? throw new ControlException(new("job_not_found", "The selected job does not exist in this scope."));
        if (args.FileIndex is < 0 || (job.OperationId == "recognition.start" && args.FileIndex is > 0))
            throw new ControlException(new("invalid_arguments", "The omission suggestion file index is invalid."));

        var saved = await jobs.ReadCheckpointAsync(scope, args.JobId, ct);
        if (saved.Error is { } error)
            throw new ControlException(error);
        var omissions = job.OperationId switch
        {
            "recognition.start" => ControlTranslationOperations.ReadRecognitionOmissions(saved.Value),
            "workflow.create_subtitles" => ReadPipelineOmissions(saved.Value, args.FileIndex ?? 0),
            _ => null
        };
        return omissions?.Read(args) ?? throw new ControlException(new("omissions_unavailable",
            "The selected job has no retained omission suggestion report.",
            job.Status is ControlJobStatus.Accepted or ControlJobStatus.WaitingForResource or ControlJobStatus.Running));
    }

    private async Task<ControlResult> UpdatePauseAsync(ControlScope scope, Guid id, bool paused, CancellationToken ct)
    {
        if (await jobs.GetAsync(scope, id, ct) is null || !recognition.TryGetValue(id, out var pause))
            return ControlResult.Fail("recognition_not_running", "The selected recognition job is not active.");
        pause.IsPaused = paused;
        return new(Value: ControlProtocol.Encode(new { paused }));
    }

    private async Task<ControlJobOutcome> TranslateAsync(ControlJobContext context, ProviderTranslationRequest args,
        CancellationToken ct)
    {
        var input = await documents.GetDocumentAsync(context.Request.Scope, new(args.JobId, args.Revision), ct);
        var saved = settings.CaptureWorkflowSettings();
        ValidateTranslationSettings(context, saved.TranslationEnabled, saved.TranslationIdentity);
        return ControlTranslationOperations.SaveDocument(context,
            await TranslateDocumentAsync(context, input, args.TargetLanguage, args.SourceLanguage, saved.Config, ct,
                args.Start, args.Count));
    }

    private static void ValidateTranslationSettings(ControlJobContext context, bool enabled, string identity)
    {
        if (!enabled)
            throw new ControlJobException("provider_disabled", "Configure and enable a subtitle translation provider.");
        context.VerifyIdentity("translation_settings", identity);
    }

    private async Task<ControlCueDocument> TranslateDocumentAsync(ControlJobContext context,
        ControlCueDocument input, TargetLanguage target, string? source, Config config, CancellationToken ct,
        int start = 0, int? count = null)
    {
        var selected = input.SelectTranslationCueOrdinals(start, count).Select(index => input.Cues[index]).ToArray();
        var cues = selected.Select(item => new SubtitleData
        {
            Text = item.Text,
            StartTime = item.Start,
            EndTime = item.End
        }).ToArray();
        await runtime.Workflows.TranslateDocumentAsync(cues, Language.Get(source ?? input.SourceLanguage ?? "und"),
            target, new InlineProgress<(int Completed, int Total)>(p =>
                context.Report("translating", p.Total == 0 ? null : (double)p.Completed / p.Total)), ct, config);
        var translations = selected.Select((item, index) => (item, translated: cues[index].TranslatedText))
            .Where(value => !string.IsNullOrWhiteSpace(value.item.Text) && value.translated is not null)
            .ToDictionary(value => value.item.CueId, value => value.translated!);
        if (translations.Count != selected.Length)
            throw new ControlJobException("translation_incomplete", "The provider did not return all requested subtitle lines.");
        return input.WithTranslation(target.ToLanguageTag(), translations);
    }

    private async Task<ControlJobOutcome> ExportAsync(ControlJobContext context, ExportRequest args, CancellationToken ct)
    {
        var document = await documents.GetDocumentAsync(context.Request.Scope, new(args.JobId, args.Revision), ct);
        var (path, guardedSource) = args.Path is { } output
            ? (Absolute(output), (string?)null) : await InferExportSidecarAsync(context, args, document, ct);
        var staged = ControlSubtitleExport.Prepare(document, args.Format, args.Text, args.Utf8Bom, args.SpeakerLabels);
        Action<string>? validatePublication = guardedSource is not null
            ? stage => ValidateGuardedSidecar(guardedSource, path, stage) : null;
        await staged.PublishAsync(context, path, args.Overwrite, ct, validatePublication: validatePublication);
        return Done(new { path, sha256 = staged.ComputeSha256() });
    }

    private async Task<(string Path, string? GuardedSource)> InferExportSidecarAsync(ControlJobContext context, ExportRequest args,
        ControlCueDocument document, CancellationToken ct)
    {
        if (args.Format != SubtitleExportFormat.Srt || args.Text != ControlSubtitleText.TranslatedOnly || args.Overwrite)
            throw new ControlJobException("invalid_arguments",
                "Omit path only for translated-only Russian SRT export with overwrite disabled.");
        var scope = context.Request.Scope;
        if (await jobs.GetAsync(scope, args.JobId, ct) is not { Status: ControlJobStatus.Completed }
            || document.Cues.Any(cue => !string.IsNullOrWhiteSpace(cue.Text) && string.IsNullOrWhiteSpace(cue.TranslatedText)))
            throw new ControlJobException("translation_incomplete", "An inferred sidecar requires a complete translation.");
        if (document.TargetLanguage is not ("ru" or "russian"))
            throw new ControlJobException("invalid_arguments", "The inferred SRT sidecar requires a Russian translation.");
        var source = await jobs.ReadRecognitionSourceAsync(scope, document.DocumentId, ct)
            ?? throw new ControlJobException("recognition_source_unavailable",
                "The document has no verified media origin from recognition in this scope.");
        if (!string.Equals(source.Path, PhysicalSourcePath(source.Path, source.RequirePhysicalSource), StringComparison.Ordinal))
            throw new ControlJobException("stale_media", "The original recognition path no longer names the same physical file.");
        await context.VerifyInputAsync(source.Path, ct, source.Fingerprint);
        return (RussianSidecarPath(source.Path, source.RequirePhysicalSource), source.RequirePhysicalSource ? source.Path : null);
    }

    private void Read<TArgs, TResult>(string id, string family, string description,
        Func<ControlRequest, TArgs, CancellationToken, Task<TResult>> handler, bool mutates = false) where TArgs : class
        => catalog.Add<TArgs, TResult>(new(id, family, description, mutates),
            (invocation, args, ct) => handler(invocation.Request, args, ct));

    private void Job<TArgs>(string id, string family, string description,
        Func<ControlJobContext, TArgs, CancellationToken, Task<ControlJobOutcome>> handler,
        Action<TArgs>? validateAdmission = null) where TArgs : class
    {
        handlers.Add(id, async (context, ct) =>
        {
            try
            {
                return await handler(context, context.Request.Arguments.Deserialize<TArgs>(ControlProtocol.Json)!, ct);
            }
            catch (ControlException exception)
            {
                return new(ControlJobStatus.Failed, Error: exception.Error);
            }
            catch (ControlCueException exception)
            {
                return new(ControlJobStatus.Failed, Error: new(exception.Code, exception.Message));
            }
        });
        catalog.Add<TArgs, ControlResult>(new(id, family, description, true, ControlOperationCompletion.Job),
            (invocation, args, ct) =>
            {
                validateAdmission?.Invoke(args);
                return jobs.EnqueueAsync(Normalize(invocation.Request, args), ct);
            });
    }

    private static ControlRequest Normalize(ControlRequest request, object argument)
        => request with { Arguments = ControlProtocol.Encode(argument) };

    private static ControlSettingsPatch BackendPatch(BackendRequest request)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (request.HardwareEnabled is { } enabled)
            fields["videoHardwareAccelerationEnabled"] = ControlProtocol.Encode(enabled);
        if (request.Decode is { } decode)
            fields["videoHardwareDecodeMode"] = ControlProtocol.Encode(decode);
        if (request.Device is { } device)
            fields["videoHardwareDevice"] = ControlProtocol.Encode(device);
        if (request.Presentation is { } presentation)
            fields["videoPresentationMode"] = ControlProtocol.Encode(presentation);
        if (fields.Count == 0)
            throw new ControlException(new("invalid_arguments", "Provide at least one requested backend option."));
        return new("playback", fields);
    }

    private static string Absolute(string path)
        => Path.IsPathFullyQualified(path) ? Path.GetFullPath(path)
            : throw new ControlJobException("invalid_path", "An absolute filesystem path is required.");

    private static ControlJobOutcome FromResult(ControlResult reply)
        => reply.Error switch
        {
            null => new(ControlJobStatus.Completed, reply.Value),
            { Code: "cancelled" } => new(ControlJobStatus.Cancelled, reply.Value, reply.Error),
            _ => new(ControlJobStatus.Failed, reply.Value, reply.Error)
        };

    private static ControlJobOutcome Done<T>(T value) => new(ControlJobStatus.Completed, ControlProtocol.Encode(value));

    internal sealed record ProbeRequest(string MediaPath, int? StreamIndex = null, bool RequirePhysicalSource = false);
    internal sealed record BackendRequest(bool? HardwareEnabled = null, PortableVideoDecodeMode? Decode = null,
        string? Device = null, PortableVideoPresentationMode? Presentation = null);
    internal sealed record ProbeResult(string MediaPath, int StreamIndex, MediaType MediaType, Language Language,
        TimeSpan Duration, IReadOnlyList<MediaAudioTrack> Tracks, bool LanguageTagged, string SourceRevision);
    internal sealed record NoArguments;
    internal sealed record RecognitionRequest(string MediaPath, int? StreamIndex = null,
        SubASREngineType? Engine = null, long StartTicks = 0, int? Slot = null, string? ExternalTargetLanguage = null,
        string? SourceRevision = null, string? SourceLanguage = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool RequirePhysicalSource = false,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? DetectAsrOmissions = null);
    internal sealed record JobIdRequest(Guid JobId);
    internal sealed record ProviderTranslationRequest(Guid JobId, string Revision, TargetLanguage TargetLanguage,
        string? SourceLanguage = null, int Start = 0, int? Count = null);
    internal sealed record BeginTranslationDraftRequest(Guid JobId, string Revision, string TargetLanguage,
        int Start = 0, int? Count = null);
    internal sealed record ExportRequest(Guid JobId, string Revision, SubtitleExportFormat Format, string? Path = null,
        ControlSubtitleText Text = ControlSubtitleText.Original, bool Overwrite = false, bool Utf8Bom = true,
        bool SpeakerLabels = false);
    internal sealed record ModelListRequest(ModelKind Kind, string? WhisperDirectory = null);
    internal sealed record ModelRequest(string Id, string? WhisperDirectory = null);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
