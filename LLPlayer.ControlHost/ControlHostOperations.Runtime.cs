using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Diarization;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Batch;
using LLPlayer.Core.Services.Diarization;

namespace LLPlayer.ControlHost;

internal sealed partial class ControlHostOperations
{
    private void RegisterRuntime()
    {
        Job<OcrRequest>("ocr.start", "recognition", "Recognize the selected local bitmap subtitle track.", RunOcrAsync);
        Job<DiarizationRequest>("diarization.start", "diarization", "Match local speaker analysis to timed text cues.", RunDiarizationAsync);
        Read<RuntimeEmpty, IReadOnlyList<TtsVoice>>("dubbing.voices", "dubbing", "List the current dubbing voices.",
            (_, _, _) =>
            {
                var config = settings.CaptureDubbing().Engine;
                return Task.FromResult(VoiceBankResolver.ForConfig(config.DefaultVoiceId, config.CustomVoiceIds));
            });
        Job<DubbingRequest>("dubbing.render", "dubbing", "Render translated cues into a local dub track.", RenderDubbingAsync);
        Read<RuntimeEmpty, ResourceStatus>("resources.status", "models", "Read model-file and GPU ownership status.",
            (_, _, _) => Task.FromResult(new ResourceStatus(runtime.Resources.IsInUse, runtime.Gpu.PendingDrain,
                runtime.Diarization.HasPendingDrain)));
        RegisterBatch();
    }

    private async Task<ControlJobOutcome> RunOcrAsync(ControlJobContext context, OcrRequest args, CancellationToken ct)
    {
        string path = Absolute(args.MediaPath);
        await context.VerifyInputAsync(path, ct);
        var snapshot = settings.CaptureWorkflowSettings();
        context.VerifyIdentity("recognition_settings", snapshot.RecognitionIdentity);
        Language? language = args.Language is null ? null : Language.Get(args.Language);
        if (language == Language.Unknown)
            throw new ControlJobException("unknown_language", "Choose a valid OCR language.");
        var result = await runtime.Workflows.RecognizeBitmapAsync(path, args.StreamIndex, args.External, args.Slot, language,
            new InlineProgress<int>(_ => context.Report("recognizing_text")), ct, savedConfig: snapshot.Config);
        await context.VerifyInputAsync(path, ct);
        return ControlTranslationOperations.SaveDocument(context, ControlCueDocument.Capture(context.JobId,
            $"{path}|{args.StreamIndex}|{args.Slot}|{snapshot.RecognitionIdentity}", result.SourceLanguage.ISO6391, result.Subtitles));
    }

    private async Task<ControlJobOutcome> RunDiarizationAsync(ControlJobContext context, DiarizationRequest args, CancellationToken ct)
    {
        string path = Absolute(args.MediaPath);
        ArgumentOutOfRangeException.ThrowIfNegative(args.StreamIndex);
        await context.VerifyInputAsync(path, ct);
        string python = Environment.GetEnvironmentVariable("LLPLAYER_DIARIZATION_PYTHON") ?? "";
        string model = Environment.GetEnvironmentVariable("LLPLAYER_DIARIZATION_MODEL") ?? "";
        string device = Environment.GetEnvironmentVariable("LLPLAYER_DIARIZATION_DEVICE")?.Trim().ToLowerInvariant() ?? "cpu";
        if (string.IsNullOrWhiteSpace(python) || string.IsNullOrWhiteSpace(model))
            throw new ControlJobException("diarization_prerequisite", "Configure the local pyannote Python and model paths before starting diarization.");
        context.VerifyIdentity("diarization_environment", $"{python}|{model}|{device}");
        DiarizationCueRequest[] cues = args.Cues ?? [];
        if (args.JobId is { } jobId)
        {
            var document = await documents.GetDocumentAsync(context.Request.Scope, new(jobId, args.Revision ?? ""), ct);
            cues = document.Cues.Where(cue => !cue.IsBitmap && !string.IsNullOrWhiteSpace(cue.Text) && cue.End > cue.Start)
                .Select(cue => new DiarizationCueRequest(cue.CueId, cue.Start.Ticks, cue.End.Ticks)).ToArray();
        }
        if (cues.Length == 0 || cues.Length > 32768 || cues.Any(cue => cue.CueId == Guid.Empty || cue.EndTicks <= cue.StartTicks)
            || cues.Select(cue => cue.CueId).Distinct().Count() != cues.Length)
            throw new ControlJobException("invalid_cues", "Supply distinct timed text cues for speaker analysis.");

        long shift = checked(args.SubtitleDelayTicks - args.AudioDelayTicks);
        var timings = cues.Select(cue => new DiarizationCue(TimeSpan.FromTicks(checked(cue.StartTicks + shift)),
            TimeSpan.FromTicks(checked(cue.EndTicks + shift)))).ToArray();
        runtime.EnsureEngine();
        IDisposable? resource = runtime.Resources.AcquireUse();
        IReadOnlyList<string?> labels;
        try
        {
            labels = await runtime.Diarization.RunAsync(new LocalDiarizationEngine(python, model, device),
                new(path, args.StreamIndex), timings, device == "cuda", ct);
        }
        catch (EngineProcessDrainException failure)
        {
            IDisposable? held = resource;
            resource = null;
            throw new EngineProcessDrainException(failure.Message, failure, async () =>
            {
                if (!await runtime.Diarization.RetryDrainAsync()) throw new InvalidOperationException("Diarization cleanup remains active.");
                held?.Dispose();
            });
        }
        finally { resource?.Dispose(); }
        await context.VerifyInputAsync(path, ct);
        return Done(new DiarizationResult(cues.Select((cue, index) => new SpeakerLabel(cue.CueId, labels[index])).ToArray()));
    }

    private async Task<ControlJobOutcome> RenderDubbingAsync(ControlJobContext context, DubbingRequest args, CancellationToken ct)
    {
        string media = Absolute(args.MediaPath);
        string output = Absolute(args.Path);
        if (BatchWorkflow.PathComparer.Equals(media, output))
            throw new ControlJobException("output_conflict", "The output must differ from the input media file.");
        await context.VerifyInputAsync(media, ct);
        var snapshot = settings.CaptureWorkflowSettings();
        context.VerifyIdentity("dubbing_settings", snapshot.DubbingIdentity);
        if (context.IsRecovery && await context.TryRecoverFileAsync("dub", output, args.Overwrite, ct))
            return Done(new { path = output });
        if (!args.Overwrite && File.Exists(output))
            throw new ControlJobException("output_exists", "The dub already exists. Allow overwrite explicitly to replace it.");

        DubbingCueRequest[] detached = args.Cues ?? [];
        if (args.JobId is { } jobId)
        {
            var document = await documents.GetDocumentAsync(context.Request.Scope, new(jobId, args.Revision ?? ""), ct);
            if (!string.Equals(document.TargetLanguage, "ru", StringComparison.OrdinalIgnoreCase))
                throw new ControlJobException("dubbing_language", "The current dubbing workflow requires Russian translations.");
            detached = document.Cues.Select(cue => new DubbingCueRequest(cue.Start.Ticks, cue.End.Ticks,
                cue.TranslatedText, cue.AssignedVoiceId)).ToArray();
        }
        var cues = detached.Where(cue => cue.EndTicks > cue.StartTicks && !string.IsNullOrWhiteSpace(cue.TranslatedText))
            .Select(cue => new SubtitleData
            {
                TranslatedText = cue.TranslatedText, AssignedVoiceId = cue.AssignedVoiceId,
                StartTime = TimeSpan.FromTicks(cue.StartTicks), EndTime = TimeSpan.FromTicks(cue.EndTicks)
            }).ToArray();
        string directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        string staging = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.llplayer-dub");
        bool publishing = false;
        try
        {
            await runtime.Workflows.RenderDubbingAsync(cues, media, staging,
                new InlineProgress<DubbingProgress>(p => context.Report(p.Phase ?? "dubbing",
                    p.TotalLines > 0 ? (double)p.CompletedLines / p.TotalLines : null)), ct,
                waiting => context.Report(waiting ? "waiting_for_gpu" : "dubbing", status: waiting
                    ? ControlJobStatus.WaitingForResource : ControlJobStatus.Running), savedConfig: snapshot.Config);
            await context.VerifyInputAsync(media, ct);
            publishing = true;
            await context.PublishFileAsync("dub", staging, output, args.Overwrite, ct);
        }
        finally
        {
            if (!publishing && File.Exists(staging)) File.Delete(staging);
        }
        return Done(new { path = output });
    }

    internal sealed record RuntimeEmpty;
    internal sealed record OcrRequest(string MediaPath, int StreamIndex, bool External = false, int Slot = 0, string? Language = null);
    internal sealed record DiarizationRequest(string MediaPath, int StreamIndex, Guid? JobId = null, string? Revision = null,
        DiarizationCueRequest[]? Cues = null, long AudioDelayTicks = 0, long SubtitleDelayTicks = 0);
    internal sealed record DiarizationCueRequest(Guid CueId, long StartTicks, long EndTicks);
    internal sealed record SpeakerLabel(Guid CueId, string? SpeakerId);
    internal sealed record DiarizationResult(SpeakerLabel[] Speakers);
    internal sealed record DubbingRequest(string MediaPath, string Path, Guid? JobId = null, string? Revision = null,
        DubbingCueRequest[]? Cues = null, bool Overwrite = false);
    internal sealed record DubbingCueRequest(long StartTicks, long EndTicks, string? TranslatedText, string? AssignedVoiceId);
    internal sealed record ResourceStatus(bool ModelFilesInUse, string? AwaitingGpuCleanup, bool AwaitingDiarizationCleanup);
}
